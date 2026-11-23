using ClaimsModule.Application.Abstractions;
using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Claims.Validation;
using ClaimsModule.Domain.Common;
using ClaimsModule.Domain.Reserves;
using ClaimsModule.Persistence.Conventions;
using ClaimsModule.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ClaimsModule.Persistence.Interceptors;

/// <summary>
/// Shared plumbing: runs <see cref="Apply"/> for the sync and async SaveChanges paths, after
/// DetectChanges, so the entry states are current. They are registered in this order:
/// guard → soft delete → tenant → auditable.
/// </summary>
public abstract class ClaimsSaveChangesInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Run(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Run(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    protected abstract void Apply(DbContext context);

    private void Run(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        context.ChangeTracker.DetectChanges();
        Apply(context);
    }
}

/// <summary>
/// BR-A-01 / S14.2: ClaimAuditLog rows are never updated or deleted.
/// D-22: the event-sourced fields of a reserve transaction never change after insert.
/// This is the application-level guard. The database trigger is the second line of defence.
/// </summary>
public sealed class AppendOnlyGuardInterceptor : ClaimsSaveChangesInterceptor
{
    private static readonly string[] ImmutableReserveTransactionFields =
    [
        nameof(ReserveTransaction.Amount),
        nameof(ReserveTransaction.ChangeSequence),
        nameof(ReserveTransaction.IdempotencyKey),
        nameof(ReserveTransaction.TransactionType),
        nameof(ReserveTransaction.ComponentType),
        nameof(ReserveTransaction.ChangeReason),
        nameof(ReserveTransaction.SubmittedByUserId),
        nameof(ReserveTransaction.SubmittedAt),
        nameof(ReserveTransaction.ReserveComponentId),
        nameof(ReserveTransaction.ClaimId),
    ];

    protected override void Apply(DbContext context)
    {
        foreach (var entry in context.ChangeTracker.Entries<ClaimAuditLog>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    "ClaimAuditLog is append-only: audit entries cannot be updated or deleted (BR-A-01).");
            }
        }

        foreach (var entry in context.ChangeTracker.Entries<ReserveTransaction>().Where(e => e.State == EntityState.Modified))
        {
            var changed = ImmutableReserveTransactionFields.Where(f => entry.Property(f).IsModified).ToList();
            if (changed.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Reserve history is append-only; immutable fields cannot change: {string.Join(", ", changed)} (D-22).");
            }
        }
    }
}

/// <summary>
/// S15.1 soft delete: a Deleted domain entity is turned into an update that sets IsDeleted = 1
/// and DeletedAt. That includes orphaned aggregate children, which EF marks Deleted through
/// ClientCascade. Rows are never physically deleted.
/// </summary>
public sealed class SoftDeleteInterceptor(TimeProvider clock) : ClaimsSaveChangesInterceptor
{
    protected override void Apply(DbContext context)
    {
        foreach (var entry in context.ChangeTracker.Entries<Entity>().Where(e => e.State == EntityState.Deleted).ToList())
        {
            // Go to Unchanged first, not straight to Modified: setting State = Modified marks *every*
            // column as modified (including OrganisationId and CreatedAt), which would rewrite the
            // whole row and trip the tenant guard. This way only IsDeleted and DeletedAt are updated.
            entry.State = EntityState.Unchanged;
            entry.Property(ShadowProperties.IsDeleted).CurrentValue = true;
            entry.Property(ShadowProperties.DeletedAt).CurrentValue = clock.GetUtcNow();
        }
    }
}

/// <summary>
/// Tenant isolation (S15.1, D-01): new rows are stamped with the current organisation, and an
/// existing row can never move to another organisation.
/// </summary>
public sealed class TenantInterceptor(ITenantProvider tenantProvider) : ClaimsSaveChangesInterceptor
{
    protected override void Apply(DbContext context)
    {
        var organisationId = tenantProvider.OrganisationId;
        if (organisationId == Guid.Empty)
        {
            throw new InvalidOperationException("No tenant (OrganisationId) is set for this unit of work.");
        }

        foreach (var entry in context.ChangeTracker.Entries<Entity>())
        {
            var property = entry.Property(ShadowProperties.OrganisationId);
            switch (entry.State)
            {
                case EntityState.Added when (Guid)property.CurrentValue! == Guid.Empty:
                    property.CurrentValue = organisationId;
                    break;
                case EntityState.Modified when property.IsModified:
                    throw new InvalidOperationException("OrganisationId cannot be changed on an existing row.");
            }
        }
    }
}

