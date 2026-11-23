using ClaimsModule.Application.Abstractions;
using ClaimsModule.Application.Abstractions.Persistence;
using ClaimsModule.Application.Common.Exceptions;
using ClaimsModule.Application.Common.Messaging;
using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Claims.Validation;
using ClaimsModule.Domain.Common;
using FluentValidation;
using MediatR;

namespace ClaimsModule.Application.Claims;

/// <summary>The claim's state after a maintenance command: status plus the re-evaluated validation issues.</summary>
public sealed record ClaimChangeResult(Guid ClaimId, ClaimStatus Status, IReadOnlyList<ValidationIssueDto> ValidationIssues)
{
    public static ClaimChangeResult From(Claim claim) =>
        new(claim.Id, claim.Status, claim.ValidationIssues.Select(ValidationIssueDto.From).ToList());
}

internal static class ClaimRepositoryExtensions
{
    public static async Task<Claim> GetRequiredAsync(this IClaimRepository claims, Guid claimId, CancellationToken cancellationToken) =>
        await claims.GetByIdAsync(claimId, cancellationToken) ?? throw new NotFoundException("Claim", claimId);
}

// ------------------------------------------------------------------ loss details (D-31: Draft only)

public sealed record UpdateLossDetailsCommand(Guid ClaimId) : IdempotentCommand, ICommand<ClaimChangeResult>, ILossDetailsInput
{
    public DateTimeOffset LossDate { get; init; }
    public string LossDescription { get; init; } = string.Empty;
    public string CauseOfLossCode { get; init; } = string.Empty;
    public string? LossLocation { get; init; }
    public decimal? EstimatedLossAmount { get; init; }
    public string? PoliceReportNumber { get; init; }
}

internal sealed class UpdateLossDetailsValidator : AbstractValidator<UpdateLossDetailsCommand>
{
    public UpdateLossDetailsValidator(TimeProvider clock, ICauseOfLossCodeRepository causeCodes) =>
        Include(new LossDetailsValidator(clock, causeCodes));
}

internal sealed class UpdateLossDetailsHandler(
    IClaimRepository claims,
    ICauseOfLossCodeRepository causeCodes,
    ClaimValidationContextBuilder validation,
    ICurrentUser currentUser,
    TimeProvider clock) : IRequestHandler<UpdateLossDetailsCommand, ClaimChangeResult>
{
    public async Task<ClaimChangeResult> Handle(UpdateLossDetailsCommand request, CancellationToken cancellationToken)
    {
        var claim = await claims.GetRequiredAsync(request.ClaimId, cancellationToken);
        var cause = await causeCodes.GetByCodeAsync(request.CauseOfLossCode.Trim(), cancellationToken)
            ?? throw new DomainRuleViolationException("CAUSE_OF_LOSS_INVALID", "Cause of loss code is not recognised or is inactive.", "CauseOfLossCode");
        var now = clock.GetUtcNow();

        claim.UpdateLossDetails(
            new LossEventDetails(request.LossDate, request.LossDescription, cause.Code, request.LossLocation, request.EstimatedLossAmount, request.PoliceReportNumber),
            cause.PerilCategory.ToString(),
            currentUser.GetActor(),
            now);
        await validation.RevalidateAsync(claim, now, cancellationToken);
        return ClaimChangeResult.From(claim);
    }
}

// ------------------------------------------------------------------ link policy (BR-C-06)

public sealed record LinkPolicyCommand(Guid ClaimId, Guid PolicyId) : IdempotentCommand, ICommand<ClaimChangeResult>;

