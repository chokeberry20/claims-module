using ClaimsModule.Application.Abstractions;
using ClaimsModule.Application.Abstractions.Persistence;
using ClaimsModule.Application.Claims;
using ClaimsModule.Application.Common.Messaging;
using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Common;
using ClaimsModule.Domain.Reserves;
using FluentValidation;
using MediatR;

namespace ClaimsModule.Application.Reserves;

/// <summary>
/// Loads the claim and its reserve components for a reserve command and applies the claim-level
/// preconditions first (task 25a, your P1 review): BR-C-06 (a policy must be linked) and D-20 (claim
/// status). <see cref="Touch"/> then marks reserve activity on the claim, so its row version changes
/// and a concurrent claim edit conflicts instead of racing (PLAN §3).
/// </summary>
internal sealed class ReserveContextLoader(IClaimRepository claims, IReserveComponentRepository reserves)
{
    public async Task<ReserveContext> LoadAsync(Guid claimId, CancellationToken cancellationToken)
    {
        var claim = await claims.GetRequiredAsync(claimId, cancellationToken);
        claim.EnsureReserveChangesAllowed();
        var components = await reserves.ListForClaimAsync(claimId, cancellationToken);
        return new ReserveContext(claim, components);
    }
}

internal sealed record ReserveContext(Claim Claim, IReadOnlyList<ClaimReserveComponent> Components)
{
    public Money ApprovedGrossTotal => ReserveSummary.ApprovedGrossTotal(Components);

    public ClaimReserveComponent ComponentOf(Guid transactionId) =>
        Components.FirstOrDefault(c => c.Transactions.Any(t => t.Id == transactionId))
        ?? throw new DomainNotFoundException(nameof(ReserveTransaction), transactionId);

    public void Touch(DateTimeOffset now) => Claim.RecordReserveActivity(now);
}

/// <summary>A reserve transaction's id and its state after an approval-workflow command.</summary>
public sealed record ReserveTransactionStateDto(
    Guid ReserveComponentId,
    Guid TransactionId,
    ApprovalStatus ApprovalStatus,
    PostingStatus PostingStatus,
    decimal ComponentBalance)
{
    internal static ReserveTransactionStateDto From(ClaimReserveComponent component, Guid transactionId)
    {
        var t = component.Transactions.Single(x => x.Id == transactionId);
        return new(component.Id, t.Id, t.ApprovalStatus, t.PostingStatus, component.CurrentAmount.Amount);
    }
}

// ------------------------------------------------------------------ submit (S6, S10.2, D-04, D-16)

/// <summary>
/// POST /api/claims/{id}/reserves (and PUT /reserves/{componentId} as an Adjust alias, D-04).
/// Add opens a component with its initial balance; Adjust sets a new target balance; Reverse
/// brings it to zero (D-16).
/// </summary>
public sealed record SubmitReserveTransactionCommand(
    Guid ClaimId,
    ReserveComponentType ComponentType,
    ReserveTransactionType TransactionType,
    decimal? Amount,
    string ChangeReason) : IdempotentCommand, ICommand<ReserveSubmissionDto>;

internal sealed class SubmitReserveTransactionValidator : AbstractValidator<SubmitReserveTransactionCommand>
{
    public SubmitReserveTransactionValidator()
    {
        RuleFor(c => c.ComponentType).IsInEnum().WithErrorCode("RESERVE_COMPONENT_INVALID").WithMessage("Invalid reserve component type.");
        RuleFor(c => c.TransactionType).IsInEnum().WithErrorCode("RESERVE_TRANSACTION_TYPE_INVALID").WithMessage("Invalid reserve transaction type.");
        RuleFor(c => c.Amount).ValidReserveAmount(c => c.ComponentType).When(c => c.TransactionType != ReserveTransactionType.Reverse);
        RuleFor(c => c.Amount).Null().When(c => c.TransactionType == ReserveTransactionType.Reverse)
            .WithErrorCode("RESERVE_REVERSE_AMOUNT").WithMessage("A reversal takes no amount; it brings the balance to zero.");
        RuleFor(c => c.ChangeReason).ValidChangeReason();
    }
}

internal sealed class SubmitReserveTransactionHandler(
    ReserveContextLoader loader,
    IReserveComponentRepository reserves,
    ICurrentUser currentUser,
    TimeProvider clock) : IRequestHandler<SubmitReserveTransactionCommand, ReserveSubmissionDto>
{
    public async Task<ReserveSubmissionDto> Handle(SubmitReserveTransactionCommand request, CancellationToken cancellationToken)
    {
        var context = await loader.LoadAsync(request.ClaimId, cancellationToken);
        var now = clock.GetUtcNow();

        // One component per type per claim (unique index); a new one is created on first use.
        var component = context.Components.FirstOrDefault(c => c.ComponentType == request.ComponentType);
        var isNew = component is null;
        component ??= ClaimReserveComponent.Create(context.Claim.Id, request.ComponentType);

        var result = component.Submit(request.TransactionType, request.Amount, request.ChangeReason, currentUser.GetActor(), context.ApprovedGrossTotal, now);
        if (isNew)
        {
            reserves.Add(component);
        }

        context.Touch(now);
        return ReserveSubmissionDto.From(component, result);
    }
}

// ------------------------------------------------------------------ approve, reject, retract (S6.3, S6.4, BR-R-02..05)

[RequiresRole(UserRole.Supervisor)]
public sealed record ApproveReserveTransactionCommand(Guid ClaimId, Guid TransactionId) : IdempotentCommand, ICommand<ReserveTransactionStateDto>;

