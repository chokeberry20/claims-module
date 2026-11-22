using ClaimsModule.Domain.Common;
using ClaimsModule.Domain.Reserves;
using ClaimsModule.Domain.Reserves.Events;
using static ClaimsModule.Domain.Tests.TestData;

namespace ClaimsModule.Domain.Tests.Reserves;

public sealed class ReserveComponentTests
{
    private static readonly Guid ClaimId = Guid.NewGuid();

    private static ClaimReserveComponent NewComponent(ReserveComponentType type = ReserveComponentType.Indemnity) =>
        ClaimReserveComponent.Create(ClaimId, type);

    private static ReserveSubmissionResult Add(ClaimReserveComponent c, decimal amount, Actor? by = null, decimal gross = 0m) =>
        c.Submit(ReserveTransactionType.Add, amount, "Initial reserve at intake", by ?? Handler1, Money.Of(gross), Now);

    private static ReserveSubmissionResult Adjust(ClaimReserveComponent c, decimal target, Actor? by = null, decimal gross = 0m) =>
        c.Submit(ReserveTransactionType.Adjust, target, "Revised estimate after inspection", by ?? Handler1, Money.Of(gross), Now);

    /// <summary>An opened component with an auto-approved balance (≤ $10k, or Add + Adjust steps).</summary>
    private static ClaimReserveComponent Opened(decimal balance)
    {
        var c = NewComponent();
        var first = Math.Min(balance, 10_000m);
        Add(c, first);
        var remaining = balance - first;
        while (remaining > 0)
        {
            var step = Math.Min(remaining, 10_000m);
            Adjust(c, c.CurrentAmount.Amount + step);
            remaining -= step;
        }

        c.ClearDomainEvents();
        return c;
    }

    // ------------------------------------------------------------------ creation

    [Fact]
    public void New_component_is_active_with_zero_balance()
    {
        var c = NewComponent(ReserveComponentType.ALAE);

        c.Status.ShouldBe(ReserveComponentStatus.Active);
        c.CurrentAmount.ShouldBe(Money.Zero);
        c.IsOpened.ShouldBeFalse();
        c.ClaimId.ShouldBe(ClaimId);
    }

    [Fact]
    public void Unknown_component_type_is_rejected_with_S8_message() =>
        Should.Throw<DomainRuleViolationException>(() => ClaimReserveComponent.Create(ClaimId, (ReserveComponentType)99))
            .Violations.ShouldHaveSingleItem().Message.ShouldBe("Invalid reserve component type.");

    // ------------------------------------------------------------------ auto-approval & event sourcing

    [Fact]
    public void BR_R_02_add_up_to_10k_is_auto_approved_and_raises_approved_event_with_idempotency_key()
    {
        var c = NewComponent();

        var result = Add(c, 10_000m);

        var txn = result.Transaction;
        txn.ApprovalStatus.ShouldBe(ApprovalStatus.AutoApproved);
        txn.ApprovedAt.ShouldBe(Now);
        txn.ApprovedByUserId.ShouldBeNull();
        txn.PostingStatus.ShouldBe(PostingStatus.Pending);
        txn.Amount.ShouldBe(Money.Of(10_000m));
        (txn.PreviousBalance, txn.NewBalance).ShouldBe((Money.Zero, Money.Of(10_000m)));
        txn.ChangeSequence.ShouldBe(1);
        txn.IdempotencyKey.ShouldBe($"Reserve:{c.Id}:Change:1");
        txn.SubmittedByUserId.ShouldBe(Handler1.UserId);
        c.CurrentAmount.ShouldBe(Money.Of(10_000m));
        result.Warnings.ShouldBeEmpty();

        c.DomainEvents.Select(e => e.GetType()).ShouldBe([typeof(ReserveTransactionSubmitted), typeof(ReserveTransactionApproved)]);
        var approved = c.DomainEvents.OfType<ReserveTransactionApproved>().Single();
        approved.IsAutoApproved.ShouldBeTrue();
        approved.IdempotencyKey.ShouldBe(txn.IdempotencyKey);
    }