/// <summary>
/// Audit columns (S15.1): CreatedAt/UserCreated on insert, UpdatedAt/UserModified on update.
/// <b>Aggregate-root versioning:</b> a change to a Claim's child entity (loss event, party, risk
/// object, validation issue) also stamps the Claim row. That bumps its RowVer and UpdatedAt, so the
/// SLA job sees the activity (S12.2) and concurrent edits to the same claim conflict.
/// A change limited to the SLA columns doesn't count as activity and leaves UpdatedAt alone (D-13).
/// </summary>
public sealed class AuditableEntityInterceptor(TimeProvider clock, ICurrentUser currentUser) : ClaimsSaveChangesInterceptor
{
    private static readonly HashSet<string> SlaOnlyColumns =
    [
        nameof(Claim.IsSlaBreached),
        nameof(Claim.LastSlaBreachAt),
    ];

    protected override void Apply(DbContext context)
    {
        var now = clock.GetUtcNow();
        var userId = currentUser.UserId;

        TouchClaimRootsOfChangedChildren(context);

        foreach (var entry in context.ChangeTracker.Entries<Entity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Property(ShadowProperties.CreatedAt).CurrentValue = now;
                    entry.Property(ShadowProperties.UserCreated).CurrentValue = userId;
                    break;

                case EntityState.Modified:
                    ProtectCreationColumns(entry);
                    if (entry.Entity is Claim && IsSlaOnlyChange(entry))
                    {
                        break;
                    }

                    entry.Property(ShadowProperties.UpdatedAt).CurrentValue = now;
                    entry.Property(ShadowProperties.UserModified).CurrentValue = userId;
                    break;
            }
        }
    }

    private static void TouchClaimRootsOfChangedChildren(DbContext context)
    {
        var changedClaimIds = context.ChangeTracker.Entries<Entity>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(e => e.Entity switch
            {
                LossEvent l => l.ClaimId,
                ClaimParty p => p.ClaimId,
                ClaimRiskObject r => r.ClaimId,
                ClaimValidationIssue i => i.ClaimId,
                _ => Guid.Empty,
            })
            .Where(id => id != Guid.Empty)
            .ToHashSet();

        foreach (var claimEntry in context.ChangeTracker.Entries<Claim>()
                     .Where(e => e.State == EntityState.Unchanged && changedClaimIds.Contains(e.Entity.Id)))
        {
            // Marking UpdatedAt modified makes EF issue an UPDATE on the root, which bumps its RowVer.
            claimEntry.Property(ShadowProperties.UpdatedAt).IsModified = true;
        }

        // The same aggregate-root versioning for ClaimReserveComponent: a change to only a
        // ReserveTransaction (posting, reject, retract, requeue) must still bump the component's
        // RowVer, or two concurrent writers to the same transaction both commit (found by the
        // concurrent GL posting test in P3 task 26).
        var changedComponentIds = context.ChangeTracker.Entries<ReserveTransaction>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(e => e.Entity.ReserveComponentId)
            .ToHashSet();

        foreach (var componentEntry in context.ChangeTracker.Entries<ClaimReserveComponent>()
                     .Where(e => e.State == EntityState.Unchanged && changedComponentIds.Contains(e.Entity.Id)))
        {
            componentEntry.Property(ShadowProperties.UpdatedAt).IsModified = true;
        }
    }

    private static bool IsSlaOnlyChange(EntityEntry entry) =>
        entry.Properties.Where(p => p.IsModified).All(p => SlaOnlyColumns.Contains(p.Metadata.Name));

    private static void ProtectCreationColumns(EntityEntry entry)
    {
        entry.Property(ShadowProperties.CreatedAt).IsModified = false;
        entry.Property(ShadowProperties.UserCreated).IsModified = false;
    }
}
