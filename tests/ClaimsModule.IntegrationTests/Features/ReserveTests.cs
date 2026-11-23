using ClaimsModule.Application.Claims;
using ClaimsModule.Application.Common.Exceptions;
using ClaimsModule.Application.Reserves;
using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Common;
using ClaimsModule.Domain.Reserves;
using ClaimsModule.IntegrationTests.Persistence;

namespace ClaimsModule.IntegrationTests.Features;

/// <summary>Tasks 25 and 25a: reserve commands and queries, one test per BR-R rule plus the claim-level preconditions.</summary>
[Collection(SqlServerTestGroup.Name)]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA1001", Justification = "_host is disposed in IAsyncLifetime.DisposeAsync, which xUnit calls after every test.")]
public sealed class ReserveTests(SqlServerDatabaseFixture db) : IAsyncLifetime
{
    private static readonly Actor Handler = ApplicationHost.Handler1;
    private static readonly Actor Supervisor = ApplicationHost.Supervisor1;
    private static readonly Actor Manager = ApplicationHost.Manager1;

    private ApplicationHost _host = null!;

    public Task InitializeAsync()
    {
        _host = new ApplicationHost(db);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private DateTimeOffset Now => _host.Clock.Now;

    private async Task<Guid> OpenClaimAsync(Guid? policyId = null, bool withPolicy = true)
    {
        var created = await _host.SendAsync(Fnol.Complete(Now) with { PolicyId = withPolicy ? policyId ?? Fnol.MeridianPolicyId : null });
        if (withPolicy)
        {
            await _host.SendAsync(new TransitionClaimStatusCommand(created.ClaimId, ClaimStatus.Open));
        }

        return created.ClaimId;
    }

    private Task<ReserveSubmissionDto> SubmitAsync(Guid claimId, decimal? amount, ReserveTransactionType type = ReserveTransactionType.Add,
        ReserveComponentType component = ReserveComponentType.Indemnity, Actor? actor = null) =>
        _host.SendAsync(new SubmitReserveTransactionCommand(claimId, component, type, amount, "Adjuster estimate"), actor ?? Handler);

    private Task<ReserveTransactionStateDto> ApproveAsync(Guid claimId, Guid transactionId, Actor approver) =>
        _host.SendAsync(new ApproveReserveTransactionCommand(claimId, transactionId), approver);

    private Task<ClaimReservesDto> ReservesAsync(Guid claimId) => _host.SendAsync(new GetClaimReservesQuery(claimId));

    // ------------------------------------------------------------------ BR-R-02 authority thresholds (S6.3)

    [Theory]
    [InlineData("10000", ApprovalStatus.AutoApproved, ApprovalLevel.Auto)]
    [InlineData("10000.01", ApprovalStatus.PendingApproval, ApprovalLevel.Supervisor)]
    [InlineData("100000", ApprovalStatus.PendingApproval, ApprovalLevel.Supervisor)]
    [InlineData("100000.01", ApprovalStatus.PendingApproval, ApprovalLevel.Manager)]
    public async Task Authority_level_follows_the_thresholds(string amount, ApprovalStatus status, ApprovalLevel level)
    {
        var claimId = await OpenClaimAsync();

        var result = await SubmitAsync(claimId, decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture));

        result.ApprovalStatus.ShouldBe(status);
        result.RequiredApprovalLevel.ShouldBe(level);
    }

    [Fact]
    public async Task Supervisor_cannot_approve_above_100k_but_a_manager_can()
    {
        var claimId = await OpenClaimAsync();
        var txn = await SubmitAsync(claimId, 150_000m);

        var error = await Should.ThrowAsync<DomainRuleViolationException>(() => ApproveAsync(claimId, txn.TransactionId, Supervisor));
        error.Violations.Single().Message.ShouldBe("Your role does not have authority to approve this reserve amount.");

        var approved = await ApproveAsync(claimId, txn.TransactionId, Manager);
        approved.ApprovalStatus.ShouldBe(ApprovalStatus.Approved);
        approved.ComponentBalance.ShouldBe(150_000m);
    }

    [Fact]
    public async Task Handler_approval_is_403()
    {
        var claimId = await OpenClaimAsync();
        var txn = await SubmitAsync(claimId, 50_000m);

        (await Should.ThrowAsync<ForbiddenAccessException>(() => ApproveAsync(claimId, txn.TransactionId, ApplicationHost.Handler2)))
            .RequiredRole.ShouldBe(UserRole.Supervisor); // D-24
    }

    // ------------------------------------------------------------------ BR-R-03 self-approval

