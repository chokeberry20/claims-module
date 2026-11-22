using ClaimsModule.Domain.Claims.Workflow;
using ClaimsModule.Domain.ReferenceData;

namespace ClaimsModule.Persistence.Seed;

public static partial class SeedData
{
    /// <summary>A fixed audit timestamp for seeded rows, so the migrations are deterministic.</summary>
    public static readonly DateTimeOffset SeededAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    internal sealed record PolicySeed(string PolicyNumber, string ClientName, DateOnly EffectiveDate, DateOnly ExpirationDate, string[] CoverageTypes)
    {
        public Guid Id => SeedIds.For("policy:" + PolicyNumber);
    }

    internal sealed record CauseOfLossSeed(string Code, string Name, PerilCategory PerilCategory, bool IsActive, int SortOrder)
    {
        public Guid Id => SeedIds.For("cause-of-loss:" + Code);
    }

    /// <summary>
    /// S5.5 minimum dataset (the first five, verbatim), plus three in-force policies so the demo
    /// stays valid as dates pass (D-34, D-52). POL-2023-000099 is the expired policy used to test the
    /// BR-C-02 warning.
    /// </summary>
    internal static readonly PolicySeed[] Policies =
    [
        new("POL-2024-001001", "Meridian Transport LLC", new(2024, 1, 1), new(2026, 12, 31), ["Vehicle", "Cargo"]),
        new("POL-2024-001002", "Harborview Properties Inc", new(2024, 6, 1), new(2026, 5, 31), ["Property", "Liability"]),
        new("POL-2025-002001", "Coastal Builders Group", new(2025, 3, 1), new(2027, 2, 28), ["Property", "Equipment"]),
        new("POL-2025-002002", "Stanton Medical Group", new(2025, 1, 1), new(2026, 12, 31), ["Liability", "Vehicle"]),
        new("POL-2023-000099", "Archived Corp", new(2020, 1, 1), new(2021, 12, 31), ["Property"]),
        new("POL-2026-003001", "Northwind Logistics Ltd", new(2026, 1, 1), new(2027, 12, 31), ["Vehicle", "Cargo"]),
        new("POL-2026-003002", "Bluewater Marina Holdings", new(2026, 3, 1), new(2028, 2, 29), ["Property", "Liability", "Equipment"]),
        new("POL-2025-002010", "Summit Engineering Partners", new(2025, 7, 1), new(2027, 6, 30), ["Equipment", "Property"]),
    ];

    /// <summary>
    /// S5.6: the ten required active codes, verbatim. COL-LEGACY-SUBS is an extra, deliberately
    /// inactive code, so BR-C-05 ("inactive code is Critical") can be shown with real data (D-52).
    /// </summary>
    internal static readonly CauseOfLossSeed[] CausesOfLoss =
    [
        new("COL-FIRE", "Fire", PerilCategory.Property, true, 10),
        new("COL-FLOOD", "Flood", PerilCategory.Weather, true, 20),
        new("COL-THEFT", "Theft", PerilCategory.Crime, true, 30),
        new("COL-VEH-COL", "Vehicle Collision", PerilCategory.Auto, true, 40),
        new("COL-VEH-COMP", "Vehicle Comprehensive", PerilCategory.Auto, true, 50),
        new("COL-LIAB", "Third Party Liability", PerilCategory.Liability, true, 60),
        new("COL-EQUIP", "Equipment Breakdown", PerilCategory.Equipment, true, 70),
        new("COL-WIND", "Wind / Storm", PerilCategory.Weather, true, 80),
        new("COL-INJURY", "Bodily Injury", PerilCategory.Liability, true, 90),
        new("COL-OTHER", "Other / Unknown", PerilCategory.General, true, 100),
        new("COL-LEGACY-SUBS", "Subsidence (legacy, retired)", PerilCategory.Property, false, 900),
    ];

    internal static Guid TransitionId(ClaimStatusTransitionRule rule) => SeedIds.For($"transition:{rule.From}->{rule.To}");
}
