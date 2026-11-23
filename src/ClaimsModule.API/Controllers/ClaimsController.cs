using ClaimsModule.API.Contracts;
using ClaimsModule.API.Hosting;
using ClaimsModule.Application.Audit;
using ClaimsModule.Application.Claims;
using ClaimsModule.Application.Reserves;
using Microsoft.AspNetCore.Mvc;

namespace ClaimsModule.API.Controllers;

/// <summary>Claims: FNOL intake, list and detail, lifecycle, maintenance, parties, risk objects, audit log (S10.1).</summary>
[Route("api/claims")]
[Tags("Claims")]
public sealed class ClaimsController : ApiControllerBase
{
    /// <summary>Creates a claim (FNOL) in one transaction: loss event, parties, risk objects and an optional initial reserve.</summary>
    /// <remarks>
    /// Request-level rule breaks (future loss date, short description, unknown cause code, invalid amount)
    /// give 422 and save nothing. State-level findings (no claimant, no policy, outside the policy period,
    /// no risk objects) are saved on the Draft claim and returned as validation issues (D-05).
    /// </remarks>
    [HttpPost]
    [Idempotent]
    [ProducesResponseType<CreateClaimResult>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> Create(CreateClaimRequest request, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(request.ToCommand(IdempotencyKey), cancellationToken);
        return CreatedAtAction(nameof(Get), new { claimId = result.ClaimId }, result);
    }

    /// <summary>Lists claims, paginated and filtered (S10.1, S11.1).</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<ClaimSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<PagedResult<ClaimSummaryDto>> List([FromQuery] ClaimListRequest request, CancellationToken cancellationToken) =>
        await Sender.Send(request.ToQuery(), cancellationToken);

    /// <summary>Full claim detail: parties, risk objects, validation issues, reserve summary, documents and recent audit entries.</summary>
    [HttpGet("{claimId:guid}")]
    [ProducesResponseType<ClaimDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ClaimDetailDto> Get(Guid claimId, CancellationToken cancellationToken) =>
        await Sender.Send(new GetClaimQuery(claimId), cancellationToken);

    /// <summary>Moves the claim to another status (S4, BR-ST-01..04). A refused transition gives 422 with the blocking conditions.</summary>
    [HttpPut("{claimId:guid}/status")]
    [Idempotent]
    [ProducesResponseType<ClaimChangeResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ApiError>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ClaimChangeResult> TransitionStatus(Guid claimId, TransitionStatusRequest request, CancellationToken cancellationToken) =>
        await Sender.Send(new TransitionClaimStatusCommand(claimId, request.TargetStatus)
        {
            Reason = request.Reason,
            ClosureJustification = request.ClosureJustification,
            AcknowledgeWarnings = request.AcknowledgeWarnings,
            IdempotencyKey = IdempotencyKey,
        }, cancellationToken);

    /// <summary>The statuses this user can move the claim to next, with the conditions that block or warn (pre-flight checklist, D-27).</summary>
    [HttpGet("{claimId:guid}/transitions")]
    [ProducesResponseType<IReadOnlyList<TransitionOptionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IReadOnlyList<TransitionOptionDto>> AvailableTransitions(Guid claimId, CancellationToken cancellationToken) =>
        await Sender.Send(new GetAvailableTransitionsQuery(claimId), cancellationToken);

    /// <summary>The claim's append-only audit log, newest first (S14, BR-A-02).</summary>
    [HttpGet("{claimId:guid}/audit")]
    [ProducesResponseType<PagedResult<AuditLogEntryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<PagedResult<AuditLogEntryDto>> AuditLog(
        Guid claimId, [FromQuery] string? eventType, CancellationToken cancellationToken, [FromQuery] int page = 1, [FromQuery] int pageSize = 50) =>
        await Sender.Send(new GetAuditLogQuery(claimId) { EventType = eventType, Page = page, PageSize = pageSize }, cancellationToken);

    // ------------------------------------------------------------------ maintenance (D-31)

    /// <summary>Corrects the loss details of a Draft claim and re-validates it.</summary>
    [HttpPut("{claimId:guid}/loss-details")]
    [Idempotent]
    [ProducesResponseType<ClaimChangeResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ClaimChangeResult> UpdateLossDetails(Guid claimId, UpdateLossDetailsRequest request, CancellationToken cancellationToken) =>
        await Sender.Send(new UpdateLossDetailsCommand(claimId)
        {
            LossDate = request.LossDate,
            LossDescription = request.LossDescription,
            CauseOfLossCode = request.CauseOfLossCode,
            LossLocation = request.LossLocation,
            EstimatedLossAmount = request.EstimatedLossAmount,
            PoliceReportNumber = request.PoliceReportNumber,
            IdempotencyKey = IdempotencyKey,
        }, cancellationToken);

    /// <summary>Links a policy (BR-C-06: reserves need one).</summary>
    [HttpPut("{claimId:guid}/policy")]
    [Idempotent]
    [ProducesResponseType<ClaimChangeResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ClaimChangeResult> LinkPolicy(Guid claimId, LinkPolicyRequest request, CancellationToken cancellationToken) =>
        await Sender.Send(new LinkPolicyCommand(claimId, request.PolicyId) { IdempotencyKey = IdempotencyKey }, cancellationToken);

    /// <summary>Assigns the handler (supervisor or above, D-10).</summary>
    [HttpPut("{claimId:guid}/assignment")]
    [Idempotent]
    [ProducesResponseType<ClaimChangeResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ApiError>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ClaimChangeResult> AssignHandler(Guid claimId, AssignHandlerRequest request, CancellationToken cancellationToken) =>
        await Sender.Send(new AssignHandlerCommand(claimId, request.HandlerId) { IdempotencyKey = IdempotencyKey }, cancellationToken);

    /// <summary>Replaces the claim notes.</summary>
    [HttpPut("{claimId:guid}/notes")]
    [Idempotent]
    [ProducesResponseType<ClaimChangeResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ClaimChangeResult> UpdateNotes(Guid claimId, UpdateNotesRequest request, CancellationToken cancellationToken) =>
        await Sender.Send(new UpdateNotesCommand(claimId, request.Notes) { IdempotencyKey = IdempotencyKey }, cancellationToken);

    /// <summary>Changes the severity (D-33).</summary>
    [HttpPut("{claimId:guid}/severity")]
    [Idempotent]
    [ProducesResponseType<ClaimChangeResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ClaimChangeResult> ChangeSeverity(Guid claimId, ChangeSeverityRequest request, CancellationToken cancellationToken) =>
        await Sender.Send(new ChangeSeverityCommand(claimId, request.Severity) { IdempotencyKey = IdempotencyKey }, cancellationToken);

    /// <summary>Re-runs the state validation (S5.4) and returns the current issues.</summary>
    [HttpPost("{claimId:guid}/validate")]
    [Idempotent]
    [ProducesResponseType<ClaimChangeResult>(StatusCodes.Status200OK)]
    public async Task<ClaimChangeResult> Validate(Guid claimId, CancellationToken cancellationToken) =>
        await Sender.Send(new ValidateClaimCommand(claimId) { IdempotencyKey = IdempotencyKey }, cancellationToken);

    /// <summary>Acknowledges every open warning (BR-C-02), so they no longer block Open.</summary>
    [HttpPost("{claimId:guid}/acknowledge-warnings")]
    [Idempotent]
    [ProducesResponseType<AcknowledgeWarningsResult>(StatusCodes.Status200OK)]
    public async Task<AcknowledgeWarningsResult> AcknowledgeWarnings(Guid claimId, CancellationToken cancellationToken) =>
        await Sender.Send(new AcknowledgeWarningsCommand(claimId) { IdempotencyKey = IdempotencyKey }, cancellationToken);

    /// <summary>Waives a Critical validation issue with a reason (supervisor or above; NO_CLAIMANT can't be waived; D-07).</summary>
    [HttpPost("{claimId:guid}/validation-issues/{issueId:guid}/waive")]
    [Idempotent]
    [ProducesResponseType<ClaimChangeResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ApiError>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ClaimChangeResult> WaiveIssue(Guid claimId, Guid issueId, WaiveIssueRequest request, CancellationToken cancellationToken) =>
        await Sender.Send(new WaiveValidationIssueCommand(claimId, issueId, request.Reason) { IdempotencyKey = IdempotencyKey }, cancellationToken);

    /// <summary>Sets or clears the manager override for the $10M aggregate reserve limit (manager only, BR-R-07, D-19).</summary>
    [HttpPut("{claimId:guid}/reserve-limit-override")]
    [Idempotent]
    [ProducesResponseType<ClaimChangeResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden, "application/problem+json")]
    public async Task<ClaimChangeResult> SetReserveLimitOverride(Guid claimId, ReserveLimitOverrideRequest request, CancellationToken cancellationToken) =>
        await Sender.Send(new SetReserveLimitOverrideCommand(claimId, request.Enabled) { IdempotencyKey = IdempotencyKey }, cancellationToken);

    // ------------------------------------------------------------------ parties and risk objects

    /// <summary>Adds a party (BR-P-02: duplicate roles allowed).</summary>
    [HttpPost("{claimId:guid}/parties")]
    [Idempotent]
    [ProducesResponseType<ClaimChildAddedResult>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> AddParty(Guid claimId, PartyInput request, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new AddPartyCommand(claimId, request) { IdempotencyKey = IdempotencyKey }, cancellationToken);
        return CreatedAtAction(nameof(Get), new { claimId }, result);
    }

    /// <summary>Soft-removes a party. Removing the last active Claimant gives 422 (BR-P-01).</summary>
    [HttpDelete("{claimId:guid}/parties/{partyId:guid}")]
    [Idempotent]
    [ProducesResponseType<ClaimChangeResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ApiError>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ClaimChangeResult> RemoveParty(Guid claimId, Guid partyId, CancellationToken cancellationToken) =>
        await Sender.Send(new RemovePartyCommand(claimId, partyId) { IdempotencyKey = IdempotencyKey }, cancellationToken);

    /// <summary>Adds a risk object (at most one primary).</summary>
    [HttpPost("{claimId:guid}/risk-objects")]
    [Idempotent]
    [ProducesResponseType<ClaimChildAddedResult>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> AddRiskObject(Guid claimId, RiskObjectInput request, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new AddRiskObjectCommand(claimId, request) { IdempotencyKey = IdempotencyKey }, cancellationToken);
        return CreatedAtAction(nameof(Get), new { claimId }, result);
    }

    /// <summary>Removes a risk object.</summary>
    [HttpDelete("{claimId:guid}/risk-objects/{riskObjectId:guid}")]
    [Idempotent]
    [ProducesResponseType<ClaimChangeResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ClaimChangeResult> RemoveRiskObject(Guid claimId, Guid riskObjectId, CancellationToken cancellationToken) =>
        await Sender.Send(new RemoveRiskObjectCommand(claimId, riskObjectId) { IdempotencyKey = IdempotencyKey }, cancellationToken);
}