    [Fact]
    public async Task Self_approval_is_not_permitted()
    {
        var claimId = await OpenClaimAsync();
        var txn = await SubmitAsync(claimId, 50_000m, actor: Supervisor);

        var error = await Should.ThrowAsync<DomainRuleViolationException>(() => ApproveAsync(claimId, txn.TransactionId, Supervisor));
        error.Violations.Single().Message.ShouldBe("Self-approval is not permitted.");

        (await ApproveAsync(claimId, txn.TransactionId, ApplicationHost.Supervisor2)).ApprovalStatus.ShouldBe(ApprovalStatus.Approved);
    }

    // ------------------------------------------------------------------ BR-R-04 reject with reason, resubmit

    [Fact]
    public async Task Rejection_needs_a_reason_and_the_submitter_can_resubmit()
    {
        var claimId = await OpenClaimAsync();
        var txn = await SubmitAsync(claimId, 50_000m);

        (await Should.ThrowAsync<RequestValidationException>(() =>
            _host.SendAsync(new RejectReserveTransactionCommand(claimId, txn.TransactionId, ""), Supervisor)))
            .Violations.Single().Code.ShouldBe("REJECTION_REASON_REQUIRED");

        var rejected = await _host.SendAsync(new RejectReserveTransactionCommand(claimId, txn.TransactionId, "Estimate not supported by photos"), Supervisor);
        rejected.ApprovalStatus.ShouldBe(ApprovalStatus.Rejected);

        var resubmitted = await SubmitAsync(claimId, 30_000m);
        resubmitted.TransactionId.ShouldNotBe(txn.TransactionId);

        var history = (await ReservesAsync(claimId)).History;
        history.Count.ShouldBe(2); // the rejected record stays in history
        history.Single(h => h.TransactionId == txn.TransactionId).RejectionReason.ShouldBe("Estimate not supported by photos");
    }

    // ------------------------------------------------------------------ retract, one pending (S6.4, D-17)

    [Fact]
    public async Task Only_the_submitter_retracts_and_only_one_transaction_may_be_pending()
    {
        var claimId = await OpenClaimAsync();
        var txn = await SubmitAsync(claimId, 50_000m);

        (await Should.ThrowAsync<DomainRuleViolationException>(() => SubmitAsync(claimId, 60_000m)))
            .Contains("RESERVE_PENDING_EXISTS").ShouldBeTrue();
        await Should.ThrowAsync<DomainForbiddenException>(() =>
            _host.SendAsync(new RetractReserveTransactionCommand(claimId, txn.TransactionId), ApplicationHost.Handler2));

        var retracted = await _host.SendAsync(new RetractReserveTransactionCommand(claimId, txn.TransactionId), Handler);
        retracted.ApprovalStatus.ShouldBe(ApprovalStatus.Cancelled);
        retracted.PostingStatus.ShouldBe(PostingStatus.Cancelled);
    }

    // ------------------------------------------------------------------ BR-R-01 sign, D-16 adjust/reverse semantics

    [Fact]
    public async Task Adjust_sets_a_target_balance_and_reverse_brings_it_to_zero()
    {
        var claimId = await OpenClaimAsync();
        await SubmitAsync(claimId, 8_000m);

        var down = await SubmitAsync(claimId, 5_000m, ReserveTransactionType.Adjust);
        down.Amount.ShouldBe(-3_000m); // signed delta stored
        down.NewBalance.ShouldBe(5_000m);

        var reversed = await SubmitAsync(claimId, null, ReserveTransactionType.Reverse);
        reversed.NewBalance.ShouldBe(0m);

        (await Should.ThrowAsync<RequestValidationException>(() => SubmitAsync(claimId, -100m, ReserveTransactionType.Adjust)))
            .Violations.Single().Code.ShouldBe("RESERVE_AMOUNT_NOT_POSITIVE");
        (await Should.ThrowAsync<DomainRuleViolationException>(() => SubmitAsync(claimId, 1_000m, component: ReserveComponentType.Expense, type: ReserveTransactionType.Adjust)))
            .Contains("RESERVE_NOT_OPEN").ShouldBeTrue();
    }

    [Fact]
    public async Task Subrogation_recoverable_may_go_negative()
    {
        var claimId = await OpenClaimAsync();

        var result = await SubmitAsync(claimId, -4_000m, component: ReserveComponentType.SubrogationRecoverable);

        result.NewBalance.ShouldBe(-4_000m);
        result.ApprovalStatus.ShouldBe(ApprovalStatus.AutoApproved);
    }

    // ------------------------------------------------------------------ BR-R-05 / D-19 aggregate $10M limit

