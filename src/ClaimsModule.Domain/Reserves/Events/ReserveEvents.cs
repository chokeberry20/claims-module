using ClaimsModule.Domain.Common;

namespace ClaimsModule.Domain.Reserves.Events;

// Domain events raised by the ClaimReserveComponent aggregate. Amounts are plain decimals so the
// audit log's NewValue/OldValue JSON stays simple.

/// <summary>Maps to RESERVE_CREATED (S14.1). Raised for every submitted transaction.</summary>
public sealed record ReserveTransactionSubmitted(
    Guid ClaimId,
    Guid ReserveComponentId,
    Guid TransactionId,
    ReserveComponentType ComponentType,
    ReserveTransactionType TransactionType,
    decimal Amount,
    decimal NewBalance,
    ApprovalStatus ApprovalStatus,
    ApprovalLevel RequiredLevel,
    bool RequiresLimitOverride,
    Guid ActorUserId,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

/// <summary>
/// Maps to RESERVE_AUTO_APPROVED or RESERVE_APPROVED (S14.1). After commit the application
/// enqueues PostGLReserveChangeJob with <see cref="IdempotencyKey"/> (S12.1, BR-R-06).
/// </summary>
public sealed record ReserveTransactionApproved(
    Guid ClaimId,
    Guid ReserveComponentId,
    Guid TransactionId,
    ReserveComponentType ComponentType,
    decimal Amount,
    decimal NewBalance,
    int ChangeSequence,
    string IdempotencyKey,
    bool IsAutoApproved,
    Guid? ApproverUserId,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

/// <summary>Maps to RESERVE_REJECTED. OldValue carries the rejection reason (S14.1).</summary>
public sealed record ReserveTransactionRejected(
    Guid ClaimId,
    Guid ReserveComponentId,
    Guid TransactionId,
    decimal Amount,
    string Reason,
    Guid ActorUserId,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

/// <summary>Maps to RESERVE_RETRACTED.</summary>
public sealed record ReserveTransactionRetracted(
    Guid ClaimId,
    Guid ReserveComponentId,
    Guid TransactionId,
    decimal Amount,
    Guid ActorUserId,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

/// <summary>Maps to GL_POSTING_SIMULATED.</summary>
public sealed record ReservePostingCompleted(
    Guid ClaimId,
    Guid ReserveComponentId,
    Guid TransactionId,
    string IdempotencyKey,
    string JobId,
    decimal Amount,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

/// <summary>Maps to GL_POSTING_FAILED.</summary>
public sealed record ReservePostingFailed(
    Guid ClaimId,
    Guid ReserveComponentId,
    Guid TransactionId,
    string IdempotencyKey,
    string Reason,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

/// <summary>Maps to GL_POSTING_RETRY_REQUESTED (D-28). The application re-enqueues with the same idempotency key.</summary>
public sealed record ReservePostingRetryRequested(
    Guid ClaimId,
    Guid ReserveComponentId,
    Guid TransactionId,
    string IdempotencyKey,
    Guid ActorUserId,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

/// <summary>The stale-posting sweep re-enqueued a Pending GL posting (task 27a). System-initiated.</summary>
public sealed record ReservePostingRequeued(
    Guid ClaimId,
    Guid ReserveComponentId,
    Guid TransactionId,
    string IdempotencyKey,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
