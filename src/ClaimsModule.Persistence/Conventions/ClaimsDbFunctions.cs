using ClaimsModule.Domain.Claims;

namespace ClaimsModule.Persistence.Conventions;

/// <summary>
/// Database functions usable in LINQ queries. They are never executed in .NET; EF translates them
/// (registered in ClaimsDbContext.OnModelCreating).
/// </summary>
public static class ClaimsDbFunctions
{
    /// <summary>
    /// The stored text of a value-converted claim number, <c>CAST([ClaimNumber] AS nvarchar(50))</c>,
    /// so string operators such as <c>Contains</c> translate to SQL <c>LIKE</c> (S10.1 partial search).
    /// </summary>
    public static string ClaimNumberText(ClaimNumber claimNumber) =>
        throw new NotSupportedException($"{nameof(ClaimNumberText)} is translated to SQL by EF Core and can't be called directly.");
}
