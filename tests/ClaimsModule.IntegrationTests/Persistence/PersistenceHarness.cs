using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Common;
using ClaimsModule.Persistence;
using ClaimsModule.Persistence.Interceptors;
using ClaimsModule.Persistence.Seed;

namespace ClaimsModule.IntegrationTests.Persistence;

/// <summary>
/// Builds ClaimsDbContext instances wired like production: the real interceptors, with a
/// controllable clock, user and tenant.
/// </summary>
internal sealed class PersistenceHarness(SqlServerDatabaseFixture db)
{
    private static int _sequence = Random.Shared.Next(1, 5_000_000);

    public static readonly Actor Handler = new(Guid.NewGuid(), "Hannah Handler", UserRole.Handler);
    public static readonly Actor Supervisor = new(Guid.NewGuid(), "Sam Supervisor", UserRole.Supervisor);

    public TestClock Clock { get; } = new(new DateTimeOffset(2026, 11, 23, 9, 0, 0, TimeSpan.Zero));

    public TestCurrentUser User { get; } = new() { Actor = Handler };

    public TestTenantProvider Tenant { get; } = new(SqlServerDatabaseFixture.OrganisationId);

    public ClaimsDbContext NewContext() =>
        new(
            db.Options(
                new AppendOnlyGuardInterceptor(),
                new SoftDeleteInterceptor(Clock),
                new TenantInterceptor(Tenant),
                new AuditableEntityInterceptor(Clock, User)),
            Tenant);

    /// <summary>Unique numbers per test run. Year 2090 keeps them clear of generator tests.</summary>
    public static ClaimNumber NextClaimNumber() => ClaimNumber.Create(2090, Interlocked.Increment(ref _sequence));

    public static readonly SeedData.PolicySeed Meridian = SeedData.Policies.Single(p => p.PolicyNumber == "POL-2024-001001");

    public Claim NewClaim(bool withParty = true)
    {
        var claim = Claim.Create(
            NextClaimNumber(),
            new LossEventDetails(Clock.Now.AddDays(-3), "Rear-end collision on the I-95 northbound exit ramp.", "COL-VEH-COL", "I-95 exit 12", 12_500m),
            "Auto",
            new PolicySnapshot(Meridian.Id, Meridian.PolicyNumber, Meridian.ClientName),
            ClaimSeverity.Standard,
            null,
            Handler,
            Clock.Now);

        if (withParty)
        {
            claim.AddParty(new PartyDetails(PartyRole.Claimant, PartyType.Person, "Jane", "Doe", Email: "jane@example.com"), Handler, Clock.Now);
            claim.AddRiskObject(new RiskObjectDetails(AssetType.Vehicle, "2022 Volvo FH16 tractor unit"), Handler, Clock.Now);
        }

        return claim;
    }
}
