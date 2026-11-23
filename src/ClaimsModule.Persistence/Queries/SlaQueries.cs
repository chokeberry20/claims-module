using ClaimsModule.Application.Claims;
using ClaimsModule.Domain.Claims;
using ClaimsModule.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;

namespace ClaimsModule.Persistence.Queries;

/// <summary>
/// SLA candidates (S12.2, D-13). Last activity is the shadow UpdatedAt, or CreatedAt for a claim never
/// updated. SLA-only changes don't touch UpdatedAt (AuditableEntityInterceptor), so a flagged claim
/// stays inactive until a real update clears the flag.
/// </summary>
internal sealed class SlaQueries(ClaimsDbContext context) : ISlaQueries
{
    public async Task<IReadOnlyList<SlaCandidate>> FindBreachCandidatesAsync(
        DateTimeOffset inactiveBefore, DateTimeOffset lastBreachBefore, CancellationToken cancellationToken) =>
        await context.Claims
            .AsNoTracking()
            .Where(c => c.Status == ClaimStatus.Draft || c.Status == ClaimStatus.Open)
            .Select(c => new
            {
                c.Id,
                LastActivityAt = EF.Property<DateTimeOffset?>(c, ShadowProperties.UpdatedAt) ?? EF.Property<DateTimeOffset>(c, ShadowProperties.CreatedAt),
                c.LastSlaBreachAt,
            })
            .Where(c => c.LastActivityAt < inactiveBefore && (c.LastSlaBreachAt == null || c.LastSlaBreachAt <= lastBreachBefore))
            .OrderBy(c => c.LastActivityAt)
            .Select(c => new SlaCandidate(c.Id, c.LastActivityAt))
            .ToListAsync(cancellationToken);
}