internal sealed class LinkPolicyHandler(
    IClaimRepository claims,
    IPolicyRepository policies,
    ClaimValidationContextBuilder validation,
    ICurrentUser currentUser,
    TimeProvider clock) : IRequestHandler<LinkPolicyCommand, ClaimChangeResult>
{
    public async Task<ClaimChangeResult> Handle(LinkPolicyCommand request, CancellationToken cancellationToken)
    {
        var claim = await claims.GetRequiredAsync(request.ClaimId, cancellationToken);
        var policy = await policies.GetByIdAsync(request.PolicyId, cancellationToken) ?? throw new NotFoundException("Policy", request.PolicyId);
        var now = clock.GetUtcNow();

        claim.LinkPolicy(policy.ToSnapshot(), currentUser.GetActor(), now);
        await validation.RevalidateAsync(claim, now, cancellationToken);
        return ClaimChangeResult.From(claim);
    }
}

// ------------------------------------------------------------------ assign handler (D-10: Supervisor+)

[RequiresRole(UserRole.Supervisor)]
public sealed record AssignHandlerCommand(Guid ClaimId, Guid HandlerId) : IdempotentCommand, ICommand<ClaimChangeResult>;

internal sealed class AssignHandlerHandler(
    IClaimRepository claims,
    IUserDirectory users,
    ICurrentUser currentUser,
    TimeProvider clock) : IRequestHandler<AssignHandlerCommand, ClaimChangeResult>
{
    public async Task<ClaimChangeResult> Handle(AssignHandlerCommand request, CancellationToken cancellationToken)
    {
        var claim = await claims.GetRequiredAsync(request.ClaimId, cancellationToken);
        var handler = await users.FindAsync(request.HandlerId, cancellationToken)
            ?? throw new DomainRuleViolationException("HANDLER_NOT_FOUND", "The selected handler is not a known user.", "AssignedHandlerId");

        claim.AssignHandler(handler.UserId, handler.DisplayName, currentUser.GetActor(), clock.GetUtcNow());
        return ClaimChangeResult.From(claim);
    }
}

// ------------------------------------------------------------------ notes and severity (S11.3 overview tab)

public sealed record UpdateNotesCommand(Guid ClaimId, string? Notes) : IdempotentCommand, ICommand<ClaimChangeResult>
{
    public const int MaxNotesLength = 4000;
}

internal sealed class UpdateNotesValidator : AbstractValidator<UpdateNotesCommand>
{
    public UpdateNotesValidator() =>
        RuleFor(c => c.Notes).MaximumLength(UpdateNotesCommand.MaxNotesLength).WithErrorCode("NOTES_TOO_LONG")
            .WithMessage($"Notes must be at most {UpdateNotesCommand.MaxNotesLength} characters.");
}

internal sealed class UpdateNotesHandler(IClaimRepository claims, ICurrentUser currentUser, TimeProvider clock)
    : IRequestHandler<UpdateNotesCommand, ClaimChangeResult>
{
    public async Task<ClaimChangeResult> Handle(UpdateNotesCommand request, CancellationToken cancellationToken)
    {
        var claim = await claims.GetRequiredAsync(request.ClaimId, cancellationToken);
        claim.UpdateNotes(request.Notes, currentUser.GetActor(), clock.GetUtcNow());
        return ClaimChangeResult.From(claim);
    }
}

public sealed record ChangeSeverityCommand(Guid ClaimId, ClaimSeverity Severity) : IdempotentCommand, ICommand<ClaimChangeResult>;

internal sealed class ChangeSeverityValidator : AbstractValidator<ChangeSeverityCommand>
{
    public ChangeSeverityValidator() =>
        RuleFor(c => c.Severity).IsInEnum().WithErrorCode("SEVERITY_INVALID").WithMessage("Severity is not supported.");
}

internal sealed class ChangeSeverityHandler(IClaimRepository claims, ICurrentUser currentUser, TimeProvider clock)
    : IRequestHandler<ChangeSeverityCommand, ClaimChangeResult>
{
    public async Task<ClaimChangeResult> Handle(ChangeSeverityCommand request, CancellationToken cancellationToken)
    {
        var claim = await claims.GetRequiredAsync(request.ClaimId, cancellationToken);
        claim.ChangeSeverity(request.Severity, currentUser.GetActor(), clock.GetUtcNow());
        return ClaimChangeResult.From(claim);
    }
}

