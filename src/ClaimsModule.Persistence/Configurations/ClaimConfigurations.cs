using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Claims.Validation;
using ClaimsModule.Domain.ReferenceData;
using ClaimsModule.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClaimsModule.Persistence.Configurations;

/// <summary>
/// Claims, the central aggregate root (S9.1). Children are deleted client-side (ClientCascade) so
/// the soft-delete interceptor can turn orphan removal into IsDeleted = 1. The database FKs stay
/// NO ACTION, which avoids SQL Server's multiple-cascade-path restriction.
/// </summary>
internal sealed class ClaimConfiguration : BusinessEntityConfiguration<Claim>
{
    protected override string KeyColumnName => "ClaimId";

    protected override string TableName => "Claims";

    protected override void ConfigureEntity(EntityTypeBuilder<Claim> builder)
    {
        AddRowVersion(builder);

        builder.Property(c => c.ClaimNumber)
            .HasConversion<ClaimNumberConverter>()
            .HasMaxLength(ClaimNumber.MaxLength)
            .IsRequired();
        builder.Property(c => c.PolicyNumber).HasMaxLength(PolicySnapshot.MaxPolicyNumberLength);
        builder.Property(c => c.ClientName).HasMaxLength(PolicySnapshot.MaxClientNameLength);
        builder.Property(c => c.Status).IsRequired();
        builder.Property(c => c.Severity).IsRequired();
        builder.Property(c => c.ClaimType).HasMaxLength(Claim.MaxClaimTypeLength).IsRequired();
        builder.Property(c => c.ReportedDate).IsRequired();
        builder.Property(c => c.AssignedHandlerName).HasMaxLength(Claim.MaxHandlerNameLength);
        builder.Property(c => c.ClosureReason).HasMaxLength(Claim.MaxReasonLength);
        builder.Property(c => c.Notes);
        builder.Property(c => c.ReserveLimitOverride).IsRequired().HasDefaultValue(false);
        builder.Property(c => c.ReopenCount).IsRequired();
        builder.Property(c => c.IsSlaBreached).IsRequired().HasDefaultValue(false);

        builder.HasOne<Policy>().WithMany().HasForeignKey(c => c.PolicyId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.LossEvent).WithOne().HasForeignKey<LossEvent>(l => l.ClaimId).OnDelete(DeleteBehavior.ClientCascade);
        builder.Navigation(c => c.LossEvent).IsRequired();

        builder.HasMany(c => c.Parties).WithOne().HasForeignKey(p => p.ClaimId).OnDelete(DeleteBehavior.ClientCascade);
        builder.Navigation(c => c.Parties).HasField("_parties").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(c => c.RiskObjects).WithOne().HasForeignKey(r => r.ClaimId).OnDelete(DeleteBehavior.ClientCascade);
        builder.Navigation(c => c.RiskObjects).HasField("_riskObjects").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(c => c.ValidationIssues).WithOne().HasForeignKey(i => i.ClaimId).OnDelete(DeleteBehavior.ClientCascade);
        builder.Navigation(c => c.ValidationIssues).HasField("_validationIssues").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(c => c.DomainEvents);
        builder.Ignore(c => c.ActiveClaimants);
        builder.Ignore(c => c.HasActiveClaimant);
        builder.Ignore(c => c.HasOpenCriticalIssues);
        builder.Ignore(c => c.IsTerminalForEditing);

        // BR-C-04: unique per organisation. Not filtered, so soft-deleted claims keep their number.
        builder.HasIndex(ShadowProperties.OrganisationId, nameof(Claim.ClaimNumber)).IsUnique();
        builder.HasIndex(c => c.Status);
        builder.HasIndex(c => c.AssignedHandlerId);
        builder.HasIndex(c => c.ReportedDate);
    }
}

/// <summary>LossEvents (S9.2). The cause code references CauseOfLossCodes (OrganisationId, Code).</summary>
internal sealed class LossEventConfiguration : BusinessEntityConfiguration<LossEvent>
{
    protected override string KeyColumnName => "LossEventId";

    protected override string TableName => "LossEvents";

