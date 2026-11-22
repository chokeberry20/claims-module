using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Common;

namespace ClaimsModule.Persistence.Entities;

/// <summary>
/// One row of the immutable, append-only ClaimAuditLog (S9.8, S14). It is written only through
/// IAuditLogService. EF refuses to modify or delete it, and a database trigger blocks UPDATE/DELETE.
/// It has no soft-delete or UpdatedAt columns (D-23).
/// </summary>
public sealed class ClaimAuditLog
{
    public Guid AuditLogId { get; init; }

    public Guid OrganisationId { get; init; }

    public Guid ClaimId { get; init; }

    public string EventType { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string? OldValue { get; init; }

    public string? NewValue { get; init; }

    public Guid? RelatedEntityId { get; init; }

    public string? RelatedEntityType { get; init; }

    public Guid? CorrelationId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public Guid? CreatedByUserId { get; init; }
}

/// <summary>
/// The allowed workflow transitions (brief §3.2), seeded from ClaimWorkflow.Transitions (D-08).
/// It is global configuration, not tenant data (D-51).
/// </summary>
public sealed class ClaimStatusTransition
{
    public Guid ClaimStatusTransitionId { get; init; }

    public ClaimStatus FromStatus { get; init; }

    public ClaimStatus ToStatus { get; init; }

    /// <summary>The brief's "RequiredPermission": the minimum role (D-09).</summary>
    public UserRole RequiredPermission { get; init; }

    public bool RequiresReason { get; init; }

    public string Description { get; init; } = string.Empty;
}

/// <summary>
/// Gapless claim number counter per organisation and year (S5.3, D-14). It is changed only by the
/// atomic UPDATE/MERGE … OUTPUT in ClaimNumberGenerator, inside the FNOL transaction.
/// </summary>
public sealed class ClaimNumberSequence
{
    public Guid OrganisationId { get; init; }

    public int Year { get; init; }

    public int LastValue { get; init; }
}

/// <summary>
/// The stored response for a write request carrying an <c>Idempotency-Key</c> header (S10).
/// A replay of the same key by the same user returns the stored response (P5 middleware).
/// </summary>
public sealed class IdempotencyRecord
{
    public Guid IdempotencyRecordId { get; init; }

    public Guid OrganisationId { get; init; }

    public Guid UserId { get; init; }

    public string IdempotencyKey { get; init; } = string.Empty;

    public string RequestMethod { get; init; } = string.Empty;

    public string RequestPath { get; init; } = string.Empty;

    /// <summary>SHA-256 of the request body (hex). Reusing a key with a different payload is rejected.</summary>
    public string RequestHash { get; init; } = string.Empty;

    public int ResponseStatusCode { get; init; }

    public string? ResponseBody { get; init; }

    public string? ResponseContentType { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }
}