internal sealed class ApproveReserveTransactionHandler(ReserveContextLoader loader, ICurrentUser currentUser, TimeProvider clock)
    : IRequestHandler<ApproveReserveTransactionCommand, ReserveTransactionStateDto>
{
    public async Task<ReserveTransactionStateDto> Handle(ApproveReserveTransactionCommand request, CancellationToken cancellationToken)
    {
        var context = await loader.LoadAsync(request.ClaimId, cancellationToken);
        var component = context.ComponentOf(request.TransactionId);
        var now = clock.GetUtcNow();

        // D-19: the $10M limit is re-checked against the approved total right now.
        component.Approve(request.TransactionId, currentUser.GetActor(), context.ApprovedGrossTotal, context.Claim.ReserveLimitOverride, now);
        context.Touch(now);
        return ReserveTransactionStateDto.From(component, request.TransactionId);
    }
}

[RequiresRole(UserRole.Supervisor)]
public sealed record RejectReserveTransactionCommand(Guid ClaimId, Guid TransactionId, string Reason) : IdempotentCommand, ICommand<ReserveTransactionStateDto>;

internal sealed class RejectReserveTransactionValidator : AbstractValidator<RejectReserveTransactionCommand>
{
    public RejectReserveTransactionValidator() =>
        RuleFor(c => c.Reason)
            .NotEmpty().WithErrorCode("REJECTION_REASON_REQUIRED").WithMessage("Rejection reason is required.")
            .MaximumLength(ReserveTransaction.MaxRejectionReasonLength).WithErrorCode("REJECTION_REASON_TOO_LONG")
            .WithMessage($"Rejection reason must be at most {ReserveTransaction.MaxRejectionReasonLength} characters.");
}

internal sealed class RejectReserveTransactionHandler(ReserveContextLoader loader, ICurrentUser currentUser, TimeProvider clock)
    : IRequestHandler<RejectReserveTransactionCommand, ReserveTransactionStateDto>
{
    public async Task<ReserveTransactionStateDto> Handle(RejectReserveTransactionCommand request, CancellationToken cancellationToken)
    {
        var context = await loader.LoadAsync(request.ClaimId, cancellationToken);
        var component = context.ComponentOf(request.TransactionId);
        var now = clock.GetUtcNow();
        component.Reject(request.TransactionId, currentUser.GetActor(), request.Reason, now);
        context.Touch(now);
        return ReserveTransactionStateDto.From(component, request.TransactionId);
    }
}

/// <summary>The submitter withdraws their own pending transaction (→ Cancelled, S6.4).</summary>
public sealed record RetractReserveTransactionCommand(Guid ClaimId, Guid TransactionId) : IdempotentCommand, ICommand<ReserveTransactionStateDto>;

internal sealed class RetractReserveTransactionHandler(ReserveContextLoader loader, ICurrentUser currentUser, TimeProvider clock)
    : IRequestHandler<RetractReserveTransactionCommand, ReserveTransactionStateDto>
{
    public async Task<ReserveTransactionStateDto> Handle(RetractReserveTransactionCommand request, CancellationToken cancellationToken)
    {
        var context = await loader.LoadAsync(request.ClaimId, cancellationToken);
        var component = context.ComponentOf(request.TransactionId);
        var now = clock.GetUtcNow();
        component.Retract(request.TransactionId, currentUser.GetActor(), now);
        context.Touch(now);
        return ReserveTransactionStateDto.From(component, request.TransactionId);
    }
}

// ------------------------------------------------------------------ GL posting retry (D-28)

/// <summary>POST /api/claims/{id}/reserves/{txnId}/retry-posting: re-queues a Failed posting with the same idempotency key.</summary>
[RequiresRole(UserRole.Supervisor)]
public sealed record RetryGlPostingCommand(Guid ClaimId, Guid TransactionId) : IdempotentCommand, ICommand<ReserveTransactionStateDto>;

internal sealed class RetryGlPostingHandler(ReserveContextLoader loader, ICurrentUser currentUser, TimeProvider clock)
    : IRequestHandler<RetryGlPostingCommand, ReserveTransactionStateDto>
{
    public async Task<ReserveTransactionStateDto> Handle(RetryGlPostingCommand request, CancellationToken cancellationToken)
    {
        var context = await loader.LoadAsync(request.ClaimId, cancellationToken);
        var component = context.ComponentOf(request.TransactionId);
        var now = clock.GetUtcNow();
        component.RequestPostingRetry(request.TransactionId, currentUser.GetActor(), now);
        context.Touch(now);
        return ReserveTransactionStateDto.From(component, request.TransactionId);
    }
}

// ------------------------------------------------------------------ $10M override flag (BR-R-05, D-19)

/// <summary>PUT /api/claims/{id}/reserve-limit-override (Manager only).</summary>
[RequiresRole(UserRole.Manager)]
public sealed record SetReserveLimitOverrideCommand(Guid ClaimId, bool Enabled) : IdempotentCommand, ICommand<ClaimChangeResult>;

internal sealed class SetReserveLimitOverrideHandler(IClaimRepository claims, ICurrentUser currentUser, TimeProvider clock)
    : IRequestHandler<SetReserveLimitOverrideCommand, ClaimChangeResult>
{
    public async Task<ClaimChangeResult> Handle(SetReserveLimitOverrideCommand request, CancellationToken cancellationToken)
    {
        var claim = await claims.GetRequiredAsync(request.ClaimId, cancellationToken);
        claim.SetReserveLimitOverride(request.Enabled, currentUser.GetActor(), clock.GetUtcNow());
        return ClaimChangeResult.From(claim);
    }
}
