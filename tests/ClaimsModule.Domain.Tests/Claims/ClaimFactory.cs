using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Claims.Validation;
using ClaimsModule.Domain.Common;
using static ClaimsModule.Domain.Tests.TestData;

namespace ClaimsModule.Domain.Tests.Claims;

/// <summary>Builds claims in valid states for tests, using only public domain behaviour.</summary>
internal static class ClaimFactory
{
    public static readonly PolicySnapshot MeridianPolicy = new(Guid.NewGuid(), "POL-2024-001001", "Meridian Transport LLC");
    public static readonly PolicyPeriod MeridianPeriod = new(new DateOnly(2024, 1, 1), new DateOnly(2026, 12, 31));

    public static readonly PolicySnapshot ArchivedPolicy = new(Guid.NewGuid(), "POL-2023-000099", "Archived Corp");
    public static readonly PolicyPeriod ArchivedPeriod = new(new DateOnly(2020, 1, 1), new DateOnly(2021, 12, 31));

    public static LossEventDetails ValidLoss(DateTimeOffset? lossDate = null, string cause = "COL-VEH-COL") => new(
        lossDate ?? Now.AddDays(-10),
        "Rear-end collision on the I-95 northbound exit ramp at low speed.",
        cause,
        "I-95 exit 12, Stamford CT",
        12_500m);

    public static PartyDetails Claimant(string first = "Jane", string last = "Doe") =>
        new(PartyRole.Claimant, PartyType.Person, first, last, Email: "jane.doe@example.com", Phone: "+1 555 0100");

    public static PartyDetails Witness() => new(PartyRole.Witness, PartyType.Person, "John", "Smith");

    public static RiskObjectDetails Vehicle(bool isPrimary = false) =>
        new(AssetType.Vehicle, "2022 Volvo FH16 tractor unit", "Rear bumper and lights damaged", "VIN YV2RT40A0NB123456", isPrimary);

    public static Claim NewDraft(PolicySnapshot? policy = null, Actor? createdBy = null, int sequence = 142) =>
        Claim.Create(
            ClaimNumber.Create(2026, sequence),
            ValidLoss(),
            "Auto",
            policy ?? MeridianPolicy,
            ClaimSeverity.Standard,
            null,
            createdBy ?? Handler1,
            Now);

    public static Claim NewDraftWithoutPolicy() =>
        Claim.Create(ClaimNumber.Create(2026, 143), ValidLoss(), "Auto", null, ClaimSeverity.Standard, null, Handler1, Now);

    /// <summary>A Draft with a claimant and a risk object, and no open issues of any kind.</summary>
    public static Claim NewCompleteDraft()
    {
        var claim = NewDraft();
        claim.AddParty(Claimant(), Handler1, Now);
        claim.AddRiskObject(Vehicle(), Handler1, Now);
        return claim;
    }

    public static ValidationFinding Critical(string code) => new(code, ValidationSeverity.Critical, $"{code} message", "Field");

    public static ValidationFinding Warning(string code) => new(code, ValidationSeverity.Warning, $"{code} message", "Field");
}
