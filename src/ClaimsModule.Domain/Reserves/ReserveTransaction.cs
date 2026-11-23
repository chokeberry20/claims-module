using System.Globalization;
using ClaimsModule.Domain.Common;

namespace ClaimsModule.Domain.Reserves;

/// <summary>
/// One row of reserve history (S6.6, S9.6). It is stored in the ReserveHistory table.
/// It is event-sourced: <see cref="Amount"/> (the signed delta), <see cref="ChangeSequence"/>,
/// <see cref="IdempotencyKey"/>, submitter and reason never change after insert. Only the approval
/// and posting lifecycle fields change (D-22). Previous and new balance are recomputed when a
/// pending transaction is approved (D-17).
/// </summary>
public sealed class ReserveTransaction : Entity
{
    public const int MaxChangeReasonLength = 500;
    public const int MaxRejectionReasonLength = 500;
    public const int MaxIdempotencyKeyLength = 200;

    private ReserveTransaction()
    {
    }

    private ReserveTransaction(Guid id)
        : base(id)
    {
    }

    public Guid ReserveComponentId { get; private set; }

    public Guid ClaimId { get; private set; }

    public ReserveComponentType ComponentType { get; private set; }

    public ReserveTransactionType TransactionType { get; private set; }

    /// <summary>Signed delta: positive is an increase, negative a decrease (S9.6).</summary>
    public Money Amount { get; private set; }

    public Money PreviousBalance { get; private set; }

    public Money NewBalance { get; private set; }

    public ApprovalStatus ApprovalStatus { get; private set; }

    /// <summary>The authority implied by |Amount| (S6.3).</summary>
    public ApprovalLevel RequiredApprovalLevel { get; private set; }

    /// <summary>The submission would have pushed the claim over the aggregate limit (D-19).</summary>
    public bool RequiresLimitOverride { get; private set; }

    public Guid? ApprovedByUserId { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    public Guid? RejectedByUserId { get; private set; }

    public DateTimeOffset? RejectedAt { get; private set; }

    public string? RejectionReason { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public string ChangeReason { get; private set; } = string.Empty;

    public PostingStatus PostingStatus { get; private set; }

    public string? PostingJobId { get; private set; }

    public DateTimeOffset? PostedAt { get; private set; }

    public string? PostingFailureReason { get; private set; }

    /// <summary>When the stale-posting sweep last re-enqueued the GL job (task 27a, D-56). Null if never re-enqueued.</summary>
    public DateTimeOffset? PostingLastEnqueuedAt { get; private set; }

    /// <summary><c>Reserve:{ReserveId}:Change:{ChangeSequence}</c> (BR-R-06).</summary>
    public string IdempotencyKey { get; private set; } = string.Empty;

    /// <summary>Strictly increasing per component (S9.6).</summary>
    public int ChangeSequence { get; private set; }

    public Guid SubmittedByUserId { get; private set; }

    public string SubmittedByName { get; private set; } = string.Empty;

    public DateTimeOffset SubmittedAt { get; private set; }

    public bool IsApproved => ApprovalStatus is ApprovalStatus.AutoApproved or ApprovalStatus.Approved;

    public bool IsPendingApproval => ApprovalStatus == ApprovalStatus.PendingApproval;

    public static string BuildIdempotencyKey(Guid reserveComponentId, int changeSequence) =>
        string.Create(CultureInfo.InvariantCulture, $"Reserve:{reserveComponentId}:Change:{changeSequence}");

    internal static ReserveTransaction Submit(
        ClaimReserveComponent component,
        ReserveTransactionType type,
        Money delta,
        ApprovalRequirement requirement,
        int changeSequence,
        string changeReason,
        Actor submitter,
        DateTimeOffset now)
    {
        var autoApproved = requirement.IsAutoApproved;
        return new ReserveTransaction(SequentialGuid.NewGuid())
        {
            ReserveComponentId = component.Id,
            ClaimId = component.ClaimId,
            ComponentType = component.ComponentType,
            TransactionType = type,
            Amount = delta,
            PreviousBalance = component.CurrentAmount,
            NewBalance = component.CurrentAmount + delta,
            ApprovalStatus = autoApproved ? ApprovalStatus.AutoApproved : ApprovalStatus.PendingApproval,
            ApprovedAt = autoApproved ? now.ToUniversalTime() : null,
            RequiredApprovalLevel = requirement.AmountLevel,
            RequiresLimitOverride = requirement.RequiresLimitOverride,
            ChangeReason = changeReason,
            PostingStatus = PostingStatus.Pending,
            IdempotencyKey = BuildIdempotencyKey(component.Id, changeSequence),
            ChangeSequence = changeSequence,
            SubmittedByUserId = submitter.UserId,
            SubmittedByName = submitter.DisplayName,
            SubmittedAt = now.ToUniversalTime(),
        };
    }

    internal void MarkApproved(Actor approver, Money previousBalance, DateTimeOffset now)
    {
        ApprovalStatus = ApprovalStatus.Approved;
        ApprovedByUserId = approver.UserId;
        ApprovedAt = now.ToUniversalTime();
        PreviousBalance = previousBalance;
        NewBalance = previousBalance + Amount;
    }

    internal void MarkRejected(Actor approver, string reason, DateTimeOffset now)
    {
        ApprovalStatus = ApprovalStatus.Rejected;
        RejectedByUserId = approver.UserId;
        RejectedAt = now.ToUniversalTime();
        RejectionReason = reason;
        PostingStatus = PostingStatus.Cancelled;
    }

    internal void MarkCancelled(DateTimeOffset now)
    {
        ApprovalStatus = ApprovalStatus.Cancelled;
        CancelledAt = now.ToUniversalTime();
        PostingStatus = PostingStatus.Cancelled;
    }

    internal void MarkPosted(string jobId, DateTimeOffset now)
    {
        PostingStatus = PostingStatus.Posted;
        PostingJobId = jobId;
        PostedAt = now.ToUniversalTime();
        PostingFailureReason = null;
    }

    internal void MarkPostingFailed(string reason)
    {
        PostingStatus = PostingStatus.Failed;
        PostingFailureReason = reason;
    }

    internal void ResetPostingForRetry() => PostingStatus = PostingStatus.Pending;

    internal void MarkPostingRequeued(DateTimeOffset now) => PostingLastEnqueuedAt = now.ToUniversalTime();
}
