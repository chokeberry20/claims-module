using ClaimsModule.Domain.Common;
using ClaimsModule.Domain.Reserves.Events;

namespace ClaimsModule.Domain.Reserves;

/// <summary>The outcome of submitting a reserve transaction. <see cref="Warnings"/> holds non-blocking S8 warnings.</summary>
public sealed record ReserveSubmissionResult(
    ReserveTransaction Transaction,
    ApprovalRequirement Requirement,
    IReadOnlyList<RuleViolation> Warnings);

/// <summary>
/// The reserve component aggregate root (S9.5): one component type on one claim, plus its
/// append-only transaction history (S6.6). <see cref="CurrentAmount"/> is a projection kept in the
/// same transaction: the sum of approved transactions, whether or not they are posted yet (D-18).
/// Rules enforced here:
/// <list type="bullet">
/// <item>amount sign (BR-R-01);</item>
/// <item>authority level (S6.3);</item>
/// <item>no self-approval (BR-R-03);</item>
/// <item>a pending transaction can't change and only one may be pending (S6.4, D-17);</item>
/// <item>ChangeSequence and idempotency key (BR-R-06);</item>
/// <item>the aggregate limit re-checked at approval (D-19).</item>
/// </list>
/// The application guarantees one component per type per claim.
/// </summary>
public sealed class ClaimReserveComponent : AggregateRoot
{
    public const int MaxNotesLength = 4000;

    private readonly List<ReserveTransaction> _transactions = [];

    private ClaimReserveComponent()
    {
    }

    private ClaimReserveComponent(Guid id)
        : base(id)
    {
    }

    public Guid ClaimId { get; private set; }

    public ReserveComponentType ComponentType { get; private set; }

    public Money CurrentAmount { get; private set; }

    public ReserveComponentStatus Status { get; private set; }

    public string? Notes { get; private set; }

    public int LastChangeSequence { get; private set; }

    public IReadOnlyCollection<ReserveTransaction> Transactions => _transactions.AsReadOnly();

    public ReserveTransaction? PendingTransaction => _transactions.FirstOrDefault(t => t.IsPendingApproval);

    public bool HasPendingApproval => PendingTransaction is not null;

    /// <summary>For the Reserves tab summary card: the pending delta, shown amber (S11.3).</summary>
    public Money PendingAmount => PendingTransaction?.Amount ?? Money.Zero;

    /// <summary>The component has been opened: it has at least one approved transaction.</summary>
    public bool IsOpened => _transactions.Any(t => t.IsApproved);

    public static ClaimReserveComponent Create(Guid claimId, ReserveComponentType componentType, string? notes = null)
    {
        if (claimId == Guid.Empty)
        {
            throw new ArgumentException("Claim id must not be empty.", nameof(claimId));
        }

        if (!Enum.IsDefined(componentType))
        {
            throw new DomainRuleViolationException("RESERVE_COMPONENT_INVALID", "Invalid reserve component type.", "ReserveComponent");
        }

        return new ClaimReserveComponent(SequentialGuid.NewGuid())
        {
            ClaimId = claimId,
            ComponentType = componentType,
            CurrentAmount = Money.Zero,
            Status = ReserveComponentStatus.Active,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
        };
    }

    /// <summary>
    /// Submits a reserve transaction (POST /api/claims/{id}/reserves). D-16 semantics:
    /// <list type="bullet">
    /// <item><b>Add</b> opens the component; <paramref name="amount"/> is the initial balance.</item>
    /// <item><b>Adjust</b> sets a new target balance; the delta is computed.</item>
    /// <item><b>Reverse</b> brings the balance to zero; no amount is given.</item>
    /// </list>
    /// Authority uses |delta|. Auto-approved transactions update the balance at once and raise
    /// ReserveTransactionApproved, which leads to GL posting.
    /// </summary>
    /// <param name="approvedGrossTotalOnClaim">The claim's current approved gross reserves (D-19), across all components.</param>
    public ReserveSubmissionResult Submit(
        ReserveTransactionType transactionType,
        decimal? amount,
        string changeReason,
        Actor submitter,
        Money approvedGrossTotalOnClaim,
        DateTimeOffset now,
        ReserveAuthorityLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(submitter);
        EnsureActive();

        if (HasPendingApproval)
        {
            throw new DomainRuleViolationException(
                "RESERVE_PENDING_EXISTS",
                "A reserve transaction is already pending approval for this component. Approve, reject or retract it first.",
                "ReserveComponent");
        }

        var reason = RequireText(changeReason, ReserveTransaction.MaxChangeReasonLength, "CHANGE_REASON", "Change reason", "ChangeReason");
        var delta = ComputeDelta(transactionType, amount);

        var requirement = ReserveAuthorityPolicy.Determine(ComponentType, delta, approvedGrossTotalOnClaim, limits);
        var transaction = ReserveTransaction.Submit(this, transactionType, delta, requirement, ++LastChangeSequence, reason, submitter, now);
        _transactions.Add(transaction);

        Raise(new ReserveTransactionSubmitted(
            ClaimId, Id, transaction.Id, ComponentType, transactionType, delta.Amount, transaction.NewBalance.Amount,
            transaction.ApprovalStatus, requirement.EffectiveLevel, requirement.RequiresLimitOverride, submitter.UserId, now));

        if (requirement.IsAutoApproved)
        {
            CurrentAmount = transaction.NewBalance;
            RaiseApproved(transaction, isAuto: true, approverUserId: null, now);
        }

        IReadOnlyList<RuleViolation> warnings = requirement.RequiresLimitOverride
            ? [new RuleViolation("RESERVE_AGGREGATE_LIMIT", ReserveAuthorityPolicy.AggregateLimitWarning(limits), "ReserveAmount")]
            : [];

        return new ReserveSubmissionResult(transaction, requirement, warnings);
    }

