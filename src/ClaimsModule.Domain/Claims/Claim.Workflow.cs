using ClaimsModule.Domain.Claims.Events;
using ClaimsModule.Domain.Claims.Validation;
using ClaimsModule.Domain.Claims.Workflow;
using ClaimsModule.Domain.Common;

namespace ClaimsModule.Domain.Claims;

/// <summary>The claim lifecycle: S4.2 transitions, S4.3 closure conditions, BR-ST-01..04.</summary>
public sealed partial class Claim
{
    private const string TransitionField = "StatusTransition";

    /// <summary>Checks every valid next status for the pre-flight menu (D-27).</summary>
    public IReadOnlyList<TransitionEvaluation> GetAvailableTransitions(ReserveFacts facts, Actor actor) =>
        ClaimWorkflow.ValidNextStatuses(Status)
            .Select(target => EvaluateTransition(target, facts, actor))
            .ToList();

    /// <summary>
    /// Checks a transition without applying it. Pass <paramref name="request"/> to count the
    /// confirmations it already supplies.
    /// </summary>
    public TransitionEvaluation EvaluateTransition(
        ClaimStatus target,
        ReserveFacts facts,
        Actor actor,
        TransitionRequest? request = null)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(actor);

        var rule = ClaimWorkflow.Find(Status, target);
        if (rule is null)
        {
            return new TransitionEvaluation(Status, target, false, UserRole.Handler, true, false, [InvalidTransition(target)], []);
        }

        var blocking = new List<RuleViolation>();
        var confirmations = new List<RuleViolation>();

        switch (target)
        {
            case ClaimStatus.Open when Status == ClaimStatus.Draft:
                AddOpenConditions(blocking, confirmations, request);
                break;
            case ClaimStatus.PendingPayment:
                AddPendingPaymentConditions(blocking, facts);
                break;
            case ClaimStatus.Closed:
                AddClosureConditions(blocking, confirmations, facts, request);
                break;
        }

        if (rule.RequiresReason)
        {
            AddReasonCondition(blocking, confirmations, request?.Reason, "Reason");
        }
        else if (request?.Reason is { } optionalReason && optionalReason.Trim().Length > MaxReasonLength)
        {
            blocking.Add(ReasonTooLong("Reason"));
        }

