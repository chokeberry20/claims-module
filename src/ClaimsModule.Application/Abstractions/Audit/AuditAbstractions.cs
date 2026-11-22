namespace ClaimsModule.Application.Abstractions.Audit;

/// <summary>ClaimAuditLog event types (S14.1, plus the additions D-07, D-28 and the ai-log assumptions).</summary>
public static class AuditEventTypes
{
    public const string ClaimCreated = "CLAIM_CREATED";
    public const string StatusChanged = "STATUS_CHANGED";
    public const string PartyAdded = "PARTY_ADDED";
    public const string PartyRemoved = "PARTY_REMOVED";
    public const string ReserveCreated = "RESERVE_CREATED";
    public const string ReserveAutoApproved = "RESERVE_AUTO_APPROVED";
    public const string ReserveApproved = "RESERVE_APPROVED";
    public const string ReserveRejected = "RESERVE_REJECTED";
    public const string ReserveRetracted = "RESERVE_RETRACTED";
    public const string GlPostingSimulated = "GL_POSTING_SIMULATED";
    public const string GlPostingFailed = "GL_POSTING_FAILED";
    public const string DocumentUploaded = "DOCUMENT_UPLOADED";
    public const string ClaimClosed = "CLAIM_CLOSED";
    public const string ClaimReopened = "CLAIM_REOPENED";
    public const string SlaBreachDetected = "SLA_BREACH_DETECTED";
    public const string ValidationIssueAdded = "VALIDATION_ISSUE_ADDED";

    // Additions beyond S14.1 (documented in DECISIONS.md / ai-log.md).
    public const string ValidationIssueWaived = "VALIDATION_ISSUE_WAIVED";
    public const string WarningsAcknowledged = "WARNINGS_ACKNOWLEDGED";
    public const string ClaimUpdated = "CLAIM_UPDATED";
    public const string PolicyLinked = "POLICY_LINKED";
    public const string HandlerAssigned = "HANDLER_ASSIGNED";
    public const string ReserveLimitOverrideSet = "RESERVE_LIMIT_OVERRIDE_SET";
    public const string GlPostingRetryRequested = "GL_POSTING_RETRY_REQUESTED";
}

/// <summary>One audit log entry to append. Old and new values are serialised to JSON by the service.</summary>
public sealed record AuditEntry(
    Guid ClaimId,
    string EventType,
    string Description,
    object? OldValue = null,
    object? NewValue = null,
    Guid? RelatedEntityId = null,
    string? RelatedEntityType = null,
    Guid? ActorUserId = null);

/// <summary>
/// The only way to write to ClaimAuditLog (S14.2). It is append-only by design: there is no update
/// or delete API. Entries join the current Unit of Work, so they commit atomically with the change.
/// </summary>
public interface IAuditLogService
{
    void Record(AuditEntry entry);
}