// ------------------------------------------------------------------ validation issues (S5.4, D-05..D-07)

/// <summary>POST /api/claims/{id}/validate: re-runs the state rules and syncs the persisted issues.</summary>
public sealed record ValidateClaimCommand(Guid ClaimId) : IdempotentCommand, ICommand<ClaimChangeResult>;

internal sealed class ValidateClaimHandler(IClaimRepository claims, ClaimValidationContextBuilder validation, TimeProvider clock)
    : IRequestHandler<ValidateClaimCommand, ClaimChangeResult>
{
    public async Task<ClaimChangeResult> Handle(ValidateClaimCommand request, CancellationToken cancellationToken)
    {
        var claim = await claims.GetRequiredAsync(request.ClaimId, cancellationToken);
        await validation.RevalidateAsync(claim, clock.GetUtcNow(), cancellationToken);
        return ClaimChangeResult.From(claim);
    }
}

public sealed record AcknowledgeWarningsResult(Guid ClaimId, int Acknowledged, IReadOnlyList<ValidationIssueDto> ValidationIssues);

/// <summary>BR-C-02: acknowledges every open Warning.</summary>
public sealed record AcknowledgeWarningsCommand(Guid ClaimId) : IdempotentCommand, ICommand<AcknowledgeWarningsResult>;

internal sealed class AcknowledgeWarningsHandler(IClaimRepository claims, ICurrentUser currentUser, TimeProvider clock)
    : IRequestHandler<AcknowledgeWarningsCommand, AcknowledgeWarningsResult>
{
    public async Task<AcknowledgeWarningsResult> Handle(AcknowledgeWarningsCommand request, CancellationToken cancellationToken)
    {
        var claim = await claims.GetRequiredAsync(request.ClaimId, cancellationToken);
        var count = claim.AcknowledgeWarnings(currentUser.GetActor(), clock.GetUtcNow());
        return new AcknowledgeWarningsResult(claim.Id, count, claim.ValidationIssues.Select(ValidationIssueDto.From).ToList());
    }
}

/// <summary>
/// D-07: a Supervisor or Manager waives an open Critical issue with a mandatory reason
/// (POST /api/claims/{id}/validation-issues/{issueId}/waive). NO_CLAIMANT can't be waived.
/// </summary>
[RequiresRole(UserRole.Supervisor)]
public sealed record WaiveValidationIssueCommand(Guid ClaimId, Guid IssueId, string Reason) : IdempotentCommand, ICommand<ClaimChangeResult>;

internal sealed class WaiveValidationIssueValidator : AbstractValidator<WaiveValidationIssueCommand>
{
    public WaiveValidationIssueValidator() =>
        RuleFor(c => c.Reason)
            .NotEmpty().WithErrorCode("REASON_REQUIRED").WithMessage("A reason is required to waive a validation issue.")
            .MaximumLength(ClaimValidationIssue.MaxWaiverReasonLength).WithErrorCode("REASON_TOO_LONG")
            .WithMessage($"Reason must be at most {ClaimValidationIssue.MaxWaiverReasonLength} characters.");
}

internal sealed class WaiveValidationIssueHandler(IClaimRepository claims, ICurrentUser currentUser, TimeProvider clock)
    : IRequestHandler<WaiveValidationIssueCommand, ClaimChangeResult>
{
    public async Task<ClaimChangeResult> Handle(WaiveValidationIssueCommand request, CancellationToken cancellationToken)
    {
        var claim = await claims.GetRequiredAsync(request.ClaimId, cancellationToken);
        claim.WaiveValidationIssue(request.IssueId, request.Reason, currentUser.GetActor(), clock.GetUtcNow());
        return ClaimChangeResult.From(claim);
    }
}
