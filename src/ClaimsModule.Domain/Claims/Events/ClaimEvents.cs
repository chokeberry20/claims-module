using ClaimsModule.Domain.Claims.Validation;
using ClaimsModule.Domain.Common;

namespace ClaimsModule.Domain.Claims.Events;

// Domain events raised by the Claim aggregate. The Application layer maps them to ClaimAuditLog
// entries (S14.1). ActorUserId is null only for system-initiated events (jobs).

public sealed record ClaimCreated(Guid ClaimId, string ClaimNumber, Guid? PolicyId, Guid ActorUserId, DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt);

public sealed record ClaimLossDetailsUpdated(Guid ClaimId, Guid ActorUserId, DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt);

public sealed record ClaimPolicyLinked(
    Guid ClaimId,
    Guid? PreviousPolicyId,
    Guid PolicyId,
    string PolicyNumber,
    Guid ActorUserId,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

public sealed record ClaimHandlerAssigned(
    Guid ClaimId,
    Guid? PreviousHandlerId,
    Guid HandlerId,
    string HandlerName,
    Guid ActorUserId,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

public sealed record ClaimNotesUpdated(Guid ClaimId, Guid ActorUserId, DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt);

public sealed record ClaimSeverityChanged(
    Guid ClaimId,
    ClaimSeverity PreviousSeverity,
    ClaimSeverity Severity,
    Guid ActorUserId,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

public sealed record ClaimPartyAdded(
    Guid ClaimId,
    Guid PartyId,
    PartyRole Role,
    string DisplayName,
    Guid ActorUserId,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

public sealed record ClaimPartyRemoved(
    Guid ClaimId,
    Guid PartyId,
    PartyRole Role,
    string DisplayName,
    Guid ActorUserId,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

public sealed record ClaimRiskObjectAdded(
    Guid ClaimId,
    Guid RiskObjectId,
    AssetType AssetType,
    string AssetDescription,
    Guid ActorUserId,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

public sealed record ClaimRiskObjectRemoved(Guid ClaimId, Guid RiskObjectId, Guid ActorUserId, DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt);

/// <summary>Maps to VALIDATION_ISSUE_ADDED (S14.1).</summary>
public sealed record ValidationIssueRaised(
    Guid ClaimId,
    Guid IssueId,
    string Code,
    ValidationSeverity Severity,
    string Message,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

public sealed record ValidationWarningsAcknowledged(
    Guid ClaimId,
    IReadOnlyList<Guid> IssueIds,
    Guid ActorUserId,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

/// <summary>Maps to VALIDATION_ISSUE_WAIVED (D-07).</summary>
public sealed record ValidationIssueWaived(
    Guid ClaimId,
    Guid IssueId,
    string Code,
    string Reason,
    Guid ActorUserId,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

public sealed record ClaimStatusChanged(
    Guid ClaimId,
    ClaimStatus FromStatus,
    ClaimStatus ToStatus,
    string? Reason,
    Guid ActorUserId,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

public sealed record ClaimClosed(
    Guid ClaimId,
    string? ClosureReason,
    bool ClosedWithOpenReserves,
    Guid ActorUserId,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

public sealed record ClaimReopened(Guid ClaimId, string Reason, Guid ActorUserId, DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt);

public sealed record ClaimWithdrawn(Guid ClaimId, string Reason, Guid ActorUserId, DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt);

public sealed record ReserveLimitOverrideChanged(Guid ClaimId, bool Enabled, Guid ActorUserId, DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt);

/// <summary>Maps to SLA_BREACH_DETECTED (S12.2). Raised by the system; there is no user actor.</summary>
public sealed record ClaimSlaBreachDetected(Guid ClaimId, DateTimeOffset LastActivityAt, DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt);
