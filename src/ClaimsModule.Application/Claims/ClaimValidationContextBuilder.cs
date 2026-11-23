using ClaimsModule.Application.Abstractions.Persistence;
using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Claims.Validation;

namespace ClaimsModule.Application.Claims;

/// <summary>
/// Loads the facts the state validator needs (D-05 layer b): the linked policy's period and whether
/// the cause-of-loss code is still active. Every handler that changes validated data calls
/// <see cref="RevalidateAsync"/>, so the persisted issues always reflect the current claim.
/// </summary>
internal sealed class ClaimValidationContextBuilder(IPolicyRepository policies, ICauseOfLossCodeRepository causeCodes)
{
    public async Task<ClaimValidationContext> BuildAsync(Claim claim, CancellationToken cancellationToken)
    {
        var policy = claim.PolicyId is { } policyId ? await policies.GetByIdAsync(policyId, cancellationToken) : null;
        var cause = await causeCodes.GetByCodeAsync(claim.LossEvent.CauseOfLossCode, cancellationToken);
        return new ClaimValidationContext(policy?.Period, cause?.IsActive == true);
    }

    public async Task RevalidateAsync(Claim claim, DateTimeOffset now, CancellationToken cancellationToken) =>
        claim.Revalidate(await BuildAsync(claim, cancellationToken), now);
}