    /// <summary>
    /// Approves a pending transaction (S6.4, BR-R-02, BR-R-03). The aggregate limit is checked again
    /// against the approved total right now (D-19).
    /// </summary>
    public void Approve(
        Guid transactionId,
        Actor approver,
        Money approvedGrossTotalOnClaim,
        bool claimLimitOverrideSet,
        DateTimeOffset now,
        ReserveAuthorityLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(approver);
        var transaction = GetPending(transactionId, "approved");

        if (!approver.HasAtLeast(UserRole.Supervisor))
        {
            throw new DomainForbiddenException("RESERVE_APPROVER_ROLE", "Your role does not have authority to approve this reserve amount.");
        }

        if (approver.UserId == transaction.SubmittedByUserId)
        {
            throw new DomainRuleViolationException("RESERVE_SELF_APPROVAL", "Self-approval is not permitted.", "ReserveApproval");
        }

        if (!ReserveAuthorityPolicy.CanApprove(approver.Role, transaction.RequiredApprovalLevel))
        {
            throw new DomainRuleViolationException(
                "RESERVE_INSUFFICIENT_AUTHORITY",
                "Your role does not have authority to approve this reserve amount.",
                "ReserveApproval");
        }

        var breachesNow = ReserveAuthorityPolicy.WouldExceedAggregateLimit(ComponentType, transaction.Amount, approvedGrossTotalOnClaim, limits);
        if (breachesNow && !(approver.HasAtLeast(UserRole.Manager) && claimLimitOverrideSet))
        {
            throw new DomainRuleViolationException(
                "RESERVE_AGGREGATE_LIMIT",
                ReserveAuthorityPolicy.AggregateLimitWarning(limits),
                "ReserveApproval");
        }

        var newBalance = CurrentAmount + transaction.Amount;
        if (newBalance.IsNegative && !ComponentType.AllowsNegativeAmounts())
        {
            throw new DomainRuleViolationException("RESERVE_AMOUNT_NOT_POSITIVE", "Reserve amount must be greater than zero.", "ReserveAmount");
        }

        transaction.MarkApproved(approver, CurrentAmount, now);
        CurrentAmount = newBalance;
        RaiseApproved(transaction, isAuto: false, approver.UserId, now);
    }

    /// <summary>
    /// Rejects a pending transaction with a reason (S6.4, BR-R-04). Supervisors may reject up to
    /// their approval authority (S3). The submitter may then submit a revised transaction.
    /// </summary>
    public void Reject(Guid transactionId, Actor approver, string rejectionReason, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(approver);
        var transaction = GetPending(transactionId, "rejected");

        if (!approver.HasAtLeast(UserRole.Supervisor))
        {
            throw new DomainForbiddenException("RESERVE_APPROVER_ROLE", "Your role does not have authority to reject this reserve amount.");
        }

        if (!ReserveAuthorityPolicy.CanApprove(approver.Role, transaction.RequiredApprovalLevel))
        {
            throw new DomainRuleViolationException(
                "RESERVE_INSUFFICIENT_AUTHORITY",
                "Your role does not have authority to reject this reserve amount.",
                "ReserveApproval");
        }

        var reason = RequireText(rejectionReason, ReserveTransaction.MaxRejectionReasonLength, "REJECTION_REASON", "Rejection reason", "RejectionReason");
        transaction.MarkRejected(approver, reason, now);
        Raise(new ReserveTransactionRejected(ClaimId, Id, transaction.Id, transaction.Amount.Amount, reason, approver.UserId, now));
    }

