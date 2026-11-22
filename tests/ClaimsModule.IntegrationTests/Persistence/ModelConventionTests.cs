using ClaimsModule.Domain.Common;
using ClaimsModule.Persistence;
using ClaimsModule.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ClaimsModule.IntegrationTests.Persistence;

/// <summary>
/// Checks the S15.1/S15.2 conventions on the EF design-time model. No database is needed:
/// the model is built with the SQL Server provider, so relational column types are available.
/// </summary>
public sealed class ModelConventionTests
{
    private static readonly IModel Model = BuildModel();

    private static IModel BuildModel()
    {
        var options = new DbContextOptionsBuilder<ClaimsDbContext>()
            .UseSqlServer("Server=model-only;Database=none;Trusted_Connection=True")
            .Options;
        using var context = new ClaimsDbContext(options, new TestTenantProvider(Guid.NewGuid()));
        return context.GetService<IDesignTimeModel>().Model;
    }

    private static IEnumerable<IEntityType> DomainEntityTypes =>
        Model.GetEntityTypes().Where(t => typeof(Entity).IsAssignableFrom(t.ClrType) && !t.IsOwned());

    public static TheoryData<string> DomainEntityNames()
    {
        var data = new TheoryData<string>();
        foreach (var type in DomainEntityTypes)
        {
            data.Add(type.ClrType.Name);
        }

        return data;
    }

    private static IEntityType Entity(string clrName) => DomainEntityTypes.Single(t => t.ClrType.Name == clrName);

    [Fact]
    public void Model_contains_domain_entities() => DomainEntityTypes.ShouldNotBeEmpty();

    [Theory]
    [MemberData(nameof(DomainEntityNames))]
    public void Primary_key_is_uniqueidentifier_with_newsequentialid_default_and_domain_assigned(string entity)
    {
        var key = Entity(entity).FindPrimaryKey().ShouldNotBeNull().Properties.ShouldHaveSingleItem();

        key.ClrType.ShouldBe(typeof(Guid));
        key.GetColumnType().ShouldBe("uniqueidentifier");
        key.GetDefaultValueSql().ShouldBe("NEWSEQUENTIALID()");
        key.ValueGenerated.ShouldBe(ValueGenerated.Never);
    }

    [Theory]
    [MemberData(nameof(DomainEntityNames))]
    public void Business_table_has_tenant_audit_and_soft_delete_columns(string entity)
    {
        var type = Entity(entity);

        Column(type, ShadowProperties.OrganisationId).ShouldBe(("uniqueidentifier", false));
        Column(type, ShadowProperties.CreatedAt).ShouldBe(("datetimeoffset(7)", false));
        Column(type, ShadowProperties.UpdatedAt).ShouldBe(("datetimeoffset(7)", true));
        Column(type, ShadowProperties.UserCreated).ShouldBe(("uniqueidentifier", true));
        Column(type, ShadowProperties.UserModified).ShouldBe(("uniqueidentifier", true));
        Column(type, ShadowProperties.IsDeleted).ShouldBe(("bit", false));
        Column(type, ShadowProperties.DeletedAt).ShouldBe(("datetimeoffset(7)", true));
        type.FindProperty(ShadowProperties.IsDeleted)!.GetDefaultValue().ShouldBe(false);
    }

    [Theory]
    [MemberData(nameof(DomainEntityNames))]
    public void Global_query_filter_covers_soft_delete_and_tenant(string entity)
    {
        var filter = Entity(entity).GetQueryFilter().ShouldNotBeNull().ToString();

        filter.ShouldContain(ShadowProperties.IsDeleted);
        filter.ShouldContain(ShadowProperties.OrganisationId);
        filter.ShouldContain(nameof(ClaimsDbContext.CurrentOrganisationId));
    }

    [Fact]
    public void Monetary_columns_are_decimal_19_4_and_never_money_or_float()
    {
        var violations = Model.GetEntityTypes()
            .SelectMany(t => t.GetProperties())
            .Select(p => (Property: $"{p.DeclaringType.DisplayName()}.{p.Name}", Type: p.GetColumnType()))
            .Where(p => p.Type.Contains("money", StringComparison.OrdinalIgnoreCase)
                        || p.Type.Contains("float", StringComparison.OrdinalIgnoreCase)
                        || p.Type.Contains("real", StringComparison.OrdinalIgnoreCase)
                        || (p.Type.StartsWith("decimal", StringComparison.Ordinal) && p.Type != "decimal(19,4)"))
            .ToList();

        violations.ShouldBeEmpty();
    }

    [Fact]
    public void All_timestamps_are_datetimeoffset_7() =>
        Model.GetEntityTypes()
            .SelectMany(t => t.GetProperties())
            .Where(p => (Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType) == typeof(DateTimeOffset))
            .ShouldAllBe(p => p.GetColumnType() == "datetimeoffset(7)");

    [Fact]
    public void Enums_are_stored_as_nvarchar_50_codes() =>
        Model.GetEntityTypes()
            .SelectMany(t => t.GetProperties())
            .Where(p => (Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType).IsEnum)
            .ShouldAllBe(p => p.GetColumnType() == "nvarchar(50)");

    [Fact]
    public void Bool_flags_are_bit_not_null_default_false() =>
        Model.GetEntityTypes()
            .SelectMany(t => t.GetProperties())
            .Where(p => p.ClrType == typeof(bool))
            .ShouldAllBe(p => p.GetColumnType() == "bit" && !p.IsNullable && Equals(p.GetDefaultValue(), false));

    [Fact]
    public void Policy_coverage_types_are_a_json_column() =>
        Column(Entity("Policy"), "_coverageTypes").ShouldBe(("nvarchar(max)", false));

    private static (string Type, bool Nullable) Column(IEntityType type, string property)
    {
        var p = type.FindProperty(property).ShouldNotBeNull($"{type.DisplayName()} is missing {property}");
        return (p.GetColumnType(), p.IsColumnNullable());
    }
}
