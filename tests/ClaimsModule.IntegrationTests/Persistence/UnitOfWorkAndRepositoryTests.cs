using ClaimsModule.Application.Abstractions.Audit;
using ClaimsModule.Application.Abstractions.Persistence;
using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Claims.Events;
using ClaimsModule.Domain.Claims.Validation;
using ClaimsModule.Domain.Common;
using ClaimsModule.Domain.Reserves;
using ClaimsModule.Domain.Reserves.Events;
using ClaimsModule.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClaimsModule.IntegrationTests.Persistence;

/// <summary>
/// The Unit of Work, repositories and audit service, wired through AddPersistence() against SQL
/// Server: aggregate round-trips, optimistic concurrency, two-phase event dispatch and atomicity.
/// </summary>
[Collection(SqlServerTestGroup.Name)]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA1001", Justification = "_services is disposed in IAsyncLifetime.DisposeAsync, which xUnit calls after every test.")]
public sealed class UnitOfWorkAndRepositoryTests(SqlServerDatabaseFixture db) : IAsyncLifetime
{
    private PersistenceServices _services = null!;

    public Task InitializeAsync()
    {
        _services = new PersistenceServices(db);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _services.DisposeAsync();

    private DateTimeOffset Now => _services.Clock.Now;

    private Claim NewClaim()
    {
        var claim = new PersistenceHarness(db).NewClaim();
        claim.Revalidate(new ClaimValidationContext(new PolicyPeriod(new DateOnly(2024, 1, 1), new DateOnly(2026, 12, 31)), true), Now);
        return claim;
    }

    private async Task<Claim> SaveAsync(Claim claim)
    {
        await using var scope = _services.Scope();
        scope.ServiceProvider.GetRequiredService<IClaimRepository>().Add(claim);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        return claim;
    }

    private async Task<Claim> LoadAsync(Guid id)
    {
        await using var scope = _services.Scope();
        return (await scope.ServiceProvider.GetRequiredService<IClaimRepository>().GetByIdAsync(id)).ShouldNotBeNull();
    }

    // ------------------------------------------------------------------ round-trips

    [Fact]
    public async Task Claim_aggregate_round_trips_with_all_children()
    {
        var claim = NewClaim();
        claim.AddParty(new PartyDetails(PartyRole.Witness, PartyType.Person, "John", "Smith"), _services.Actor, Now);
        claim.ApplyValidationFindings([new ValidationFinding(ValidationIssueCodes.NoPolicy, ValidationSeverity.Warning, "Demo warning", "PolicyId")], Now);
        await SaveAsync(claim);

        var loaded = await LoadAsync(claim.Id);

        loaded.ClaimNumber.ShouldBe(claim.ClaimNumber);
        loaded.Status.ShouldBe(ClaimStatus.Draft);
        loaded.PolicyNumber.ShouldBe("POL-2024-001001");
        loaded.AssignedHandlerId.ShouldBe(PersistenceHarness.Handler.UserId);
        loaded.LossEvent.EstimatedLossAmount.ShouldBe(Money.Of(12_500m));
        loaded.LossEvent.CauseOfLossCode.ShouldBe("COL-VEH-COL");
        loaded.Parties.Select(p => p.Role).ShouldBe([PartyRole.Claimant, PartyRole.Witness], ignoreOrder: true);
        loaded.RiskObjects.ShouldHaveSingleItem().IsPrimary.ShouldBeTrue();
        loaded.ValidationIssues.ShouldHaveSingleItem().Severity.ShouldBe(ValidationSeverity.Warning);
        loaded.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task Adding_a_child_to_a_loaded_aggregate_inserts_it_rather_than_updating()
    {
        // Guards the ValueGeneratedNever key convention. With a store-generated key, EF would treat
        // the domain-assigned id as an existing row, issue an UPDATE and fail with a concurrency error.
        var claim = await SaveAsync(NewClaim());

        await using (var scope = _services.Scope())
        {
            var loaded = (await scope.ServiceProvider.GetRequiredService<IClaimRepository>().GetByIdAsync(claim.Id))!;
            loaded.AddParty(new PartyDetails(PartyRole.Attorney, PartyType.Company, CompanyName: "Lex & Co"), _services.Actor, Now);
            loaded.AddRiskObject(new RiskObjectDetails(AssetType.Property, "Warehouse door"), _services.Actor, Now);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        var reloaded = await LoadAsync(claim.Id);
        reloaded.Parties.Count.ShouldBe(2);
        reloaded.RiskObjects.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Reserve_component_round_trips_with_event_sourced_history()
    {
        var claim = await SaveAsync(NewClaim());
        var component = ClaimReserveComponent.Create(claim.Id, ReserveComponentType.Indemnity);
        component.Submit(ReserveTransactionType.Add, 8_000m, "Initial reserve", _services.Actor, Money.Zero, Now);
        component.Submit(ReserveTransactionType.Adjust, 58_000m, "Surveyor estimate", _services.Actor, Money.Of(8_000m), Now);

        await using (var scope = _services.Scope())
        {
            scope.ServiceProvider.GetRequiredService<IReserveComponentRepository>().Add(component);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        await using var read = _services.Scope();
        var loaded = (await read.ServiceProvider.GetRequiredService<IReserveComponentRepository>().ListForClaimAsync(claim.Id)).ShouldHaveSingleItem();

        loaded.CurrentAmount.ShouldBe(Money.Of(8_000m));
        loaded.PendingAmount.ShouldBe(Money.Of(50_000m));
        loaded.Transactions.OrderBy(t => t.ChangeSequence).Select(t => (t.ChangeSequence, t.ApprovalStatus, t.Amount.Amount))
            .ShouldBe([(1, ApprovalStatus.AutoApproved, 8_000m), (2, ApprovalStatus.PendingApproval, 50_000m)]);
        loaded.Transactions.Select(t => t.IdempotencyKey).ShouldAllBe(k => k.StartsWith($"Reserve:{component.Id}:Change:", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ optimistic concurrency (S15.1)

    [Fact]
    public async Task Concurrent_edits_of_the_same_claim_raise_ConcurrencyException()
    {
        var claim = await SaveAsync(NewClaim());

        await using var first = _services.Scope();
        await using var second = _services.Scope();
        var a = (await first.ServiceProvider.GetRequiredService<IClaimRepository>().GetByIdAsync(claim.Id))!;
        var b = (await second.ServiceProvider.GetRequiredService<IClaimRepository>().GetByIdAsync(claim.Id))!;

        a.UpdateNotes("first writer", _services.Actor, Now);
        await first.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();

        b.UpdateNotes("second writer", _services.Actor, Now);
        await Should.ThrowAsync<ConcurrencyException>(() => second.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync());
    }

    [Fact]
    public async Task Concurrent_changes_to_only_a_reserve_transaction_conflict_on_the_component()
    {
        // A change to only the child ReserveTransaction must still bump the component root's RowVer
        // (aggregate-root versioning). Without it both writers commit: found in P3 by the concurrent
        // GL posting test, which saw two "Posted" outcomes for one transaction.
        var claim = await SaveAsync(NewClaim());
        var component = ClaimReserveComponent.Create(claim.Id, ReserveComponentType.Expense);
        var txn = component.Submit(ReserveTransactionType.Add, 2_000m, "Adjuster fee", _services.Actor, Money.Zero, Now).Transaction;
        await using (var setup = _services.Scope())
        {
            setup.ServiceProvider.GetRequiredService<IReserveComponentRepository>().Add(component);
            await setup.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        await using var first = _services.Scope();
        await using var second = _services.Scope();
        var a = (await first.ServiceProvider.GetRequiredService<IReserveComponentRepository>().GetByTransactionIdAsync(txn.Id))!;
        var b = (await second.ServiceProvider.GetRequiredService<IReserveComponentRepository>().GetByTransactionIdAsync(txn.Id))!;

        a.MarkPosted(txn.Id, "job-1", Now).ShouldBeTrue();
        await first.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();

        b.MarkPosted(txn.Id, "job-2", Now).ShouldBeTrue(); // b still sees Pending
        await Should.ThrowAsync<ConcurrencyException>(() => second.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync());
    }

    [Fact]
    public async Task Reserve_activity_on_the_claim_conflicts_with_a_concurrent_claim_edit()
    {
        var claim = await SaveAsync(NewClaim());

        await using var reserveCommand = _services.Scope();
        await using var claimCommand = _services.Scope();
        var forReserve = (await reserveCommand.ServiceProvider.GetRequiredService<IClaimRepository>().GetByIdAsync(claim.Id))!;
        var forEdit = (await claimCommand.ServiceProvider.GetRequiredService<IClaimRepository>().GetByIdAsync(claim.Id))!;

        forReserve.RecordReserveActivity(Now);
        await reserveCommand.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();

        forEdit.UpdateNotes("concurrent edit", _services.Actor, Now);
        await Should.ThrowAsync<ConcurrencyException>(() => claimCommand.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync());
    }

    // ------------------------------------------------------------------ two-phase dispatch + audit atomicity

    private void AuditEveryEventBeforeCommit(IServiceProvider scopeServices) =>
        _services.Dispatcher.BeforeCommit = events =>
        {
            var audit = scopeServices.GetRequiredService<IAuditLogService>();
            foreach (var e in events.OfType<ClaimCreated>())
            {
                audit.Record(new AuditEntry(e.ClaimId, AuditEventTypes.ClaimCreated, $"Claim {e.ClaimNumber} created.",
                    NewValue: new { e.ClaimNumber, Status = ClaimStatus.Draft }));
            }

            return Task.CompletedTask;
        };

    [Fact]
    public async Task Before_commit_audit_entries_commit_atomically_and_after_commit_sees_committed_data()
    {
        var claim = NewClaim();
        bool? committedWhenAfterCommitRan = null;

        await using (var scope = _services.Scope())
        {
            AuditEveryEventBeforeCommit(scope.ServiceProvider);
            _services.Dispatcher.AfterCommit = async _ =>
                committedWhenAfterCommitRan = await db.ScalarAsync<int>($"SELECT COUNT(*) FROM Claims WHERE ClaimId = '{claim.Id}'") == 1;

            scope.ServiceProvider.GetRequiredService<IClaimRepository>().Add(claim);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        committedWhenAfterCommitRan.ShouldBe(true);
        _services.Dispatcher.Dispatched.Where(d => d.Phase == "before").Select(d => d.Event.GetType()).ShouldContain(typeof(ClaimCreated));
        _services.Dispatcher.Dispatched.Count(d => d.Phase == "after").ShouldBe(_services.Dispatcher.Dispatched.Count(d => d.Phase == "before"));
        claim.DomainEvents.ShouldBeEmpty();

        await using var read = db.CreateContext();
        var entry = await read.AuditLog.SingleAsync(a => a.ClaimId == claim.Id);
        entry.EventType.ShouldBe("CLAIM_CREATED");
        entry.CorrelationId.ShouldBe(_services.Correlation.CorrelationId);
        entry.CreatedByUserId.ShouldBe(PersistenceHarness.Handler.UserId);
        entry.OrganisationId.ShouldBe(SqlServerDatabaseFixture.OrganisationId);
        entry.CreatedAt.ShouldBe(Now);
        entry.NewValue.ShouldBe($$"""{"claimNumber":"{{claim.ClaimNumber.Value}}","status":"Draft"}""");
    }

    [Fact]
    public async Task Failed_save_writes_neither_the_change_nor_its_audit_entry_and_skips_after_commit()
    {
        var original = await SaveAsync(NewClaim());
        _services.Dispatcher.Dispatched.Clear();

        // Same claim number: the unique index rejects the insert during SaveChanges.
        var duplicate = Claim.Create(original.ClaimNumber, new LossEventDetails(Now.AddDays(-1), "Duplicate claim number attempt for atomicity.", "COL-FIRE"),
            "Property", null, ClaimSeverity.Standard, null, _services.Actor, Now);

        await using (var scope = _services.Scope())
        {
            AuditEveryEventBeforeCommit(scope.ServiceProvider);
            scope.ServiceProvider.GetRequiredService<IClaimRepository>().Add(duplicate);
            await Should.ThrowAsync<DbUpdateException>(() => scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync());
        }

        (await db.ScalarAsync<int>($"SELECT COUNT(*) FROM ClaimAuditLog WHERE ClaimId = '{duplicate.Id}'")).ShouldBe(0);
        _services.Dispatcher.Dispatched.ShouldNotContain(d => d.Phase == "after");
    }

    [Fact]
    public async Task Transaction_with_claim_number_rolls_back_everything_and_releases_the_number()
    {
        const int year = 2301;
        Claim? created = null;

        await using (var scope = _services.Scope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await Should.ThrowAsync<InvalidOperationException>(() => uow.ExecuteInTransactionAsync<int>(async ct =>
            {
                var number = await scope.ServiceProvider.GetRequiredService<IClaimNumberGenerator>().NextAsync(year, ct);
                created = Claim.Create(number, new LossEventDetails(Now.AddDays(-1), "Fire in the storage room of the warehouse.", "COL-FIRE"),
                    "Property", null, ClaimSeverity.Standard, null, _services.Actor, Now);
                scope.ServiceProvider.GetRequiredService<IClaimRepository>().Add(created);
                await uow.SaveChangesAsync(ct);
                throw new InvalidOperationException("simulated failure after save, before commit");
            }));
        }

        (await db.ScalarAsync<int>($"SELECT COUNT(*) FROM Claims WHERE ClaimId = '{created!.Id}'")).ShouldBe(0);
        _services.Dispatcher.Dispatched.ShouldNotContain(d => d.Phase == "after");

        await using var retry = _services.Scope();
        var next = await retry.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(ct =>
            retry.ServiceProvider.GetRequiredService<IClaimNumberGenerator>().NextAsync(year, ct));
        next.Sequence.ShouldBe(1);
    }

    [Fact]
    public async Task After_commit_inside_a_transaction_waits_for_the_outer_commit()
    {
        var phasesAtSave = new List<string>();

        await using var scope = _services.Scope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.ExecuteInTransactionAsync(async ct =>
        {
            scope.ServiceProvider.GetRequiredService<IClaimRepository>().Add(NewClaim());
            await uow.SaveChangesAsync(ct);
            phasesAtSave.AddRange(_services.Dispatcher.Dispatched.Select(d => d.Phase));
            return 0;
        });

        phasesAtSave.ShouldNotContain("after");
        _services.Dispatcher.Dispatched.ShouldContain(d => d.Phase == "after");
    }

    [Fact]
    public async Task After_commit_failure_is_logged_not_rethrown_because_data_is_committed()
    {
        var claim = NewClaim();
        _services.Dispatcher.AfterCommit = _ => throw new InvalidOperationException("Hangfire unavailable");

        await SaveAsync(claim);

        (await db.ScalarAsync<int>($"SELECT COUNT(*) FROM Claims WHERE ClaimId = '{claim.Id}'")).ShouldBe(1);
    }

    [Fact]
    public async Task Reserve_events_flow_through_the_unit_of_work()
    {
        var claim = await SaveAsync(NewClaim());
        _services.Dispatcher.Dispatched.Clear();
        var component = ClaimReserveComponent.Create(claim.Id, ReserveComponentType.Expense);
        component.Submit(ReserveTransactionType.Add, 2_500m, "Adjuster fee", _services.Actor, Money.Zero, Now);

        await using (var scope = _services.Scope())
        {
            scope.ServiceProvider.GetRequiredService<IReserveComponentRepository>().Add(component);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        _services.Dispatcher.Dispatched.Where(d => d.Phase == "after").Select(d => d.Event.GetType())
            .ShouldBe([typeof(ReserveTransactionSubmitted), typeof(ReserveTransactionApproved)], ignoreOrder: true);
    }
}
