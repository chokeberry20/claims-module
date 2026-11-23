using ClaimsModule.Application.Claims;
using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Reserves;
using ClaimsModule.Persistence.Seed;

namespace ClaimsModule.IntegrationTests.Features;

/// <summary>Valid FNOL commands built from the seeded reference data, for feature tests to tweak with <c>with</c>.</summary>
internal static class Fnol
{
    /// <summary>POL-2024-001001, in force 2024-01-01..2026-12-31.</summary>
    public static readonly Guid MeridianPolicyId = SeedData.Policies.Single(p => p.PolicyNumber == "POL-2024-001001").Id;

    /// <summary>POL-2024-001002, expired 2026-05-31 (D-34).</summary>
    public static readonly Guid HarborviewPolicyId = SeedData.Policies.Single(p => p.PolicyNumber == "POL-2024-001002").Id;

    public static readonly PartyInput Claimant = new(PartyRole.Claimant, PartyType.Person, "Jane", "Doe", Email: "jane.doe@example.com");

    public static readonly RiskObjectInput Vehicle = new(AssetType.Vehicle, "2022 Volvo FH16 tractor unit", "Rear bumper and trailer coupling", "VIN YV2RT40A5NB123456");

    public static CreateClaimCommand Complete(DateTimeOffset now) => new()
    {
        PolicyId = MeridianPolicyId,
        LossDate = now.AddDays(-3),
        LossDescription = "Rear-end collision on the I-95 northbound exit ramp in heavy rain.",
        CauseOfLossCode = "COL-VEH-COL",
        LossLocation = "I-95 exit 12, Stamford CT",
        EstimatedLossAmount = 12_500m,
        Parties = [Claimant],
        RiskObjects = [Vehicle],
    };

    public static CreateClaimCommand WithReserve(DateTimeOffset now, decimal amount, ReserveComponentType type = ReserveComponentType.Indemnity) =>
        Complete(now) with { InitialReserve = new InitialReserveInput(type, amount, "Initial adjuster estimate") };
}
