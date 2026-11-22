using ClaimsModule.Domain.Common;
using ClaimsModule.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClaimsModule.Persistence.Configurations;

/// <summary>
/// The S15.1 conventions every business table gets:
/// <list type="bullet">
/// <item>a UNIQUEIDENTIFIER key with a NEWSEQUENTIALID() default;</item>
/// <item>OrganisationId (tenant), audit columns and soft-delete columns, as shadow properties (D-48).</item>
/// </list>
/// The key is assigned by the domain (<see cref="SequentialGuid"/>), so EF must always send it:
/// <c>ValueGeneratedNever</c>. Otherwise a new child added to an already-tracked aggregate (for
/// example a party added to a loaded claim) has a non-default key and is treated as an existing row
/// to UPDATE instead of INSERT. The column still gets the DB default the spec requires.
/// </summary>
internal abstract class BusinessEntityConfiguration<TEntity> : IEntityTypeConfiguration<TEntity>
    where TEntity : Entity
{
    /// <summary>The spec's key column name, for example ClaimId or ClaimPartyId (S9).</summary>
    protected abstract string KeyColumnName { get; }

    protected abstract string TableName { get; }

    public void Configure(EntityTypeBuilder<TEntity> builder)
    {
        builder.ToTable(TableName);

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasColumnName(KeyColumnName)
            .HasDefaultValueSql("NEWSEQUENTIALID()")
            .ValueGeneratedNever();

        builder.Property<Guid>(ShadowProperties.OrganisationId).IsRequired();
        builder.Property<DateTimeOffset>(ShadowProperties.CreatedAt).IsRequired();
        builder.Property<DateTimeOffset?>(ShadowProperties.UpdatedAt);
        builder.Property<Guid?>(ShadowProperties.UserCreated);
        builder.Property<Guid?>(ShadowProperties.UserModified);
        builder.Property<bool>(ShadowProperties.IsDeleted).IsRequired().HasDefaultValue(false);
        builder.Property<DateTimeOffset?>(ShadowProperties.DeletedAt);

        builder.HasIndex(ShadowProperties.OrganisationId);

        ConfigureEntity(builder);
    }

    protected abstract void ConfigureEntity(EntityTypeBuilder<TEntity> builder);

    /// <summary>RowVer ROWVERSION on aggregate roots (S15.1, S15.2).</summary>
    protected static void AddRowVersion(EntityTypeBuilder<TEntity> builder) =>
        builder.Property<byte[]>(ShadowProperties.RowVersion).IsRowVersion().IsRequired();
}
