using ClaimsModule.Domain.Common;

namespace ClaimsModule.Domain.Claims.Validation;

/// <summary>Stable codes for state-level validation issues persisted on a claim (D-05, D-06).</summary>
public static class ValidationIssueCodes
{
    public const string NoClaimant = "NO_CLAIMANT";
    public const string NoPolicy = "NO_POLICY";
    public const string LossDateOutsidePolicyPeriod = "LOSS_DATE_OUTSIDE_POLICY_PERIOD";
    public const string NoRiskObjects = "NO_RISK_OBJECTS";
    public const string CauseOfLossInactive = "CAUSE_OF_LOSS_INACTIVE";

    /// <summary>
    /// D-07: NO_CLAIMANT can't be waived, because BR-C-03 and BR-ST-02(b) require a Claimant independently.
    /// </summary>
    public static bool IsWaivable(string code) => code != NoClaimant;
}

/// <summary>The result of evaluating one state rule against a claim.</summary>
public sealed record ValidationFinding(string Code, ValidationSeverity Severity, string Message, string? Field = null);

/// <summary>
/// A validation issue recorded against a claim (D-06). Open Criticals block Draft→Open and Closed
/// (CC-02). Open Warnings block Draft→Open until they are acknowledged (BR-C-02, D-07).
/// </summary>
public sealed class ClaimValidationIssue : Entity
{
    public const int MaxWaiverReasonLength = 500;

    private ClaimValidationIssue()
    {
    }

    internal ClaimValidationIssue(Guid claimId, ValidationFinding finding, DateTimeOffset now)
        : base(SequentialGuid.NewGuid())
    {
        ArgumentNullException.ThrowIfNull(finding);
        ClaimId = claimId;
        Code = finding.Code;
        Severity = finding.Severity;
        Message = finding.Message;
        Field = finding.Field;
        Status = ValidationIssueStatus.Open;
        RaisedAt = now.ToUniversalTime();
    }

    public Guid ClaimId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public ValidationSeverity Severity { get; private set; }

    public string Message { get; private set; } = string.Empty;

    public string? Field { get; private set; }

    public ValidationIssueStatus Status { get; private set; }

    public DateTimeOffset RaisedAt { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public Guid? AcknowledgedByUserId { get; private set; }

    public DateTimeOffset? AcknowledgedAt { get; private set; }

    public Guid? WaivedByUserId { get; private set; }

    public DateTimeOffset? WaivedAt { get; private set; }

    public string? WaiverReason { get; private set; }

    /// <summary>Still tracked: Open, Acknowledged or Waived. Resolved issues are history only.</summary>
    public bool IsTracked => Status != ValidationIssueStatus.Resolved;

    public bool IsOpenCritical => Status == ValidationIssueStatus.Open && Severity == ValidationSeverity.Critical;

    public bool IsOpenWarning => Status == ValidationIssueStatus.Open && Severity == ValidationSeverity.Warning;

    internal void Acknowledge(Actor actor, DateTimeOffset now)
    {
        Status = ValidationIssueStatus.Acknowledged;
        AcknowledgedByUserId = actor.UserId;
        AcknowledgedAt = now.ToUniversalTime();
    }

    internal void Waive(Actor actor, string reason, DateTimeOffset now)
    {
        Status = ValidationIssueStatus.Waived;
        WaivedByUserId = actor.UserId;
        WaivedAt = now.ToUniversalTime();
        WaiverReason = reason;
    }

    internal void Resolve(DateTimeOffset now)
    {
        Status = ValidationIssueStatus.Resolved;
        ResolvedAt = now.ToUniversalTime();
    }
}