    /// <summary>Only the submitter may retract their own pending transaction (→ Cancelled, S6.4).</summary>
    public void Retract(Guid transactionId, Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var transaction = GetPending(transactionId, "retracted");

        if (actor.UserId != transaction.SubmittedByUserId)
        {
            throw new DomainForbiddenException(
                "RESERVE_RETRACT_NOT_SUBMITTER",
                "Only the submitter can retract a pending reserve transaction.");
        }

        transaction.MarkCancelled(now);
        Raise(new ReserveTransactionRetracted(ClaimId, Id, transaction.Id, transaction.Amount.Amount, actor.UserId, now));
    }

    // ----------------------------------------------------------------- GL posting (S12.1)

    /// <summary>Records a successful simulated GL posting. It is idempotent: returns false if already posted.</summary>
    public bool MarkPosted(Guid transactionId, string jobId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        var transaction = GetApproved(transactionId);
        if (transaction.PostingStatus == PostingStatus.Posted)
        {
            return false;
        }

        transaction.MarkPosted(jobId, now);
        Raise(new ReservePostingCompleted(ClaimId, Id, transaction.Id, transaction.IdempotencyKey, jobId, transaction.Amount.Amount, now));
        return true;
    }

    /// <summary>Records a posting that failed after all retries. Returns false if it was already posted.</summary>
    public bool MarkPostingFailed(Guid transactionId, string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var transaction = GetApproved(transactionId);
        if (transaction.PostingStatus == PostingStatus.Posted)
        {
            return false;
        }

        transaction.MarkPostingFailed(reason.Trim());
        Raise(new ReservePostingFailed(ClaimId, Id, transaction.Id, transaction.IdempotencyKey, reason.Trim(), now));
        return true;
    }

    /// <summary>
    /// Task 27a (D-56): the stale-posting sweep re-enqueues an approved posting that is still Pending
    /// (its after-commit enqueue was lost). Stamps <see cref="ReserveTransaction.PostingLastEnqueuedAt"/> so
    /// the next sweep waits again, and raises <see cref="ReservePostingRequeued"/>, whose after-commit
    /// handler enqueues the job with the same idempotency key. Returns false once it isn't Pending.
    /// </summary>
    public bool MarkPostingRequeued(Guid transactionId, DateTimeOffset now)
    {
        var transaction = GetApproved(transactionId);
        if (transaction.PostingStatus != PostingStatus.Pending)
        {
            return false;
        }

        transaction.MarkPostingRequeued(now);
        Raise(new ReservePostingRequeued(ClaimId, Id, transaction.Id, transaction.IdempotencyKey, now));
        return true;
    }

    /// <summary>D-28: Supervisor+ re-queues a failed posting with the same idempotency key.</summary>
    public void RequestPostingRetry(Guid transactionId, Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!actor.HasAtLeast(UserRole.Supervisor))
        {
            throw new DomainForbiddenException("POSTING_RETRY_ROLE", "Only a Supervisor or Manager can retry a failed GL posting.");
        }

        var transaction = GetApproved(transactionId);
        if (transaction.PostingStatus != PostingStatus.Failed)
        {
            throw new DomainRuleViolationException("POSTING_NOT_FAILED", "Only failed GL postings can be retried.", "PostingStatus");
        }

