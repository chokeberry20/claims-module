using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Claims.Workflow;
using ClaimsModule.Persistence.Entities;
using ClaimsModule.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClaimsModule.Persistence.Configurations;

/// <summary>ClaimAuditLog (S9.8, S14). Tenant-filtered in ClaimsDbContext. The append-only trigger is declared in task 14.</summary>
internal sealed class ClaimAuditLogConfiguration : IEntityTypeConfiguration<ClaimAuditLog>
{
    public void Configure(EntityTypeBuilder<ClaimAuditLog> builder)
    {
        // BR-A-01 / S14.2: the AuditLogAppendOnlyTrigger migration creates an INSTEAD OF UPDATE, DELETE
        // trigger. Declaring it tells EF not to use OUTPUT-clause batching, which conflicts with triggers.
        builder.ToTable("ClaimAuditLog", t => t.HasTrigger("TR_ClaimAuditLog_AppendOnly"));
        builder.HasKey(a => a.AuditLogId);
        builder.Property(a => a.AuditLogId).HasDefaultValueSql("NEWSEQUENTIALID()").ValueGeneratedNever();

        builder.Property(a => a.OrganisationId).IsRequired();
        builder.Property(a => a.EventType).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Description).IsRequired();
        builder.Property(a => a.OldValue);
        builder.Property(a => a.NewValue);
        builder.Property(a => a.RelatedEntityType).HasMaxLength(100);
        builder.Property(a => a.CreatedAt).IsRequired();

        builder.HasOne<Claim>().WithMany().HasForeignKey(a => a.ClaimId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => new { a.ClaimId, a.CreatedAt });
        builder.HasIndex(a => new { a.ClaimId, a.EventType, a.CreatedAt });
        builder.HasIndex(a => a.OrganisationId);
    }
}

/// <summary>ClaimStatusTransitions (brief §3.2). Global configuration, seeded from the domain workflow (D-08, D-51).</summary>
internal sealed class ClaimStatusTransitionConfiguration : IEntityTypeConfiguration<ClaimStatusTransition>
{
    public void Configure(EntityTypeBuilder<ClaimStatusTransition> builder)
    {
        builder.ToTable("ClaimStatusTransitions");
        builder.HasKey(t => t.ClaimStatusTransitionId);
        builder.Property(t => t.ClaimStatusTransitionId).HasDefaultValueSql("NEWSEQUENTIALID()").ValueGeneratedNever();
        builder.Property(t => t.FromStatus).IsRequired();
        builder.Property(t => t.ToStatus).IsRequired();
        builder.Property(t => t.RequiredPermission).IsRequired();
        builder.Property(t => t.RequiresReason).IsRequired().HasDefaultValue(false);
        builder.Property(t => t.Description).HasMaxLength(255).IsRequired();
        builder.HasIndex(t => new { t.FromStatus, t.ToStatus }).IsUnique();

        // A single source of truth: the table is seeded from the domain state machine (D-08).
        builder.HasData(ClaimWorkflow.Transitions.Select(rule => new ClaimStatusTransition
        {
            ClaimStatusTransitionId = SeedData.TransitionId(rule),
            FromStatus = rule.From,
            ToStatus = rule.To,
            RequiredPermission = rule.RequiredRole,
            RequiresReason = rule.RequiresReason,
            Description = rule.Description,
        }));
    }
}

/// <summary>ClaimNumberSequences: one gapless counter per (organisation, year) (D-14).</summary>
internal sealed class ClaimNumberSequenceConfiguration : IEntityTypeConfiguration<ClaimNumberSequence>
{
    public void Configure(EntityTypeBuilder<ClaimNumberSequence> builder)
    {
        builder.ToTable("ClaimNumberSequences");
        builder.HasKey(s => new { s.OrganisationId, s.Year });
        builder.Property(s => s.Year).ValueGeneratedNever();
        builder.Property(s => s.LastValue).IsRequired();
    }
}

/// <summary>IdempotencyRecords: replay store for Idempotency-Key headers (S10).</summary>
internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("IdempotencyRecords");
        builder.HasKey(r => r.IdempotencyRecordId);
        builder.Property(r => r.IdempotencyRecordId).HasDefaultValueSql("NEWSEQUENTIALID()").ValueGeneratedNever();
        builder.Property(r => r.IdempotencyKey).HasMaxLength(200).IsRequired();
        builder.Property(r => r.RequestMethod).HasMaxLength(10).IsRequired();
        builder.Property(r => r.RequestPath).HasMaxLength(500).IsRequired();
        builder.Property(r => r.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(r => r.ResponseContentType).HasMaxLength(100);
        builder.HasIndex(r => new { r.OrganisationId, r.UserId, r.IdempotencyKey }).IsUnique();
        builder.HasIndex(r => r.ExpiresAt);
    }
}
