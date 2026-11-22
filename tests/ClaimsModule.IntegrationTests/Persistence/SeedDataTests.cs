using ClaimsModule.Domain.Claims.Workflow;
using ClaimsModule.Domain.ReferenceData;
using Microsoft.EntityFrameworkCore;

namespace ClaimsModule.IntegrationTests.Persistence;

/// <summary>Reference data seeded by the migrations (S5.5, S5.6, S15.4, D-08, D-34, D-52).</summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class SeedDataTests(SqlServerDatabaseFixture db)
{
    public static TheoryData<string, string, string, string, string> SpecPolicies() => new()
    {
        // S5.5, verbatim
        { "POL-2024-001001", "Meridian Transport LLC", "2024-01-01", "2026-12-31", "Vehicle,Cargo" },
        { "POL-2024-001002", "Harborview Properties Inc", "2024-06-01", "2026-05-31", "Property,Liability" },
        { "POL-2025-002001", "Coastal Builders Group", "2025-03-01", "2027-02-28", "Property,Equipment" },
        { "POL-2025-002002", "Stanton Medical Group", "2025-01-01", "2026-12-31", "Liability,Vehicle" },
        { "POL-2023-000099", "Archived Corp", "2020-01-01", "2021-12-31", "Property" },
    };

    [Theory]
    [MemberData(nameof(SpecPolicies))]
    public async Task S5_5_spec_policy_is_seeded_exactly(string number, string client, string effective, string expiration, string coverage)
    {
        await using var context = db.CreateContext();

        var policy = await context.Policies.SingleAsync(p => p.PolicyNumber == number);

        policy.ClientName.ShouldBe(client);
        policy.EffectiveDate.ShouldBe(DateOnly.Parse(effective, System.Globalization.CultureInfo.InvariantCulture));
        policy.ExpirationDate.ShouldBe(DateOnly.Parse(expiration, System.Globalization.CultureInfo.InvariantCulture));
        policy.CoverageTypes.ShouldBe(coverage.Split(','));
        policy.IsCancelled.ShouldBeFalse();
    }

    [Fact]
    public async Task D_34_three_extra_in_force_policies_are_seeded_and_the_archived_one_is_expired()
    {
        await using var context = db.CreateContext();
        var policies = await context.Policies.ToListAsync();
        var today = new DateOnly(2026, 11, 23);

        policies.Count.ShouldBe(8);
        policies.Single(p => p.PolicyNumber == "POL-2023-000099").StatusOn(today).ShouldBe(PolicyStatus.Expired);
        policies.Count(p => p.StatusOn(today) == PolicyStatus.Active).ShouldBe(6);
    }

    public static TheoryData<string, string, PerilCategory> SpecCausesOfLoss() => new()
    {
        // S5.6, verbatim
        { "COL-FIRE", "Fire", PerilCategory.Property },
        { "COL-FLOOD", "Flood", PerilCategory.Weather },
        { "COL-THEFT", "Theft", PerilCategory.Crime },
        { "COL-VEH-COL", "Vehicle Collision", PerilCategory.Auto },
        { "COL-VEH-COMP", "Vehicle Comprehensive", PerilCategory.Auto },
        { "COL-LIAB", "Third Party Liability", PerilCategory.Liability },
        { "COL-EQUIP", "Equipment Breakdown", PerilCategory.Equipment },
        { "COL-WIND", "Wind / Storm", PerilCategory.Weather },
        { "COL-INJURY", "Bodily Injury", PerilCategory.Liability },
        { "COL-OTHER", "Other / Unknown", PerilCategory.General },
    };

    [Theory]
    [MemberData(nameof(SpecCausesOfLoss))]
    public async Task S5_6_cause_of_loss_code_is_seeded_active(string code, string name, PerilCategory category)
    {
        await using var context = db.CreateContext();

        var cause = await context.CauseOfLossCodes.SingleAsync(c => c.Code == code);

        cause.Name.ShouldBe(name);
        cause.PerilCategory.ShouldBe(category);
        cause.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task D_52_one_extra_inactive_code_exists_for_the_BR_C_05_demo()
    {
        await using var context = db.CreateContext();

        var inactive = await context.CauseOfLossCodes.Where(c => !c.IsActive).ToListAsync();

        inactive.ShouldHaveSingleItem().Code.ShouldBe("COL-LEGACY-SUBS");
        (await context.CauseOfLossCodes.CountAsync()).ShouldBe(11);
    }

    [Fact]
    public async Task D_08_transition_table_matches_the_domain_state_machine_exactly()
    {
        await using var context = db.CreateContext();

        var rows = await context.ClaimStatusTransitions
            .Select(t => new { t.FromStatus, t.ToStatus, t.RequiredPermission, t.RequiresReason })
            .ToListAsync();

        rows.Select(r => (r.FromStatus, r.ToStatus, r.RequiredPermission, r.RequiresReason))
            .ShouldBe(
                ClaimWorkflow.Transitions.Select(t => (t.From, t.To, t.RequiredRole, t.RequiresReason)),
                ignoreOrder: true);
    }

    [Fact]
    public async Task Seeded_business_rows_belong_to_the_fixed_organisation_and_are_invisible_to_other_tenants()
    {
        (await db.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM Policies WHERE OrganisationId <> '{SqlServerDatabaseFixture.OrganisationId}'")).ShouldBe(0);

        await using var otherTenant = db.CreateContext(Guid.NewGuid());

        (await otherTenant.Policies.CountAsync()).ShouldBe(0);
        (await otherTenant.CauseOfLossCodes.CountAsync()).ShouldBe(0);
    }
}