        return new TransitionEvaluation(
            Status,
            target,
            true,
            rule.RequiredRole,
            actor.HasAtLeast(rule.RequiredRole),
            rule.RequiresReason,
            blocking,
            confirmations);
    }

    /// <summary>
    /// Applies a status transition (PUT /api/claims/{id}/status).
    /// An invalid transition gives a 422 that lists the valid next statuses (BR-ST-01).
    /// A missing role gives a 403 (D-09, D-24). Unmet conditions give a 422 with all blocking
    /// conditions (BR-ST-02, BR-ST-03). Reopen moves straight on to Open (BR-ST-04, D-12).
    /// </summary>
    public void TransitionTo(ClaimStatus target, TransitionRequest request, ReserveFacts facts, Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        var evaluation = EvaluateTransition(target, facts, actor, request);

        if (!evaluation.IsDefinedTransition)
        {
            throw new DomainRuleViolationException(evaluation.BlockingConditions);
        }

        if (!evaluation.HasRequiredRole)
        {
            throw new DomainForbiddenException(
                "TRANSITION_ROLE",
                $"Transition from {Status} to {target} requires the {evaluation.RequiredRole} role.");
        }

        var unmet = evaluation.BlockingConditions.Concat(evaluation.ConfirmationsRequired).ToList();
        if (unmet.Count > 0)
        {
            throw new DomainRuleViolationException(unmet);
        }

        var reason = CleanText(request.Reason);
        switch (target)
        {
            case ClaimStatus.Open when Status == ClaimStatus.Draft:
                if (request.AcknowledgeWarnings)
                {
                    AcknowledgeWarnings(actor, now);
                }

                ChangeStatus(ClaimStatus.Open, reason, actor, now);
                break;

            case ClaimStatus.Closed:
                var justification = CleanText(request.ClosureJustification);
                ClosedAt = now.ToUniversalTime();
                ClosureReason = reason ?? justification;
                ChangeStatus(ClaimStatus.Closed, reason, actor, now);
                Raise(new ClaimClosed(Id, ClosureReason, facts.HasOpenReserveBalance, justification, actor.UserId, now));
                break;

            case ClaimStatus.Withdrawn:
                ClosureReason = reason;
                ChangeStatus(ClaimStatus.Withdrawn, reason, actor, now);
                Raise(new ClaimWithdrawn(Id, reason!, actor.UserId, now));
                break;

            case ClaimStatus.Reopened:
                ChangeStatus(ClaimStatus.Reopened, reason, actor, now);
                Raise(new ClaimReopened(Id, reason!, actor.UserId, now));
                ClosedAt = null;
                ClosureReason = null;
                ReopenedAt = now.ToUniversalTime();
                ReopenCount++;
                ChangeStatus(ClaimStatus.Open, null, actor, now);
                break;

            default:
                ChangeStatus(target, reason, actor, now);
                break;
        }
    }

    private void ChangeStatus(ClaimStatus target, string? reason, Actor actor, DateTimeOffset now)
    {
        var from = Status;
        Status = target;
        Touch();
        Raise(new ClaimStatusChanged(Id, from, target, reason, actor.UserId, now));
    }

    /// <summary>BR-ST-02, BR-C-02, D-07, D-10.</summary>
    private void AddOpenConditions(List<RuleViolation> blocking, List<RuleViolation> confirmations, TransitionRequest? request)
    {
        if (!HasActiveClaimant)
        {
            blocking.Add(new(ValidationIssueCodes.NoClaimant, "At least one Claimant party is required to open a claim.", "ClaimParties"));
        }

        var openCriticals = _validationIssues
            .Where(i => i.IsOpenCritical && i.Code != ValidationIssueCodes.NoClaimant)
            .Select(i => i.Code)
            .ToList();
        if (openCriticals.Count > 0)
        {
            blocking.Add(new(
                "OPEN_CRITICAL_ISSUES",
                $"The claim has unresolved Critical validation issues: {string.Join(", ", openCriticals)}.",
                "ValidationIssues"));
        }

        if (AssignedHandlerId is null)
        {
            blocking.Add(new("NO_HANDLER", "A handler must be assigned before the claim can be opened.", "AssignedHandlerId"));
        }

        var openWarnings = _validationIssues.Where(i => i.IsOpenWarning).Select(i => i.Message).ToList();
        if (openWarnings.Count > 0 && request?.AcknowledgeWarnings != true)
        {
            confirmations.Add(new(
                "WARNINGS_NOT_ACKNOWLEDGED",
                $"Validation warnings must be acknowledged before the claim can be opened: {string.Join(" ", openWarnings)}",
                "AcknowledgeWarnings"));
        }
    }

    /// <summary>S4.2 (D-11): at least one approved reserve transaction is needed.</summary>
    private static void AddPendingPaymentConditions(List<RuleViolation> blocking, ReserveFacts facts)
    {
        if (!facts.HasApprovedTransaction)
        {
            blocking.Add(new(
                "NO_APPROVED_RESERVE",
                "At least one approved reserve is required before the claim can move to PendingPayment.",
                "Reserves"));
        }
    }

    /// <summary>S4.3 closure conditions CC-01..CC-04 (BR-ST-03). Messages follow S8.</summary>
    private void AddClosureConditions(
        List<RuleViolation> blocking,
        List<RuleViolation> confirmations,
        ReserveFacts facts,
        TransitionRequest? request)
    {
        if (facts.HasPendingApproval)
        {
            blocking.Add(new(
                "CC-01",
                "Claim cannot be closed — CC-01 (no reserve transactions pending approval) is not satisfied.",
                "Reserves"));
        }

        if (HasOpenCriticalIssues)
        {
            blocking.Add(new(
                "CC-02",
                "Claim cannot be closed — CC-02 (no unresolved Critical validation issues) is not satisfied.",
                "ValidationIssues"));
        }

        if (!HasActiveClaimant)
        {
            blocking.Add(new(
                "CC-03",
                "Claim cannot be closed — CC-03 (at least one Claimant party) is not satisfied.",
                "ClaimParties"));
        }

        if (facts.HasOpenReserveBalance)
        {
            AddReasonCondition(blocking, confirmations, request?.ClosureJustification, "ClosureJustification",
                new RuleViolation(
                    "CC-04",
                    "Open reserve balances remain. Confirm closure by providing a justification note.",
                    "ClosureJustification"));
        }
    }

    private static void AddReasonCondition(
        List<RuleViolation> blocking,
        List<RuleViolation> confirmations,
        string? value,
        string field,
        RuleViolation? whenMissing = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            confirmations.Add(whenMissing ?? new("REASON_REQUIRED", "A reason is required for this transition.", field));
        }
        else if (value.Trim().Length > MaxReasonLength)
        {
            blocking.Add(ReasonTooLong(field));
        }
    }

    private static RuleViolation ReasonTooLong(string field) =>
        new("REASON_TOO_LONG", $"Reason must be at most {MaxReasonLength} characters.", field);

    private RuleViolation InvalidTransition(ClaimStatus target)
    {
        var valid = ClaimWorkflow.ValidNextStatuses(Status);
        var validText = valid.Count == 0 ? "none" : string.Join(", ", valid);
        return new RuleViolation(
            "INVALID_TRANSITION",
            $"Transition from {Status} to {target} is not permitted. Valid next statuses: {validText}.",
            TransitionField);
    }
}
