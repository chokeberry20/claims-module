using System.Linq.Expressions;
using ClaimsModule.Application.Abstractions;
using ClaimsModule.Domain.Common;
using ClaimsModule.Domain.ReferenceData;
using ClaimsModule.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;

namespace ClaimsModule.Persistence;

/// <summary>
/// The EF Core context for the claims module. The schema is defined only through Fluent API
/// configurations (S15.2). Global query filters combine soft delete and tenant isolation (S15.2, B6.1).
/// The tenant value is read per context instance from <see cref="CurrentOrganisationId"/>.
/// </summary>
public sealed class ClaimsDbContext : DbContext
{
    private readonly ITenantProvider _tenantProvider;

    public ClaimsDbContext(DbContextOptions<ClaimsDbContext> options, ITenantProvider tenantProvider)
        : base(options)
    {
        _tenantProvider = tenantProvider;
    }

    /// <summary>Referenced by the global query filters, so it is evaluated per query.</summary>
    public Guid CurrentOrganisationId => _tenantProvider.OrganisationId;

    public DbSet<Policy> Policies => Set<Policy>();

    public DbSet<CauseOfLossCode> CauseOfLossCodes => Set<CauseOfLossCode>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // S15.1 column conventions applied by CLR type.
        configurationBuilder.Properties<decimal>().HavePrecision(19, 4);
        configurationBuilder.Properties<Money>().HaveConversion<MoneyConverter>().HavePrecision(19, 4);
        configurationBuilder.Properties<DateTimeOffset>().HavePrecision(7);
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(50);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ClaimsDbContext).Assembly);
        ApplyBusinessQueryFilters(modelBuilder);
    }

    /// <summary>
    /// Adds <c>!IsDeleted &amp;&amp; OrganisationId == CurrentOrganisationId</c> to every domain entity
    /// (every type deriving from <see cref="Entity"/>).
    /// </summary>
    private void ApplyBusinessQueryFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(t => typeof(Entity).IsAssignableFrom(t.ClrType) && t.BaseType is null))
        {
            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var isDeleted = Expression.Call(
                typeof(EF), nameof(EF.Property), [typeof(bool)], parameter, Expression.Constant(ShadowProperties.IsDeleted));
            var organisationId = Expression.Call(
                typeof(EF), nameof(EF.Property), [typeof(Guid)], parameter, Expression.Constant(ShadowProperties.OrganisationId));
            var currentOrganisation = Expression.Property(Expression.Constant(this), nameof(CurrentOrganisationId));

            var body = Expression.AndAlso(
                Expression.Not(isDeleted),
                Expression.Equal(organisationId, currentOrganisation));

            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(Expression.Lambda(body, parameter));
        }
    }
}
