using ClaimsModule.Application.Abstractions;
using ClaimsModule.Application.Abstractions.Persistence;
using ClaimsModule.Application.Common.Exceptions;
using ClaimsModule.Application.Common.Messaging;
using ClaimsModule.Application.ReferenceData;
using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Claims.Workflow;
using ClaimsModule.Domain.Common;
using ClaimsModule.Domain.Reserves;
using FluentValidation;
using MediatR;

namespace ClaimsModule.Application.Claims;

/// <summary>
/// PUT /api/claims/{id}/status (S4, S10.1, BR-ST-01..04). The requested pair is first checked against
/// the ClaimStatusTransitions rows in the database (B3.3.1 "validate against allowed transitions
/// table", task 23a, D-59), then the domain checks the preconditions and applies it.
/// </summary>
public sealed record TransitionClaimStatusCommand(Guid ClaimId, ClaimStatus TargetStatus) : IdempotentCommand, ICommand<ClaimChangeResult>
{
    public string? Reason { get; init; }
    public string? ClosureJustification { get; init; }
    public bool AcknowledgeWarnings { get; init; }
}

internal sealed class TransitionClaimStatusValidator : AbstractValidator<TransitionClaimStatusCommand>
{
    public TransitionClaimStatusValidator()
    {
        RuleFor(c => c.TargetStatus).IsInEnum().WithErrorCode("STATUS_INVALID").WithMessage("Unknown claim status.");
        RuleFor(c => c.Reason).MaximumLength(Claim.MaxReasonLength).WithErrorCode("REASON_TOO_LONG")
            .WithMessage($"Reason must be at most {Claim.MaxReasonLength} characters.");
        RuleFor(c => c.ClosureJustification).MaximumLength(Claim.MaxReasonLength).WithErrorCode("REASON_TOO_LONG")
            .WithMessage($"Closure justification must be at most {Claim.MaxReasonLength} characters.");
    }
}

/// <summary>Reads the allowed (from, to) pairs from the ClaimStatusTransitions table (D-08, D-59).</summary>
internal sealed class TransitionTable(IReferenceDataReader reader)
{
    public async Task<IReadOnlyList<ClaimStatusTransitionDto>> FromAsync(ClaimStatus from, CancellationToken cancellationToken) =>
        (await reader.ListStatusTransitionsAsync(cancellationToken)).Where(t => t.FromStatus == from).ToList();

    /// <summary>Rejects a pair that has no row: 422 listing the valid next statuses (BR-ST-01), or 403 for the row's role.</summary>
    public async Task EnsureAllowedAsync(ClaimStatus from, ClaimStatus to, Actor actor, CancellationToken cancellationToken)
    {
        var rows = await FromAsync(from, cancellationToken);
        var row = rows.FirstOrDefault(t => t.ToStatus == to);
        if (row is null)
        {
            var valid = rows.Count == 0 ? "none" : string.Join(", ", rows.Select(t => t.ToStatus));
            throw new DomainRuleViolationException(
                "INVALID_TRANSITION",
                $"Transition from {from} to {to} is not permitted. Valid next statuses: {valid}.",
                "StatusTransition");
        }

        if (!actor.HasAtLeast(row.RequiredRole))
        {
            throw new ForbiddenAccessException(row.RequiredRole);
        }
    }
}

internal sealed class TransitionClaimStatusHandler(
    IClaimRepository claims,
    IReserveComponentRepository reserves,
    TransitionTable transitions,
    ICurrentUser currentUser,
    TimeProvider clock) : IRequestHandler<TransitionClaimStatusCommand, ClaimChangeResult>
{
    public async Task<ClaimChangeResult> Handle(TransitionClaimStatusCommand request, CancellationToken cancellationToken)
    {
        var actor = currentUser.GetActor();
        var claim = await claims.GetRequiredAsync(request.ClaimId, cancellationToken);
        await transitions.EnsureAllowedAsync(claim.Status, request.TargetStatus, actor, cancellationToken);

        var components = await reserves.ListForClaimAsync(claim.Id, cancellationToken);
        var now = clock.GetUtcNow();
        claim.TransitionTo(
            request.TargetStatus,
            new TransitionRequest(request.Reason, request.ClosureJustification, request.AcknowledgeWarnings),
            ReserveSummary.ToReserveFacts(components),
            actor,
            now);

        // D-21: closing closes the components (balances unchanged); reopening reactivates them.
        if (claim.Status == ClaimStatus.Closed)
        {
            foreach (var component in components.Where(c => c.Status == ReserveComponentStatus.Active))
            {
                component.Close();
            }
        }
        else if (request.TargetStatus == ClaimStatus.Reopened)
        {
            foreach (var component in components)
            {
                component.Reactivate();
            }
        }

        return ClaimChangeResult.From(claim);
    }
}

// ------------------------------------------------------------------ pre-flight (D-27)

/// <summary>One possible next status, with what blocks it or needs confirming (S11.3 transition menu and close checklist).</summary>
public sealed record TransitionOptionDto(
    ClaimStatus TargetStatus,
    UserRole RequiredRole,
    bool HasRequiredRole,
    bool RequiresReason,
    bool IsAllowed,
    bool CanProceed,
    IReadOnlyList<WarningDto> BlockingConditions,
    IReadOnlyList<WarningDto> ConfirmationsRequired);

/// <summary>GET /api/claims/{id}/transitions: the valid next statuses for the current user, evaluated (D-27).</summary>
public sealed record GetAvailableTransitionsQuery(Guid ClaimId) : IQuery<IReadOnlyList<TransitionOptionDto>>;

internal sealed class GetAvailableTransitionsHandler(
    IClaimRepository claims,
    IReserveComponentRepository reserves,
    TransitionTable transitions,
    ICurrentUser currentUser) : IRequestHandler<GetAvailableTransitionsQuery, IReadOnlyList<TransitionOptionDto>>
{
    public async Task<IReadOnlyList<TransitionOptionDto>> Handle(GetAvailableTransitionsQuery request, CancellationToken cancellationToken)
    {
        var claim = await claims.GetRequiredAsync(request.ClaimId, cancellationToken);
        var allowedInTable = (await transitions.FromAsync(claim.Status, cancellationToken)).Select(t => t.ToStatus).ToHashSet();
        var facts = ReserveSummary.ToReserveFacts(await reserves.ListForClaimAsync(claim.Id, cancellationToken));

        return claim.GetAvailableTransitions(facts, currentUser.GetActor())
            .Where(e => allowedInTable.Contains(e.Target))
            .Select(e => new TransitionOptionDto(
                e.Target,
                e.RequiredRole,
                e.HasRequiredRole,
                e.RequiresReason,
                e.IsAllowed,
                e.CanProceed,
                e.BlockingConditions.Select(WarningDto.From).ToList(),
                e.ConfirmationsRequired.Select(WarningDto.From).ToList()))
            .ToList();
    }
}