        transaction.ResetPostingForRetry();
        Raise(new ReservePostingRetryRequested(ClaimId, Id, transaction.Id, transaction.IdempotencyKey, actor.UserId, now));
    }

    // ----------------------------------------------------------------- claim lifecycle (D-21)

    /// <summary>Closes the component when its claim closes. Balances are unchanged (D-21).</summary>
    public void Close()
    {
        if (HasPendingApproval)
        {
            throw new DomainRuleViolationException(
                "RESERVE_PENDING_EXISTS",
                "A reserve component with a pending transaction cannot be closed.",
                "ReserveComponent");
        }

        Status = ReserveComponentStatus.Closed;
    }

    /// <summary>Reactivates the component when its claim is reopened.</summary>
    public void Reactivate() => Status = ReserveComponentStatus.Active;

    // ----------------------------------------------------------------- helpers

    private Money ComputeDelta(ReserveTransactionType transactionType, decimal? amount)
    {
        switch (transactionType)
        {
            case ReserveTransactionType.Add:
                if (IsOpened)
                {
                    throw new DomainRuleViolationException(
                        "RESERVE_ALREADY_OPEN",
                        "The reserve component is already open; use Adjust to change it.",
                        "TransactionType");
                }

                var initial = RequireAmount(amount);
                EnsureAllowedAmount(initial);
                return initial;

            case ReserveTransactionType.Adjust:
                EnsureOpened();
                var target = RequireAmount(amount);
                if (!ComponentType.AllowsNegativeAmounts() && !target.IsPositive)
                {
                    throw AmountNotPositive();
                }

                var delta = target - CurrentAmount;
                return delta.IsZero
                    ? throw new DomainRuleViolationException("RESERVE_NO_CHANGE", "The new reserve amount equals the current balance.", "ReserveAmount")
                    : delta;

            case ReserveTransactionType.Reverse:
                EnsureOpened();
                if (amount is { } given && given != 0m)
                {
                    throw new DomainRuleViolationException(
                        "RESERVE_REVERSE_AMOUNT",
                        "A reversal always brings the balance to zero; do not supply an amount.",
                        "ReserveAmount");
                }

                return CurrentAmount.IsZero
                    ? throw new DomainRuleViolationException("RESERVE_NO_CHANGE", "The reserve balance is already zero.", "ReserveAmount")
                    : -CurrentAmount;

            default:
                throw new DomainRuleViolationException("RESERVE_TRANSACTION_TYPE_INVALID", "Invalid reserve transaction type.", "TransactionType");
        }
    }

    private void EnsureAllowedAmount(Money amount)
    {
        if (ComponentType.AllowsNegativeAmounts())
        {
            if (amount.IsZero)
            {
                throw new DomainRuleViolationException("RESERVE_AMOUNT_ZERO", "Reserve amount must not be zero.", "ReserveAmount");
            }
        }
        else if (!amount.IsPositive)
        {
            throw AmountNotPositive();
        }
    }

    private static DomainRuleViolationException AmountNotPositive() =>
        new("RESERVE_AMOUNT_NOT_POSITIVE", "Reserve amount must be greater than zero.", "ReserveAmount");

    private static Money RequireAmount(decimal? amount) =>
        amount is { } value
            ? Money.Of(value)
            : throw new DomainRuleViolationException("RESERVE_AMOUNT_REQUIRED", "Reserve amount is required.", "ReserveAmount");

    private void EnsureOpened()
    {
        if (!IsOpened)
        {
            throw new DomainRuleViolationException(
                "RESERVE_NOT_OPEN",
                "The reserve component has not been opened yet; use Add.",
                "TransactionType");
        }
    }

    private void EnsureActive()
    {
        if (Status != ReserveComponentStatus.Active)
        {
            throw new DomainRuleViolationException(
                "RESERVE_COMPONENT_CLOSED",
                "The reserve component is closed.",
                "ReserveComponent");
        }
    }

    private ReserveTransaction Get(Guid transactionId) =>
        _transactions.FirstOrDefault(t => t.Id == transactionId)
        ?? throw new DomainNotFoundException(nameof(ReserveTransaction), transactionId);

    private ReserveTransaction GetPending(Guid transactionId, string action)
    {
        var transaction = Get(transactionId);
        return transaction.IsPendingApproval
            ? transaction
            : throw new DomainRuleViolationException(
                "RESERVE_NOT_PENDING",
                $"Only transactions pending approval can be {action}.",
                "ReserveApproval");
    }

    private ReserveTransaction GetApproved(Guid transactionId)
    {
        var transaction = Get(transactionId);
        return transaction.IsApproved
            ? transaction
            : throw new DomainRuleViolationException(
                "RESERVE_NOT_APPROVED",
                "Only approved reserve transactions are posted to the general ledger.",
                "PostingStatus");
    }

    private void RaiseApproved(ReserveTransaction transaction, bool isAuto, Guid? approverUserId, DateTimeOffset now) =>
        Raise(new ReserveTransactionApproved(
            ClaimId, Id, transaction.Id, ComponentType, transaction.Amount.Amount, transaction.NewBalance.Amount,
            transaction.ChangeSequence, transaction.IdempotencyKey, isAuto, approverUserId, now));

    private static string RequireText(string? value, int maxLength, string codePrefix, string label, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainRuleViolationException($"{codePrefix}_REQUIRED", $"{label} is required.", field);
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : throw new DomainRuleViolationException($"{codePrefix}_TOO_LONG", $"{label} must be at most {maxLength} characters.", field);
    }
}
