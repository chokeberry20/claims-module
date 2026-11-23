using ClaimsModule.API.Contracts;
using ClaimsModule.API.Hosting;
using ClaimsModule.Application.Claims;
using ClaimsModule.Application.Common.Exceptions;
using ClaimsModule.Application.Reserves;
using ClaimsModule.Domain.Reserves;
using Microsoft.AspNetCore.Mvc;

namespace ClaimsModule.API.Controllers;

/// <summary>
/// Reserves (S6, S10.2, B3.3.3). Approve, reject, retract and retry act on one reserve transaction
/// (<c>{transactionId}</c>), because approval is per transaction (D-04).
/// </summary>
[Route("api/claims/{claimId:guid}/reserves")]
[Tags("Reserves")]
public sealed class ReservesController : ApiControllerBase
{
    /// <summary>Current balance per component, totals and the full transaction history, newest first.</summary>
    [HttpGet]
    [ProducesResponseType<ClaimReservesDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ClaimReservesDto> Get(Guid claimId, CancellationToken cancellationToken) =>
        await Sender.Send(new GetClaimReservesQuery(claimId), cancellationToken);

    /// <summary>Opens, adjusts or reverses a reserve component (D-16).</summary>
    /// <remarks>
    /// Up to $10,000 (change size) is auto-approved and the GL posting job is enqueued; above that it waits
    /// for a supervisor (up to $100,000) or a manager (BR-R-02..04). Going over $10M in total needs the
    /// manager override (BR-R-07).
    /// </remarks>
    [HttpPost]
    [Idempotent]
    [ProducesResponseType<ReserveSubmissionDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ApiError>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> Submit(Guid claimId, SubmitReserveRequest request, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(
            new SubmitReserveTransactionCommand(claimId, request.Component, request.TransactionType, request.Amount, request.ChangeReason)
            {
                IdempotencyKey = IdempotencyKey,
            },
            cancellationToken);
        return CreatedAtAction(nameof(Get), new { claimId }, result);
    }

    /// <summary>Adjusts an existing component to a new target balance (the brief's PUT alias for an Adjust, D-04).</summary>
    [HttpPut("{componentId:guid}")]
    [Idempotent]
    [ProducesResponseType<ReserveSubmissionDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ApiError>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> Adjust(Guid claimId, Guid componentId, AdjustReserveRequest request, CancellationToken cancellationToken)
    {
        var reserves = await Sender.Send(new GetClaimReservesQuery(claimId), cancellationToken);
        var component = reserves.Totals.Components.FirstOrDefault(c => c.ReserveComponentId == componentId)
            ?? throw new NotFoundException("Reserve component", componentId);
        var result = await Sender.Send(
            new SubmitReserveTransactionCommand(claimId, component.ComponentType, ReserveTransactionType.Adjust, request.Amount, request.ChangeReason)
            {
                IdempotencyKey = IdempotencyKey,
            },
            cancellationToken);
        return CreatedAtAction(nameof(Get), new { claimId }, result);
    }

    /// <summary>Approves a pending transaction (supervisor or manager; no self-approval; amount within the approver's authority).</summary>
    [HttpPost("{transactionId:guid}/approve")]
    [Idempotent]
    [ProducesResponseType<ReserveTransactionStateDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ApiError>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ReserveTransactionStateDto> Approve(Guid claimId, Guid transactionId, CancellationToken cancellationToken) =>
        await Sender.Send(new ApproveReserveTransactionCommand(claimId, transactionId) { IdempotencyKey = IdempotencyKey }, cancellationToken);

    /// <summary>Rejects a pending transaction with a reason (supervisor or manager). A revised amount is a new submission (BR-R-06).</summary>
    [HttpPost("{transactionId:guid}/reject")]
    [Idempotent]
    [ProducesResponseType<ReserveTransactionStateDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ApiError>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ReserveTransactionStateDto> Reject(Guid claimId, Guid transactionId, RejectReserveRequest request, CancellationToken cancellationToken) =>
        await Sender.Send(new RejectReserveTransactionCommand(claimId, transactionId, request.RejectionReason) { IdempotencyKey = IdempotencyKey }, cancellationToken);

    /// <summary>The submitter withdraws a pending transaction before it is decided (status Cancelled).</summary>
    [HttpPost("{transactionId:guid}/retract")]
    [Idempotent]
    [ProducesResponseType<ReserveTransactionStateDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ApiError>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ReserveTransactionStateDto> Retract(Guid claimId, Guid transactionId, CancellationToken cancellationToken) =>
        await Sender.Send(new RetractReserveTransactionCommand(claimId, transactionId) { IdempotencyKey = IdempotencyKey }, cancellationToken);

    /// <summary>Re-enqueues a failed GL posting (supervisor or above, D-28).</summary>
    [HttpPost("{transactionId:guid}/retry-posting")]
    [Idempotent]
    [ProducesResponseType<ReserveTransactionStateDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ApiError>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ReserveTransactionStateDto> RetryPosting(Guid claimId, Guid transactionId, CancellationToken cancellationToken) =>
        await Sender.Send(new RetryGlPostingCommand(claimId, transactionId) { IdempotencyKey = IdempotencyKey }, cancellationToken);
}