    protected override void ConfigureEntity(EntityTypeBuilder<LossEvent> builder)
    {
        builder.Property(l => l.LossDate).IsRequired();
        builder.Property(l => l.LossDescription).IsRequired();
        builder.Property(l => l.LossLocation).HasMaxLength(LossEvent.MaxLocationLength);
        builder.Property(l => l.CauseOfLossCode).HasMaxLength(LossEvent.MaxCauseOfLossCodeLength).IsRequired();
        builder.Property(l => l.EstimatedLossAmount);
        builder.Property(l => l.ReportDate).IsRequired();
        builder.Property(l => l.PoliceReportNumber).HasMaxLength(LossEvent.MaxPoliceReportNumberLength);

        builder.HasOne<CauseOfLossCode>()
            .WithMany()
            .HasForeignKey(ShadowProperties.OrganisationId, nameof(LossEvent.CauseOfLossCode))
            .HasPrincipalKey(ShadowProperties.OrganisationId, nameof(CauseOfLossCode.Code))
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(l => l.LossDate);
    }
}

/// <summary>ClaimParties (S9.3). Removal is soft, through IsActive (S10.1).</summary>
internal sealed class ClaimPartyConfiguration : BusinessEntityConfiguration<ClaimParty>
{
    protected override string KeyColumnName => "ClaimPartyId";

    protected override string TableName => "ClaimParties";

    protected override void ConfigureEntity(EntityTypeBuilder<ClaimParty> builder)
    {
        builder.Property(p => p.Role).HasColumnName("PartyRole").IsRequired();
        builder.Property(p => p.Type).HasColumnName("PartyType").HasMaxLength(20).IsRequired();
        builder.Property(p => p.FirstName).HasMaxLength(ClaimParty.MaxPersonNameLength);
        builder.Property(p => p.LastName).HasMaxLength(ClaimParty.MaxPersonNameLength);
        builder.Property(p => p.CompanyName).HasMaxLength(ClaimParty.MaxCompanyNameLength);
        builder.Property(p => p.Email).HasMaxLength(ClaimParty.MaxEmailLength);
        builder.Property(p => p.Phone).HasMaxLength(ClaimParty.MaxPhoneLength);
        builder.Property(p => p.Notes);
        builder.Property(p => p.IsActive).IsRequired().HasDefaultValue(false);

        builder.Ignore(p => p.DisplayName);
        builder.Ignore(p => p.IsActiveClaimant);

        builder.HasIndex(nameof(ClaimParty.ClaimId), nameof(ClaimParty.Role));
    }
}

/// <summary>ClaimRiskObjects (S9.4).</summary>
internal sealed class ClaimRiskObjectConfiguration : BusinessEntityConfiguration<ClaimRiskObject>
{
    protected override string KeyColumnName => "ClaimRiskObjectId";

    protected override string TableName => "ClaimRiskObjects";

    protected override void ConfigureEntity(EntityTypeBuilder<ClaimRiskObject> builder)
    {
        builder.Property(r => r.AssetType).IsRequired();
        builder.Property(r => r.AssetDescription).HasMaxLength(ClaimRiskObject.MaxAssetDescriptionLength).IsRequired();
        builder.Property(r => r.DamageDescription);
        builder.Property(r => r.AssetReference).HasMaxLength(ClaimRiskObject.MaxAssetReferenceLength);
        builder.Property(r => r.IsPrimary).IsRequired().HasDefaultValue(false);
    }
}

/// <summary>ClaimValidationIssues: persisted state-level issues (D-06, D-49).</summary>
internal sealed class ClaimValidationIssueConfiguration : BusinessEntityConfiguration<ClaimValidationIssue>
{
    protected override string KeyColumnName => "ValidationIssueId";

    protected override string TableName => "ClaimValidationIssues";

    protected override void ConfigureEntity(EntityTypeBuilder<ClaimValidationIssue> builder)
    {
        builder.Property(i => i.Code).HasMaxLength(50).IsRequired();
        builder.Property(i => i.Severity).IsRequired();
        builder.Property(i => i.Message).HasMaxLength(500).IsRequired();
        builder.Property(i => i.Field).HasMaxLength(100);
        builder.Property(i => i.Status).IsRequired();
        builder.Property(i => i.RaisedAt).IsRequired();
        builder.Property(i => i.WaiverReason).HasMaxLength(ClaimValidationIssue.MaxWaiverReasonLength);

        builder.Ignore(i => i.IsTracked);
        builder.Ignore(i => i.IsOpenCritical);
        builder.Ignore(i => i.IsOpenWarning);

        builder.HasIndex(nameof(ClaimValidationIssue.ClaimId), nameof(ClaimValidationIssue.Code));
    }
}
