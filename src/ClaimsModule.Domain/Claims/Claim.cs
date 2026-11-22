using ClaimsModule.Domain.Claims.Events;
using ClaimsModule.Domain.Claims.Validation;
using ClaimsModule.Domain.Common;

namespace ClaimsModule.Domain.Claims;

/// <summary>
/// The Claim aggregate root (S9.1). It owns the loss event, parties, risk objects and validation
/// issues. Reserve components form their own aggregate and reference the claim by id. The
/// cross-aggregate rules (closure, PendingPayment, the aggregate limit) get reserve facts passed in
/// by the application layer. The lifecycle state machine is in Claim.Workflow.cs.
/// </summary>
public sealed partial class Claim : AggregateRoot
{
    public const int MaxReasonLength = 500;
    public const int MaxClaimTypeLength = 50;
    public const int MaxHandlerNameLength = 255;

    /// <summary>S12.2: a Draft or Open claim with no update for 48h is in breach.</summary>
    public static readonly TimeSpan SlaInactivityThreshold = TimeSpan.FromHours(48);

    /// <summary>S12.2: at most one breach entry per claim every 24h.</summary>
    public static readonly TimeSpan SlaBreachRepeatInterval = TimeSpan.FromHours(24);

    private readonly List<ClaimParty> _parties = [];
    private readonly List<ClaimRiskObject> _riskObjects = [];
    private readonly List<ClaimValidationIssue> _validationIssues = [];

    private Claim()
    {
    }

    private Claim(Guid id)
        : base(id)
    {
    }

    public ClaimNumber ClaimNumber { get; private set; } = null!;

    public Guid? PolicyId { get; private set; }

    public string? PolicyNumber { get; private set; }

    public string? ClientName { get; private set; }

    public ClaimStatus Status { get; private set; }

    public ClaimSeverity Severity { get; private set; }

    /// <summary>Derived from the cause of loss peril category (D-33).</summary>
    public string ClaimType { get; private set; } = string.Empty;

    public DateTimeOffset ReportedDate { get; private set; }

    public Guid? AssignedHandlerId { get; private set; }

