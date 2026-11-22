namespace ClaimsModule.Persistence.Seed;

/// <summary>
/// Seeded reference data (S5.5, S5.6, S15.4). It is applied through HasData in migrations, never
/// from startup code.
/// </summary>
public static partial class SeedData
{
    /// <summary>
    /// The single fixed organisation for this deployment (S15.1: "Seed your implementation with a
    /// single fixed OrganisationId GUID"). Application logic never uses this constant. The runtime
    /// tenant comes from configuration (<c>Tenancy:OrganisationId</c>), and startup checks that the
    /// two match.
    /// </summary>
    public static readonly Guid DefaultOrganisationId = new("3f2b8c1e-6d4a-4e9b-a7c2-5d1e0f9b8a64");
}
