using ClaimsModule.Domain.ReferenceData;
using ClaimsModule.Persistence.Conventions;
using ClaimsModule.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClaimsModule.Persistence.Configurations;

/// <summary>Policies, the simulated policy table (S9.10).</summary>
internal sealed class PolicyConfiguration : BusinessEntityConfiguration<Policy>
{
    protected override string KeyColumnName => "PolicyId";

    protected override string TableName => "Policies";

    protected override void ConfigureEntity(EntityTypeBuilder<Policy> builder)
    {
        builder.Property(p => p.PolicyNumber).HasMaxLength(Policy.MaxPolicyNumberLength).IsRequired();
        builder.Property(p => p.ClientName).HasMaxLength(Policy.MaxClientNameLength).IsRequired();
        builder.Property(p => p.EffectiveDate).IsRequired();
        builder.Property(p => p.ExpirationDate).IsRequired();
        builder.Property(p => p.IsCancelled).IsRequired().HasDefaultValue(false);

        // D-35: a JSON array in NVARCHAR(MAX), mapped as an EF primitive collection over the backing field.
        builder.PrimitiveCollection<List<string>>("_coverageTypes")
            .HasColumnName("CoverageTypes")
            .IsRequired();

        builder.Ignore(p => p.CoverageTypes);
        builder.Ignore(p => p.Period);

        builder.HasIndex(ShadowProperties.OrganisationId, nameof(Policy.PolicyNumber)).IsUnique();
        builder.HasIndex(p => p.ClientName);

        // S15.4: seed through HasData, applied by the migrations.
        builder.HasData(SeedData.Policies.Select(p => new
        {
            p.Id,
            p.PolicyNumber,
            p.ClientName,
            p.EffectiveDate,
            p.ExpirationDate,
            IsCancelled = false,
            _coverageTypes = p.CoverageTypes.ToList(),
            OrganisationId = SeedData.DefaultOrganisationId,
            CreatedAt = SeedData.SeededAt,
            IsDeleted = false,
        }));
    }
}

/// <summary>CauseOfLossCodes reference table (S9.9). Code is unique per organisation (BR-C-05).</summary>
internal sealed class CauseOfLossCodeConfiguration : BusinessEntityConfiguration<CauseOfLossCode>
{
    protected override string KeyColumnName => "CauseOfLossCodeId";

    protected override string TableName => "CauseOfLossCodes";

    protected override void ConfigureEntity(EntityTypeBuilder<CauseOfLossCode> builder)
    {
        builder.Property(c => c.Code).HasMaxLength(CauseOfLossCode.MaxCodeLength).IsRequired();
        builder.Property(c => c.Name).HasMaxLength(CauseOfLossCode.MaxNameLength).IsRequired();
        builder.Property(c => c.PerilCategory).IsRequired();
        builder.Property(c => c.IsActive).IsRequired().HasDefaultValue(false);
        builder.Property(c => c.SortOrder).IsRequired();

        // Alternate key so LossEvents can reference (OrganisationId, Code) as S9.2 says ("FK to CauseOfLossCodes.Code").
        builder.HasAlternateKey(ShadowProperties.OrganisationId, nameof(CauseOfLossCode.Code));

        builder.HasData(SeedData.CausesOfLoss.Select(c => new
        {
            c.Id,
            c.Code,
            c.Name,
            c.PerilCategory,
            c.IsActive,
            c.SortOrder,
            OrganisationId = SeedData.DefaultOrganisationId,
            CreatedAt = SeedData.SeededAt,
            IsDeleted = false,
        }));
    }
}