    [Fact]
    public async Task Exceeding_ten_million_needs_a_manager_with_the_override_flag()
    {
        var claimId = await OpenClaimAsync();

        var big = await SubmitAsync(claimId, 10_000_000.01m);
        big.RequiresLimitOverride.ShouldBeTrue();
        big.Warnings.Single().Message.ShouldBe("Total reserves will exceed $10,000,000. Manager override required.");

        (await Should.ThrowAsync<DomainRuleViolationException>(() => ApproveAsync(claimId, big.TransactionId, Manager)))
            .Contains("RESERVE_AGGREGATE_LIMIT").ShouldBeTrue();
        await Should.ThrowAsync<ForbiddenAccessException>(() =>
            _host.SendAsync(new SetReserveLimitOverrideCommand(claimId, true), Supervisor));

        await _host.SendAsync(new SetReserveLimitOverrideCommand(claimId, true), Manager);

        (await ApproveAsync(claimId, big.TransactionId, Manager)).ComponentBalance.ShouldBe(10_000_000.01m);
    }

    // ------------------------------------------------------------------ task 25a: claim-level preconditions in every handler

    [Fact]
    public async Task Reserves_need_a_linked_policy()
    {
        var claimId = await OpenClaimAsync(withPolicy: false); // stays Draft, NO_POLICY warning

        (await Should.ThrowAsync<DomainRuleViolationException>(() => SubmitAsync(claimId, 1_000m)))
            .Contains("RESERVES_REQUIRE_POLICY").ShouldBeTrue();
    }

    [Theory]
    [InlineData(ClaimStatus.Draft)]
    [InlineData(ClaimStatus.Open)]
    [InlineData(ClaimStatus.UnderInvestigation)]
    [InlineData(ClaimStatus.PendingPayment)]
    public async Task Reserves_are_allowed_in_active_statuses(ClaimStatus status)
    {
        var created = await _host.SendAsync(Fnol.WithReserve(Now, 1_000m));
        foreach (var step in status switch
        {
            ClaimStatus.Draft => Array.Empty<ClaimStatus>(),
            ClaimStatus.Open => [ClaimStatus.Open],
            ClaimStatus.UnderInvestigation => [ClaimStatus.Open, ClaimStatus.UnderInvestigation],
            _ => [ClaimStatus.Open, ClaimStatus.PendingPayment],
        })
        {
            await _host.SendAsync(new TransitionClaimStatusCommand(created.ClaimId, step));
        }

        (await SubmitAsync(created.ClaimId, 2_000m, ReserveTransactionType.Adjust)).NewBalance.ShouldBe(2_000m);
    }

    [Fact]
    public async Task Every_reserve_command_is_blocked_on_a_withdrawn_claim()
    {
        var claimId = await OpenClaimAsync();
        var pending = await SubmitAsync(claimId, 50_000m);
        await _host.SendAsync(new TransitionClaimStatusCommand(claimId, ClaimStatus.Withdrawn) { Reason = "Withdrawn by the insured" });

        foreach (var command in new Func<Task>[]
        {
            () => SubmitAsync(claimId, 1_000m, component: ReserveComponentType.Expense),
            () => ApproveAsync(claimId, pending.TransactionId, Supervisor),
            () => _host.SendAsync(new RejectReserveTransactionCommand(claimId, pending.TransactionId, "No longer needed"), Supervisor),
            () => _host.SendAsync(new RetractReserveTransactionCommand(claimId, pending.TransactionId), Handler),
            () => _host.SendAsync(new RetryGlPostingCommand(claimId, pending.TransactionId), Supervisor),
        })
        {
            (await Should.ThrowAsync<DomainRuleViolationException>(command)).Contains("RESERVES_NOT_ALLOWED_IN_STATUS").ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Reserve_commands_record_activity_on_the_claim()
    {
        var claimId = await OpenClaimAsync();
        var txn = await SubmitAsync(claimId, 50_000m);
        _host.Clock.Now += TimeSpan.FromMinutes(5);

        await ApproveAsync(claimId, txn.TransactionId, Supervisor);

        var stamped = await _host.InScopeAsync(sp => db.ScalarAsync<DateTimeOffset>($"SELECT LastReserveActivityAt FROM Claims WHERE ClaimId = '{claimId}'"));
        stamped.ShouldBe(Now);
    }

    // ------------------------------------------------------------------ query

    [Fact]
    public async Task Reserves_query_returns_totals_and_newest_first_history()
    {
        var claimId = await OpenClaimAsync();
        await SubmitAsync(claimId, 8_000m);
        _host.Clock.Now += TimeSpan.FromMinutes(1);
        await SubmitAsync(claimId, 2_500m, component: ReserveComponentType.Expense);

        var reserves = await ReservesAsync(claimId);

        reserves.Totals.ApprovedGross.ShouldBe(10_500m);
        reserves.Totals.Components.Count.ShouldBe(2);
        reserves.History.Select(h => h.ComponentType).ShouldBe([ReserveComponentType.Expense, ReserveComponentType.Indemnity]);
        reserves.History[0].IdempotencyKey.ShouldStartWith("Reserve:");
        reserves.History[0].PostingStatus.ShouldBe(PostingStatus.Pending);
        await Should.ThrowAsync<NotFoundException>(() => ReservesAsync(Guid.NewGuid()));
    }
}
