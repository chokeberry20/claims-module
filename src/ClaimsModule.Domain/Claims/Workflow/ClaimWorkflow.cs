using ClaimsModule.Domain.Common;

namespace ClaimsModule.Domain.Claims.Workflow;

/// <summary>
/// One allowed status transition (brief §3.2 ClaimStatusTransitions: FromStatus, ToStatus,
/// RequiredPermission). The persistence layer seeds the ClaimStatusTransitions table from
/// <see cref="ClaimWorkflow.Transitions"/>, so there is a single source of truth (D-08).
/// </summary>
public sealed record ClaimStatusTransitionRule(
    ClaimStatus From,
    ClaimStatus To,
    UserRole RequiredRole,
    bool RequiresReason,
    string Description);

/// <summary>The claim lifecycle state machine from S4.2. Any transition not listed here is invalid (BR-ST-01).</summary>
public static class ClaimWorkflow
{
    public static IReadOnlyList<ClaimStatusTransitionRule> Transitions { get; } =
    [
        new(ClaimStatus.Draft, ClaimStatus.Open, UserRole.Handler, false,
            "No unresolved or unwaived Critical issues, all Warnings acknowledged, at least one Claimant, handler assigned"),
        new(ClaimStatus.Open, ClaimStatus.UnderInvestigation, UserRole.Handler, false, "No additional conditions"),
        new(ClaimStatus.Open, ClaimStatus.PendingPayment, UserRole.Handler, false, "At least one approved reserve transaction"),
        new(ClaimStatus.Open, ClaimStatus.Closed, UserRole.Handler, false, "All closure conditions satisfied (CC-01..CC-04)"),
        new(ClaimStatus.Open, ClaimStatus.Withdrawn, UserRole.Handler, true, "Withdrawal reason provided"),
        new(ClaimStatus.UnderInvestigation, ClaimStatus.Open, UserRole.Handler, false, "Handler manually reverts"),
        new(ClaimStatus.UnderInvestigation, ClaimStatus.PendingPayment, UserRole.Handler, false, "At least one approved reserve transaction"),
        new(ClaimStatus.UnderInvestigation, ClaimStatus.Closed, UserRole.Handler, false, "All closure conditions satisfied (CC-01..CC-04)"),
        new(ClaimStatus.UnderInvestigation, ClaimStatus.Withdrawn, UserRole.Handler, true, "Withdrawal reason provided"),
        new(ClaimStatus.PendingPayment, ClaimStatus.Closed, UserRole.Handler, false, "All closure conditions satisfied (CC-01..CC-04)"),
        new(ClaimStatus.Closed, ClaimStatus.Reopened, UserRole.Supervisor, true, "Reopen reason provided; Supervisor role required"),
        new(ClaimStatus.Reopened, ClaimStatus.Open, UserRole.Supervisor, false, "Immediately on reopen"),
    ];

    public static ClaimStatusTransitionRule? Find(ClaimStatus from, ClaimStatus to) =>
        Transitions.FirstOrDefault(t => t.From == from && t.To == to);

    public static IReadOnlyList<ClaimStatus> ValidNextStatuses(ClaimStatus from) =>
        Transitions.Where(t => t.From == from).Select(t => t.To).ToList();
}

/// <summary>Optional inputs for a status transition (PUT /api/claims/{id}/status).</summary>
public sealed record TransitionRequest(
    string? Reason = null,
    string? ClosureJustification = null,
    bool AcknowledgeWarnings = false)
{
    public static TransitionRequest None { get; } = new();
}

/// <summary>
/// Facts from the reserve aggregates that the claim's transition rules need (S4.2, S4.3).
/// The application layer computes them from the claim's reserve components.
/// </summary>
public sealed record ReserveFacts(bool HasApprovedTransaction, bool HasPendingApproval, bool HasOpenReserveBalance)
{
    public static ReserveFacts None { get; } = new(false, false, false);
}

/// <summary>
/// The result of checking one target status. It backs the pre-flight checklist (D-27, S11.3).
/// <see cref="BlockingConditions"/> must be fixed in the data. <see cref="ConfirmationsRequired"/>
/// are cleared by the request itself (a reason, acknowledging warnings, or a CC-04 justification).
/// </summary>
public sealed record TransitionEvaluation(
    ClaimStatus From,
    ClaimStatus Target,
    bool IsDefinedTransition,
    UserRole RequiredRole,
    bool HasRequiredRole,
    bool RequiresReason,
    IReadOnlyList<RuleViolation> BlockingConditions,
    IReadOnlyList<RuleViolation> ConfirmationsRequired)
{
    /// <summary>The transition can go ahead once any outstanding confirmations are supplied.</summary>
    public bool IsAllowed => IsDefinedTransition && HasRequiredRole && BlockingConditions.Count == 0;

    /// <summary>The transition can go ahead as requested, right now.</summary>
    public bool CanProceed => IsAllowed && ConfirmationsRequired.Count == 0;
}
