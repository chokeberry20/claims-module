using ClaimsModule.API.Contracts;
using ClaimsModule.API.Hosting;
using ClaimsModule.Application.ReferenceData;
using Microsoft.AspNetCore.Mvc;

namespace ClaimsModule.API.Controllers;

/// <summary>Reference data (S10.3, B3.3.4).</summary>
[Route("api/reference")]
[Tags("Reference data")]
public sealed class ReferenceController : ApiControllerBase
{
    /// <summary>Active cause-of-loss codes, optionally for one peril category.</summary>
    [HttpGet("cause-of-loss-codes")]
    [ProducesResponseType<IReadOnlyList<CauseOfLossCodeDto>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<CauseOfLossCodeDto>> CauseOfLossCodes([FromQuery] CauseOfLossCodesRequest request, CancellationToken cancellationToken) =>
        await Sender.Send(new GetCauseOfLossCodesQuery(request.IncludeInactive, request.PerilCategory), cancellationToken);

    /// <summary>Every claim status with its valid next statuses, from the ClaimStatusTransitions table (FR-27).</summary>
    [HttpGet("claim-statuses")]
    [ProducesResponseType<IReadOnlyList<ClaimStatusDto>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<ClaimStatusDto>> ClaimStatuses(CancellationToken cancellationToken) =>
        await Sender.Send(new GetClaimStatusesQuery(), cancellationToken);
}

/// <summary>The simulated policy domain (S5.5, S10.3, B3.3.2).</summary>
[Route("api/policies")]
[Tags("Policies")]
public sealed class PoliciesController : ApiControllerBase
{
    /// <summary>Searches policies by part of the policy number or client name (<c>q</c>), with dates, status and coverage types.</summary>
    [HttpGet("search")]
    [ProducesResponseType<IReadOnlyList<PolicySummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IReadOnlyList<PolicySummaryDto>> Search(
        [FromQuery] string? q, CancellationToken cancellationToken, [FromQuery] int limit = SearchPoliciesQuery.DefaultLimit) =>
        await Sender.Send(new SearchPoliciesQuery(q, limit), cancellationToken);

    /// <summary>The coverages of one policy, shown during FNOL.</summary>
    [HttpGet("{policyId:guid}/coverage")]
    [ProducesResponseType<PolicyCoverageDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<PolicyCoverageDto> Coverage(Guid policyId, CancellationToken cancellationToken) =>
        await Sender.Send(new GetPolicyCoverageQuery(policyId), cancellationToken);
}