    public string? AssignedHandlerName { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public string? ClosureReason { get; private set; }

    public string? Notes { get; private set; }

    /// <summary>BR-R-05 / D-19: set by a Manager to allow approved reserves above the $10M aggregate.</summary>
    public bool ReserveLimitOverride { get; private set; }

    public Guid? ReserveLimitOverrideSetByUserId { get; private set; }

    public DateTimeOffset? ReserveLimitOverrideSetAt { get; private set; }

    /// <summary>D-12: the Reopened status is transient, so these record that a reopen happened.</summary>
    public DateTimeOffset? ReopenedAt { get; private set; }

    public int ReopenCount { get; private set; }

    /// <summary>D-13: SLA breach is a flag, not a status. It clears on the next business update.</summary>
    public bool IsSlaBreached { get; private set; }

    public DateTimeOffset? LastSlaBreachAt { get; private set; }

    public LossEvent LossEvent { get; private set; } = null!;

    public IReadOnlyCollection<ClaimParty> Parties => _parties.AsReadOnly();

    public IReadOnlyCollection<ClaimRiskObject> RiskObjects => _riskObjects.AsReadOnly();

    public IReadOnlyCollection<ClaimValidationIssue> ValidationIssues => _validationIssues.AsReadOnly();

    public IEnumerable<ClaimParty> ActiveClaimants => _parties.Where(p => p.IsActiveClaimant);

    public bool HasActiveClaimant => _parties.Any(p => p.IsActiveClaimant);

    public bool HasOpenCriticalIssues => _validationIssues.Any(i => i.IsOpenCritical);

    public bool IsTerminalForEditing => Status is ClaimStatus.Closed or ClaimStatus.Withdrawn;

    /// <summary>
    /// FNOL intake (S5.2, FR-01). The claim starts in Draft, and the creator becomes the assigned
    /// handler (D-10). Request-level rules are enforced again here (D-05). Parties and risk objects
    /// are added afterwards in the same transaction.
    /// </summary>
    public static Claim Create(
        ClaimNumber claimNumber,
        LossEventDetails lossDetails,
        string claimType,
        PolicySnapshot? policy,
        ClaimSeverity severity,
        string? notes,
        Actor createdBy,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(claimNumber);
        ArgumentNullException.ThrowIfNull(createdBy);
        EnsureValidSeverity(severity);

        var claim = new Claim(SequentialGuid.NewGuid())
        {
            ClaimNumber = claimNumber,
            Status = ClaimStatus.Draft,
            Severity = severity,
            ClaimType = RequireClaimType(claimType),
            ReportedDate = now.ToUniversalTime(),
            AssignedHandlerId = createdBy.UserId,
            AssignedHandlerName = createdBy.DisplayName,
            Notes = CleanText(notes),
        };

        claim.LossEvent = LossEvent.Create(claim.Id, lossDetails, now);
        if (policy is not null)
        {
            claim.ApplyPolicy(policy);
        }

        claim.Raise(new ClaimCreated(claim.Id, claimNumber.Value, policy?.PolicyId, createdBy.UserId, now));
        return claim;
    }

    /// <summary>D-31: loss details can be corrected only while the claim is in Draft.</summary>
    public void UpdateLossDetails(LossEventDetails lossDetails, string claimType, Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (Status != ClaimStatus.Draft)
        {
            throw new DomainRuleViolationException(
                "CLAIM_NOT_DRAFT",
                "Loss details can only be changed while the claim is in Draft.",
                "Status");
        }

        LossEvent.Update(lossDetails, now);
        ClaimType = RequireClaimType(claimType);
        Touch();
        Raise(new ClaimLossDetailsUpdated(Id, actor.UserId, now));
    }

    /// <summary>Links (or re-links) the simulated policy. This clears the BR-C-06 block on reserves.</summary>
    public void LinkPolicy(PolicySnapshot policy, Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(actor);
        EnsureEditable();

        if (PolicyId == policy.PolicyId)
        {
            return;
        }

        var previous = PolicyId;
        ApplyPolicy(policy);
        Touch();
        Raise(new ClaimPolicyLinked(Id, previous, policy.PolicyId, policy.PolicyNumber, actor.UserId, now));
    }

    /// <summary>D-10: Supervisors and Managers may (re)assign the handler.</summary>
    public void AssignHandler(Guid handlerId, string handlerName, Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!actor.HasAtLeast(UserRole.Supervisor))
        {
            throw new DomainForbiddenException(
                "CLAIM_ASSIGN_ROLE",
                "Only a Supervisor or Manager can assign the claim handler.");
        }

        EnsureEditable();
        if (handlerId == Guid.Empty)
        {
            throw new DomainRuleViolationException("HANDLER_REQUIRED", "A handler must be specified.", "AssignedHandlerId");
        }

        if (string.IsNullOrWhiteSpace(handlerName) || handlerName.Trim().Length > MaxHandlerNameLength)
        {
            throw new DomainRuleViolationException("HANDLER_NAME_INVALID", "Handler name is required.", "AssignedHandlerName");
        }

        if (AssignedHandlerId == handlerId)
        {
            return;
        }

        var previous = AssignedHandlerId;
        AssignedHandlerId = handlerId;
        AssignedHandlerName = handlerName.Trim();
        Touch();
        Raise(new ClaimHandlerAssigned(Id, previous, handlerId, AssignedHandlerName, actor.UserId, now));
    }

