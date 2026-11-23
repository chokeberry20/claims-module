using System.Data;
using System.Linq.Expressions;
using ClaimsModule.Application.Abstractions;
using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Common;
using ClaimsModule.Domain.Documents;
using ClaimsModule.Domain.ReferenceData;
using ClaimsModule.Domain.Reserves;
using ClaimsModule.Persistence.Conventions;
using ClaimsModule.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

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

    public DbSet<Claim> Claims => Set<Claim>();

    public DbSet<ClaimReserveComponent> ReserveComponents => Set<ClaimReserveComponent>();

    public DbSet<ReserveTransaction> ReserveTransactions => Set<ReserveTransaction>();

    public DbSet<ClaimDocument> ClaimDocuments => Set<ClaimDocument>();

    public DbSet<ClaimAuditLog> AuditLog => Set<ClaimAuditLog>();

    public DbSet<Policy> Policies => Set<Policy>();

    public DbSet<CauseOfLossCode> CauseOfLossCodes => Set<CauseOfLossCode>();

    public DbSet<ClaimStatusTransition> ClaimStatusTransitions => Set<ClaimStatusTransition>();

    public DbSet<ClaimNumberSequence> ClaimNumberSequences => Set<ClaimNumberSequence>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // S15.1 column conventions applied by CLR type.
        configurationBuilder.Properties<decimal>().HavePrecision(19, 4);
        configurationBuilder.Properties<Money>().HaveConversion<MoneyConverter>().HavePrecision(19, 4);
        configurationBuilder.Properties<DateTimeOffset>().HavePrecision(7);
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(50);

        // Lets ClaimNumber values appear as function arguments and parameters (ClaimsDbFunctions).
        configurationBuilder.DefaultTypeMapping<ClaimNumber>().HasConversion<ClaimNumberConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ClaimsDbContext).Assembly);

        // CAST(ClaimNumber AS nvarchar(50)): a plain string expression, so Contains → LIKE (S10.1).
        modelBuilder.HasDbFunction(typeof(ClaimsDbFunctions).GetMethod(nameof(ClaimsDbFunctions.ClaimNumberText))!)
            .HasTranslation(args => new SqlUnaryExpression(
                ExpressionType.Convert,
                args[0],
                typeof(string),
                new StringTypeMapping("nvarchar(50)", DbType.String, unicode: true, size: ClaimNumber.MaxLength)));
        ApplyBusinessQueryFilters(modelBuilder);

        // The audit log is tenant-scoped but has no soft delete (D-23).
        modelBuilder.Entity<ClaimAuditLog>().HasQueryFilter(a => a.OrganisationId == CurrentOrganisationId);
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
