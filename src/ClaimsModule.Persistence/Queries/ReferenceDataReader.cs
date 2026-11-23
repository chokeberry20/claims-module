using AutoMapper;
using AutoMapper.QueryableExtensions;
using ClaimsModule.Application.ReferenceData;
using ClaimsModule.Domain.ReferenceData;
using Microsoft.EntityFrameworkCore;

namespace ClaimsModule.Persistence.Queries;

/// <summary>Reference data reads: AsNoTracking projections through the Application AutoMapper profiles.</summary>
internal sealed class ReferenceDataReader(ClaimsDbContext context, IMapper mapper) : IReferenceDataReader
{
    public async Task<IReadOnlyList<CauseOfLossCodeDto>> ListCauseOfLossCodesAsync(bool includeInactive, PerilCategory? perilCategory, CancellationToken cancellationToken) =>
        await context.CauseOfLossCodes
            .AsNoTracking()
            .Where(c => includeInactive || c.IsActive)
            .Where(c => perilCategory == null || c.PerilCategory == perilCategory)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Code)
            .ProjectTo<CauseOfLossCodeDto>(mapper.ConfigurationProvider)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ClaimStatusTransitionDto>> ListStatusTransitionsAsync(CancellationToken cancellationToken) =>
        await context.ClaimStatusTransitions
            .AsNoTracking()
            .OrderBy(t => t.FromStatus).ThenBy(t => t.ToStatus)
            .Select(t => new ClaimStatusTransitionDto
            {
                FromStatus = t.FromStatus,
                ToStatus = t.ToStatus,
                RequiredRole = t.RequiredPermission,
                RequiresReason = t.RequiresReason,
                Description = t.Description,
            })
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PolicySummaryDto>> SearchPoliciesAsync(string? term, int limit, DateOnly today, CancellationToken cancellationToken)
    {
        var policies = context.Policies.AsNoTracking();
        if (!string.IsNullOrEmpty(term))
        {
            // Contains → LIKE '%term%' with escaped wildcards; the column collation is case-insensitive.
            policies = policies.Where(p => p.PolicyNumber.Contains(term) || p.ClientName.Contains(term));
        }

        return await policies
            .OrderBy(p => p.PolicyNumber)
            .Take(limit)
            .ProjectTo<PolicySummaryDto>(mapper.ConfigurationProvider, new { today })
            .ToListAsync(cancellationToken);
    }

    public async Task<PolicyCoverageDto?> GetPolicyCoverageAsync(Guid policyId, DateOnly today, CancellationToken cancellationToken)
    {
        var policy = await context.Policies.AsNoTracking().SingleOrDefaultAsync(p => p.Id == policyId, cancellationToken);
        return policy is null ? null : mapper.Map<PolicyCoverageDto>(policy) with { Status = policy.StatusOn(today) };
    }
}