    public void UpdateNotes(string? notes, Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);
        EnsureEditable();
        Notes = CleanText(notes);
        Touch();
        Raise(new ClaimNotesUpdated(Id, actor.UserId, now));
    }

    public void ChangeSeverity(ClaimSeverity severity, Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);
        EnsureValidSeverity(severity);
        EnsureEditable();
        if (Severity == severity)
        {
            return;
        }

        var previous = Severity;
        Severity = severity;
        Touch();
        Raise(new ClaimSeverityChanged(Id, previous, severity, actor.UserId, now));
    }

    // ----------------------------------------------------------------- parties (BR-P-01/02)

    public ClaimParty AddParty(PartyDetails details, Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);
        EnsureEditable();

        var party = ClaimParty.Create(Id, details);
        _parties.Add(party);
        Touch();
        Raise(new ClaimPartyAdded(Id, party.Id, party.Role, party.DisplayName, actor.UserId, now));
        return party;
    }

    /// <summary>Soft-removes a party (S10.1). The last active Claimant can't be removed (FR-30).</summary>
    public void RemoveParty(Guid partyId, Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);
        EnsureEditable();

        var party = _parties.FirstOrDefault(p => p.Id == partyId)
            ?? throw new DomainNotFoundException(nameof(ClaimParty), partyId);

        if (!party.IsActive)
        {
            throw new DomainRuleViolationException("PARTY_ALREADY_REMOVED", "The party has already been removed from the claim.", "ClaimParties");
        }

        if (party.IsActiveClaimant && ActiveClaimants.Count() == 1)
        {
            throw new DomainRuleViolationException(
                "PARTY_LAST_CLAIMANT",
                "The last Claimant party cannot be removed. At least one Claimant party is required.",
                "ClaimParties");
        }

        party.Deactivate(now);
        Touch();
        Raise(new ClaimPartyRemoved(Id, party.Id, party.Role, party.DisplayName, actor.UserId, now));
    }

    // ----------------------------------------------------------------- risk objects (S9.4)

    /// <summary>The first risk object becomes primary. Adding one marked primary demotes the current primary.</summary>
    public ClaimRiskObject AddRiskObject(RiskObjectDetails details, Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);
        EnsureEditable();

        var riskObject = ClaimRiskObject.Create(Id, details);
        if (riskObject.IsPrimary)
        {
            _riskObjects.ForEach(r => r.SetPrimary(false));
        }
        else if (_riskObjects.Count == 0)
        {
            riskObject.SetPrimary(true);
        }

        _riskObjects.Add(riskObject);
        Touch();
        Raise(new ClaimRiskObjectAdded(Id, riskObject.Id, riskObject.AssetType, riskObject.AssetDescription, actor.UserId, now));
        return riskObject;
    }

    public void RemoveRiskObject(Guid riskObjectId, Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);
        EnsureEditable();

        var riskObject = _riskObjects.FirstOrDefault(r => r.Id == riskObjectId)
            ?? throw new DomainNotFoundException(nameof(ClaimRiskObject), riskObjectId);

        _riskObjects.Remove(riskObject);
        if (riskObject.IsPrimary && _riskObjects.Count > 0)
        {
            _riskObjects[0].SetPrimary(true);
        }

        Touch();
        Raise(new ClaimRiskObjectRemoved(Id, riskObjectId, actor.UserId, now));
    }

    // ----------------------------------------------------------------- validation issues (D-05..D-07)

    /// <summary>
    /// Syncs the persisted issues with the latest evaluation:
    /// <list type="bullet">
    /// <item>a new condition raises an Open issue (VALIDATION_ISSUE_ADDED);</item>
    /// <item>an issue whose condition no longer holds becomes Resolved;</item>
    /// <item>Acknowledged and Waived issues are never raised again while the condition still holds (D-07).</item>
    /// </list>
    /// This is system re-evaluation, so it doesn't count as a business update for SLA purposes.
    /// </summary>
    public void ApplyValidationFindings(IReadOnlyCollection<ValidationFinding> findings, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(findings);
        var current = findings.DistinctBy(f => f.Code).ToList();
        var currentCodes = current.Select(f => f.Code).ToHashSet(StringComparer.Ordinal);

        foreach (var issue in _validationIssues.Where(i => i.IsTracked && !currentCodes.Contains(i.Code)))
        {
            issue.Resolve(now);
        }

        foreach (var finding in current.Where(f => !_validationIssues.Any(i => i.IsTracked && i.Code == f.Code)))
        {
            var issue = new ClaimValidationIssue(Id, finding, now);
            _validationIssues.Add(issue);
            Raise(new ValidationIssueRaised(Id, issue.Id, issue.Code, issue.Severity, issue.Message, now));
        }
    }

    /// <summary>Runs <see cref="ClaimStateValidator"/> and syncs the persisted issues (S5.4 validate endpoint).</summary>
    public IReadOnlyList<ValidationFinding> Revalidate(ClaimValidationContext context, DateTimeOffset now)
    {
        var findings = ClaimStateValidator.Evaluate(this, context);
        ApplyValidationFindings(findings, now);
        return findings;
    }

    /// <summary>BR-C-02: acknowledges every open Warning. Returns how many were acknowledged.</summary>
    public int AcknowledgeWarnings(Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);
        EnsureEditable();

        var warnings = _validationIssues.Where(i => i.IsOpenWarning).ToList();
        if (warnings.Count == 0)
        {
            return 0;
        }

        warnings.ForEach(w => w.Acknowledge(actor, now));
        Touch();
        Raise(new ValidationWarningsAcknowledged(Id, warnings.Select(w => w.Id).ToList(), actor.UserId, now));
        return warnings.Count;
    }

    /// <summary>
    /// D-07: a Supervisor or Manager waives an open Critical issue with a mandatory reason
    /// (S4.2 "or all waived"). NO_CLAIMANT can't be waived.
    /// </summary>
    public void WaiveValidationIssue(Guid issueId, string reason, Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!actor.HasAtLeast(UserRole.Supervisor))
        {
            throw new DomainForbiddenException(
                "VALIDATION_WAIVE_ROLE",
                "Only a Supervisor or Manager can waive a Critical validation issue.");
        }

        EnsureEditable();
        var issue = _validationIssues.FirstOrDefault(i => i.Id == issueId)
            ?? throw new DomainNotFoundException(nameof(ClaimValidationIssue), issueId);

        if (!ValidationIssueCodes.IsWaivable(issue.Code))
        {
            throw new DomainRuleViolationException("VALIDATION_ISSUE_NOT_WAIVABLE", "This issue cannot be waived.", "ValidationIssue");
        }

        if (issue.Severity != ValidationSeverity.Critical)
        {
            throw new DomainRuleViolationException(
                "VALIDATION_WAIVE_NOT_CRITICAL",
                "Only Critical validation issues can be waived; Warnings are acknowledged.",
                "ValidationIssue");
        }

        if (issue.Status != ValidationIssueStatus.Open)
        {
            throw new DomainRuleViolationException("VALIDATION_WAIVE_NOT_OPEN", "Only open validation issues can be waived.", "ValidationIssue");
        }

        var cleanedReason = RequireReason(reason);
        issue.Waive(actor, cleanedReason, now);
        Touch();
        Raise(new ValidationIssueWaived(Id, issue.Id, issue.Code, cleanedReason, actor.UserId, now));
    }

    // ----------------------------------------------------------------- reserves (BR-C-06, D-19, D-20)

    /// <summary>
    /// D-20: reserves can be created or changed in Draft, Open, UnderInvestigation and PendingPayment,
    /// and only once a policy is linked (BR-C-06).
    /// </summary>
    public void EnsureReserveChangesAllowed()
    {
        if (Status is not (ClaimStatus.Draft or ClaimStatus.Open or ClaimStatus.UnderInvestigation or ClaimStatus.PendingPayment))
        {
            throw new DomainRuleViolationException(
                "RESERVES_NOT_ALLOWED_IN_STATUS",
                $"Reserves cannot be changed while the claim is {Status}.",
                "Status");
        }

        if (PolicyId is null)
        {
            throw new DomainRuleViolationException(
                "RESERVES_REQUIRE_POLICY",
                "No policy linked. Policy must be associated before reserves can be set.",
                "PolicyId");
        }
    }

    /// <summary>
    /// Marks reserve activity on the claim. The application calls this for every reserve command, so
    /// the claim's row version changes and concurrent claim and reserve operations conflict
    /// (cross-aggregate concurrency, PLAN §3).
    /// </summary>
    public void RecordReserveActivity() => Touch();

    /// <summary>BR-R-05 / D-19: only a Manager may set or clear the override flag.</summary>
    public void SetReserveLimitOverride(bool enabled, Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!actor.HasAtLeast(UserRole.Manager))
        {
            throw new DomainForbiddenException(
                "RESERVE_OVERRIDE_ROLE",
                "Only a Manager can set the reserve limit override.");
        }

        EnsureEditable();
        if (ReserveLimitOverride == enabled)
        {
            return;
        }

        ReserveLimitOverride = enabled;
        ReserveLimitOverrideSetByUserId = actor.UserId;
        ReserveLimitOverrideSetAt = now.ToUniversalTime();
        Touch();
        Raise(new ReserveLimitOverrideChanged(Id, enabled, actor.UserId, now));
    }

    // ----------------------------------------------------------------- SLA (S12.2, D-13)

    /// <summary>
    /// Records an SLA breach when it is due: the status is Draft or Open, there has been no activity
    /// for 48h, and no breach was recorded in the last 24h. <paramref name="lastActivityAt"/> is the
    /// persisted UpdatedAt (or CreatedAt). The status is never changed. Returns true if a breach was recorded.
    /// </summary>
    public bool TryRecordSlaBreach(DateTimeOffset lastActivityAt, DateTimeOffset now)
    {
        if (Status is not (ClaimStatus.Draft or ClaimStatus.Open))
        {
            return false;
        }

        if (lastActivityAt >= now - SlaInactivityThreshold)
        {
            return false;
        }

        if (LastSlaBreachAt is { } last && last > now - SlaBreachRepeatInterval)
        {
            return false;
        }

        IsSlaBreached = true;
        LastSlaBreachAt = now.ToUniversalTime();
        Raise(new ClaimSlaBreachDetected(Id, lastActivityAt, now));
        return true;
    }

    // ----------------------------------------------------------------- helpers

    private void EnsureEditable()
    {
        if (IsTerminalForEditing)
        {
            throw new DomainRuleViolationException(
                "CLAIM_NOT_EDITABLE",
                $"The claim is {Status} and can no longer be changed.",
                "Status");
        }
    }

    /// <summary>Every business mutation clears the SLA breach flag (D-13).</summary>
    private void Touch() => IsSlaBreached = false;

    private void ApplyPolicy(PolicySnapshot policy)
    {
        PolicyId = policy.PolicyId;
        PolicyNumber = policy.PolicyNumber;
        ClientName = policy.ClientName;
    }

    private static string RequireClaimType(string claimType)
    {
        if (string.IsNullOrWhiteSpace(claimType) || claimType.Trim().Length > MaxClaimTypeLength)
        {
            throw new DomainRuleViolationException("CLAIM_TYPE_INVALID", "Claim type is required.", "ClaimType");
        }

        return claimType.Trim();
    }

    private static void EnsureValidSeverity(ClaimSeverity severity)
    {
        if (!Enum.IsDefined(severity))
        {
            throw new DomainRuleViolationException("SEVERITY_INVALID", "Severity is not supported.", "Severity");
        }
    }

    private static string RequireReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainRuleViolationException("REASON_REQUIRED", "A reason is required.", "Reason");
        }

        var trimmed = reason.Trim();
        if (trimmed.Length > MaxReasonLength)
        {
            throw new DomainRuleViolationException("REASON_TOO_LONG", $"Reason must be at most {MaxReasonLength} characters.", "Reason");
        }

        return trimmed;
    }

    private static string? CleanText(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