    [Theory]
    [InlineData(10_000.01, ApprovalLevel.Supervisor)]
    [InlineData(100_000, ApprovalLevel.Supervisor)]
    [InlineData(100_000.01, ApprovalLevel.Manager)]
    public void BR_R_02_above_10k_is_pending_and_does_not_change_the_balance(decimal amount, ApprovalLevel level)
    {
        var c = NewComponent();

        var result = Add(c, amount);

        result.Transaction.ApprovalStatus.ShouldBe(ApprovalStatus.PendingApproval);
        result.Transaction.RequiredApprovalLevel.ShouldBe(level);
        c.CurrentAmount.ShouldBe(Money.Zero);
        c.PendingAmount.ShouldBe(Money.Of(amount));
        c.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ReserveTransactionSubmitted>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void BR_R_01_amount_must_be_greater_than_zero(decimal amount) =>
        Should.Throw<DomainRuleViolationException>(() => Add(NewComponent(), amount))
            .Violations.ShouldHaveSingleItem().Message.ShouldBe("Reserve amount must be greater than zero.");

    [Fact]
    public void BR_R_01_subrogation_recoverable_may_be_negative()
    {
        var c = NewComponent(ReserveComponentType.SubrogationRecoverable);

        Add(c, -8_000m);

        c.CurrentAmount.ShouldBe(Money.Of(-8_000m));
    }

    [Fact]
    public void Subrogation_recoverable_amount_must_not_be_zero() =>
        Should.Throw<DomainRuleViolationException>(() => Add(NewComponent(ReserveComponentType.SubrogationRecoverable), 0m))
            .Violations.ShouldHaveSingleItem().Code.ShouldBe("RESERVE_AMOUNT_ZERO");

    [Fact]
    public void Add_requires_an_amount_and_a_change_reason()
    {
        var c = NewComponent();

        Should.Throw<DomainRuleViolationException>(() =>
                c.Submit(ReserveTransactionType.Add, null, "reason", Handler1, Money.Zero, Now))
            .Violations.ShouldHaveSingleItem().Code.ShouldBe("RESERVE_AMOUNT_REQUIRED");
        Should.Throw<DomainRuleViolationException>(() =>
                c.Submit(ReserveTransactionType.Add, 100m, "  ", Handler1, Money.Zero, Now))
            .Violations.ShouldHaveSingleItem().Code.ShouldBe("CHANGE_REASON_REQUIRED");
        Should.Throw<DomainRuleViolationException>(() =>
                c.Submit(ReserveTransactionType.Add, 100m, new string('r', 501), Handler1, Money.Zero, Now))
            .Violations.ShouldHaveSingleItem().Code.ShouldBe("CHANGE_REASON_TOO_LONG");
    }

    [Fact]
    public void Add_on_an_opened_component_is_rejected_and_adjust_needs_an_opened_component()
    {
        Should.Throw<DomainRuleViolationException>(() => Add(Opened(5_000m), 100m))
            .Violations.ShouldHaveSingleItem().Code.ShouldBe("RESERVE_ALREADY_OPEN");
        Should.Throw<DomainRuleViolationException>(() => Adjust(NewComponent(), 100m))
            .Violations.ShouldHaveSingleItem().Code.ShouldBe("RESERVE_NOT_OPEN");
    }

    // ------------------------------------------------------------------ adjust / reverse (D-16)

    [Fact]
    public void D_16_adjust_takes_a_target_balance_and_stores_the_signed_delta()
    {
        var c = Opened(25_000m);

        var result = Adjust(c, 30_000m);

        result.Transaction.TransactionType.ShouldBe(ReserveTransactionType.Adjust);
        result.Transaction.Amount.ShouldBe(Money.Of(5_000m));
        (result.Transaction.PreviousBalance, result.Transaction.NewBalance).ShouldBe((Money.Of(25_000m), Money.Of(30_000m)));
        c.CurrentAmount.ShouldBe(Money.Of(30_000m));
    }

    [Fact]
    public void D_16_a_large_decrease_needs_approval_too()
    {
        var c = Opened(30_000m);

        var result = Adjust(c, 5_000m);

        result.Transaction.Amount.ShouldBe(Money.Of(-25_000m));
        result.Transaction.ApprovalStatus.ShouldBe(ApprovalStatus.PendingApproval);
        result.Transaction.RequiredApprovalLevel.ShouldBe(ApprovalLevel.Supervisor);
        c.CurrentAmount.ShouldBe(Money.Of(30_000m));
    }

    [Fact]
    public void D_16_adjust_target_must_be_positive_and_different_from_current()
    {
        var c = Opened(5_000m);

        Should.Throw<DomainRuleViolationException>(() => Adjust(c, 0m))
            .Violations.ShouldHaveSingleItem().Code.ShouldBe("RESERVE_AMOUNT_NOT_POSITIVE");
        Should.Throw<DomainRuleViolationException>(() => Adjust(c, 5_000m))
            .Violations.ShouldHaveSingleItem().Code.ShouldBe("RESERVE_NO_CHANGE");
    }

    [Fact]
    public void D_16_reverse_brings_the_balance_to_zero()
    {
        var c = Opened(8_000m);

        var result = c.Submit(ReserveTransactionType.Reverse, null, "Claim settled below estimate", Handler1, Money.Of(8_000m), Now);

        result.Transaction.Amount.ShouldBe(Money.Of(-8_000m));
        c.CurrentAmount.ShouldBe(Money.Zero);
        Should.Throw<DomainRuleViolationException>(() =>
                c.Submit(ReserveTransactionType.Reverse, null, "again", Handler1, Money.Zero, Now))
            .Violations.ShouldHaveSingleItem().Code.ShouldBe("RESERVE_NO_CHANGE");
    }

    [Fact]
    public void D_16_reverse_does_not_accept_an_amount() =>
        Should.Throw<DomainRuleViolationException>(() =>
                Opened(8_000m).Submit(ReserveTransactionType.Reverse, 100m, "reason", Handler1, Money.Zero, Now))
            .Violations.ShouldHaveSingleItem().Code.ShouldBe("RESERVE_REVERSE_AMOUNT");

    [Fact]
    public void Change_sequence_is_monotonic_and_idempotency_keys_are_unique()
    {
        var c = Opened(30_000m); // seq 1..3
        var pending = Adjust(c, 80_000m).Transaction; // seq 4, pending
        c.Retract(pending.Id, Handler1, Now);
        Adjust(c, 31_000m); // seq 5

        c.Transactions.Select(t => t.ChangeSequence).ShouldBe([1, 2, 3, 4, 5]);
        c.Transactions.Select(t => t.IdempotencyKey).Distinct().Count().ShouldBe(5);
        c.LastChangeSequence.ShouldBe(5);
    }

    // ------------------------------------------------------------------ pending rules (S6.4, D-17)

    [Fact]
    public void S6_4_only_one_pending_transaction_per_component()
    {
        var c = NewComponent();
        Add(c, 50_000m);

        Should.Throw<DomainRuleViolationException>(() =>
                c.Submit(ReserveTransactionType.Add, 1_000m, "small one", Handler1, Money.Zero, Now))
            .Violations.ShouldHaveSingleItem().Code.ShouldBe("RESERVE_PENDING_EXISTS");
    }

    // ------------------------------------------------------------------ approval (BR-R-02, BR-R-03)

    [Fact]
    public void Supervisor_approves_up_to_100k_and_balance_and_event_follow()
    {
        var c = NewComponent();
        var txn = Add(c, 50_000m).Transaction;
        c.ClearDomainEvents();

        c.Approve(txn.Id, Supervisor, Money.Zero, false, Now);

        txn.ApprovalStatus.ShouldBe(ApprovalStatus.Approved);
        txn.ApprovedByUserId.ShouldBe(Supervisor.UserId);
        txn.ApprovedAt.ShouldBe(Now);
        txn.NewBalance.ShouldBe(Money.Of(50_000m));
        c.CurrentAmount.ShouldBe(Money.Of(50_000m));
        c.HasPendingApproval.ShouldBeFalse();
        var approved = c.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ReserveTransactionApproved>();
        approved.IsAutoApproved.ShouldBeFalse();
        approved.ApproverUserId.ShouldBe(Supervisor.UserId);
    }

    [Fact]
    public void BR_R_03_self_approval_is_rejected_even_for_a_manager()
    {
        var c = NewComponent();
        var txn = Add(c, 150_000m, Manager).Transaction;

        Should.Throw<DomainRuleViolationException>(() => c.Approve(txn.Id, Manager, Money.Zero, false, Now))
            .Violations.ShouldHaveSingleItem().Message.ShouldBe("Self-approval is not permitted.");
        txn.IsPendingApproval.ShouldBeTrue();
    }

    [Fact]
    public void D_24_handler_cannot_approve_at_all()
    {
        var c = NewComponent();
        var txn = Add(c, 20_000m).Transaction;

        Should.Throw<DomainForbiddenException>(() => c.Approve(txn.Id, Handler2, Money.Zero, false, Now))
            .Code.ShouldBe("RESERVE_APPROVER_ROLE");
    }

    [Fact]
    public void Supervisor_cannot_approve_above_100k_but_manager_can()
    {
        var c = NewComponent();
        var txn = Add(c, 100_000.01m).Transaction;

        Should.Throw<DomainRuleViolationException>(() => c.Approve(txn.Id, Supervisor, Money.Zero, false, Now))
            .Violations.ShouldHaveSingleItem().Message.ShouldBe("Your role does not have authority to approve this reserve amount.");

        c.Approve(txn.Id, Manager, Money.Zero, false, Now);
        c.CurrentAmount.ShouldBe(Money.Of(100_000.01m));
    }

    [Fact]
    public void Only_pending_transactions_can_be_approved()
    {
        var c = NewComponent();
        var auto = Add(c, 1_000m).Transaction;

        Should.Throw<DomainRuleViolationException>(() => c.Approve(auto.Id, Supervisor, Money.Zero, false, Now))
            .Violations.ShouldHaveSingleItem().Code.ShouldBe("RESERVE_NOT_PENDING");
        Should.Throw<DomainNotFoundException>(() => c.Approve(Guid.NewGuid(), Supervisor, Money.Zero, false, Now));
    }

    // ------------------------------------------------------------------ aggregate limit (BR-R-05, D-19)

    [Fact]
    public void D_19_small_breaching_transaction_goes_pending_with_the_S8_warning()
    {
        var c = NewComponent(ReserveComponentType.Expense);

        var result = Add(c, 500m, gross: 9_999_800m);

        result.Transaction.ApprovalStatus.ShouldBe(ApprovalStatus.PendingApproval);
        result.Transaction.RequiresLimitOverride.ShouldBeTrue();
        result.Requirement.EffectiveLevel.ShouldBe(ApprovalLevel.Manager);
        result.Warnings.ShouldHaveSingleItem().Message.ShouldBe("Total reserves will exceed $10,000,000. Manager override required.");
        c.CurrentAmount.ShouldBe(Money.Zero);
    }

    [Fact]
    public void D_19_breaching_transaction_needs_a_manager_and_the_claim_override()
    {
        var c = NewComponent(ReserveComponentType.Expense);
        var txn = Add(c, 500m, gross: 9_999_800m).Transaction;
        var grossNow = Money.Of(9_999_800m);

        Should.Throw<DomainRuleViolationException>(() => c.Approve(txn.Id, Supervisor, grossNow, true, Now))
            .Violations.ShouldHaveSingleItem().Code.ShouldBe("RESERVE_AGGREGATE_LIMIT");
        Should.Throw<DomainRuleViolationException>(() => c.Approve(txn.Id, Manager, grossNow, false, Now))
            .Violations.ShouldHaveSingleItem().Code.ShouldBe("RESERVE_AGGREGATE_LIMIT");

        c.Approve(txn.Id, Manager, grossNow, true, Now);
        txn.ApprovalStatus.ShouldBe(ApprovalStatus.Approved);
    }

    [Fact]
    public void D_19_limit_is_rechecked_at_approval_against_the_current_approved_total()
    {
        var c = NewComponent();
        // At submission the total is 9.9M, so 9.95M stays within the limit and a Supervisor suffices.
        var txn = Add(c, 50_000m, gross: 9_900_000m).Transaction;
        txn.RequiresLimitOverride.ShouldBeFalse();

        // Other components were approved in the meantime: the total is now 9.97M, so +50k breaches.
        var grossNow = Money.Of(9_970_000m);

        Should.Throw<DomainRuleViolationException>(() => c.Approve(txn.Id, Supervisor, grossNow, false, Now))
            .Violations.ShouldHaveSingleItem().Code.ShouldBe("RESERVE_AGGREGATE_LIMIT");

        c.Approve(txn.Id, Manager, grossNow, true, Now);
        c.CurrentAmount.ShouldBe(Money.Of(50_000m));
    }

    [Fact]
    public void D_19_breach_flagged_at_submission_but_resolved_by_approval_time_needs_no_override()
    {
        var c = NewComponent();
        var txn = Add(c, 50_000m, gross: 9_990_000m).Transaction;
        txn.RequiresLimitOverride.ShouldBeTrue();

        // Other reserves were reduced in the meantime.
        c.Approve(txn.Id, Supervisor, Money.Of(5_000_000m), false, Now);

        txn.ApprovalStatus.ShouldBe(ApprovalStatus.Approved);
    }

    // ------------------------------------------------------------------ reject / retract (BR-R-04, S6.4)

    [Fact]
    public void BR_R_04_rejected_transaction_stays_in_history_and_a_revised_one_can_be_submitted()
    {
        var c = NewComponent();
        var rejected = Add(c, 60_000m).Transaction;
        c.ClearDomainEvents();

        c.Reject(rejected.Id, Supervisor, "  Estimate not supported by the adjuster report.  ", Now);

        rejected.ApprovalStatus.ShouldBe(ApprovalStatus.Rejected);
        rejected.RejectedByUserId.ShouldBe(Supervisor.UserId);
        rejected.RejectionReason.ShouldBe("Estimate not supported by the adjuster report.");
        rejected.PostingStatus.ShouldBe(PostingStatus.Cancelled);
        c.CurrentAmount.ShouldBe(Money.Zero);
        c.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ReserveTransactionRejected>().Reason
            .ShouldBe("Estimate not supported by the adjuster report.");

        var revised = Add(c, 40_000m).Transaction;

        c.Transactions.Count.ShouldBe(2);
        revised.Id.ShouldNotBe(rejected.Id);
        revised.IsPendingApproval.ShouldBeTrue();
    }

    [Fact]
    public void Reject_requires_a_reason_and_authority_for_the_amount()
    {
        var c = NewComponent();
        var txn = Add(c, 150_000m).Transaction;

        Should.Throw<DomainForbiddenException>(() => c.Reject(txn.Id, Handler2, "no", Now));
        Should.Throw<DomainRuleViolationException>(() => c.Reject(txn.Id, Supervisor, "Too high", Now))
            .Violations.ShouldHaveSingleItem().Code.ShouldBe("RESERVE_INSUFFICIENT_AUTHORITY");
        Should.Throw<DomainRuleViolationException>(() => c.Reject(txn.Id, Manager, " ", Now))
            .Violations.ShouldHaveSingleItem().Code.ShouldBe("REJECTION_REASON_REQUIRED");
    }

    [Fact]
    public void S6_4_submitter_retracts_a_pending_transaction_then_can_submit_a_new_one()
    {
        var c = NewComponent();
        var txn = Add(c, 60_000m).Transaction;

        Should.Throw<DomainForbiddenException>(() => c.Retract(txn.Id, Handler2, Now))
            .Code.ShouldBe("RESERVE_RETRACT_NOT_SUBMITTER");

        c.Retract(txn.Id, Handler1, Now);

        txn.ApprovalStatus.ShouldBe(ApprovalStatus.Cancelled);
        txn.PostingStatus.ShouldBe(PostingStatus.Cancelled);
        c.DomainEvents.OfType<ReserveTransactionRetracted>().ShouldHaveSingleItem();
        Should.NotThrow(() => Add(c, 55_000m));
    }

    // ------------------------------------------------------------------ GL posting (S12.1, D-28)

    [Fact]
    public void Mark_posted_is_idempotent()
    {
        var c = NewComponent();
        var txn = Add(c, 1_000m).Transaction;
        c.ClearDomainEvents();

        c.MarkPosted(txn.Id, "job-1", Now).ShouldBeTrue();
        c.MarkPosted(txn.Id, "job-2", Now).ShouldBeFalse();

        txn.PostingStatus.ShouldBe(PostingStatus.Posted);
        txn.PostingJobId.ShouldBe("job-1");
        c.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ReservePostingCompleted>();
    }

    [Fact]
    public void Pending_transactions_are_never_posted()
    {
        var c = NewComponent();
        var txn = Add(c, 50_000m).Transaction;

        Should.Throw<DomainRuleViolationException>(() => c.MarkPosted(txn.Id, "job-1", Now))
            .Violations.ShouldHaveSingleItem().Code.ShouldBe("RESERVE_NOT_APPROVED");
    }

    [Fact]
    public void Failed_posting_can_be_retried_by_a_supervisor_and_then_posted()
    {
        var c = NewComponent();
        var txn = Add(c, 1_000m).Transaction;

        c.MarkPostingFailed(txn.Id, "GL endpoint timeout", Now).ShouldBeTrue();
        txn.PostingStatus.ShouldBe(PostingStatus.Failed);

        Should.Throw<DomainForbiddenException>(() => c.RequestPostingRetry(txn.Id, Handler1, Now));
        c.RequestPostingRetry(txn.Id, Supervisor, Now);
        txn.PostingStatus.ShouldBe(PostingStatus.Pending);
        c.DomainEvents.OfType<ReservePostingRetryRequested>().ShouldHaveSingleItem().IdempotencyKey.ShouldBe(txn.IdempotencyKey);

        c.MarkPosted(txn.Id, "job-2", Now).ShouldBeTrue();
        txn.PostingFailureReason.ShouldBeNull();
    }

    [Fact]
    public void Failure_after_posting_is_ignored_and_retry_requires_failed_status()
    {
        var c = NewComponent();
        var txn = Add(c, 1_000m).Transaction;
        c.MarkPosted(txn.Id, "job-1", Now);

        c.MarkPostingFailed(txn.Id, "late failure", Now).ShouldBeFalse();
        txn.PostingStatus.ShouldBe(PostingStatus.Posted);
        Should.Throw<DomainRuleViolationException>(() => c.RequestPostingRetry(txn.Id, Supervisor, Now))
            .Violations.ShouldHaveSingleItem().Code.ShouldBe("POSTING_NOT_FAILED");
    }

    // ------------------------------------------------------------------ lifecycle (D-21)

    [Fact]
    public void Closed_component_rejects_submissions_until_reactivated()
    {
        var c = Opened(5_000m);

        c.Close();

        Should.Throw<DomainRuleViolationException>(() => Adjust(c, 6_000m))
            .Violations.ShouldHaveSingleItem().Code.ShouldBe("RESERVE_COMPONENT_CLOSED");
        c.CurrentAmount.ShouldBe(Money.Of(5_000m));

        c.Reactivate();
        Should.NotThrow(() => Adjust(c, 6_000m));
    }

    [Fact]
    public void Component_with_a_pending_transaction_cannot_be_closed()
    {
        var c = NewComponent();
        Add(c, 50_000m);

        Should.Throw<DomainRuleViolationException>(c.Close).Violations.ShouldHaveSingleItem().Code.ShouldBe("RESERVE_PENDING_EXISTS");
    }
}
