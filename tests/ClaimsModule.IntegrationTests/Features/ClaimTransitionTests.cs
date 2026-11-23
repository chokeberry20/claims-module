using ClaimsModule.Application.Claims;
using ClaimsModule.Application.Common.Exceptions;
using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Claims.Validation;
using ClaimsModule.Domain.Common;
using ClaimsModule.Domain.Reserves;
using ClaimsModule.IntegrationTests.Persistence;

namespace ClaimsModule.IntegrationTests.Features;

/// <summary>Tasks 23 and 23a: every S4.2 transition row, its 422 bodies, and the database transitions table.</summary>
[Collection(SqlServerTestGroup.Name)]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA1001", Justification = "_host is disposed in IAsyncLifetime.DisposeAsync, which xUnit calls after every test.")]
public sealed class ClaimTransitionTests(SqlServerDatabaseFixture db) : IAsyncLifetime
{
    private ApplicationHost _host = null!;

    public Task InitializeAsync()
    {
        _host = new ApplicationHost(db);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private DateTimeOffset Now => _host.Clock.Now;

    private Task<ClaimChangeResult> MoveAsync(Guid claimId, ClaimStatus target, Actor? actor = null, string? reason = null, string? justification = null, bool acknowledge = false) =>
        _host.SendAsync(new TransitionClaimStatusCommand(claimId, target) { Reason = reason, ClosureJustification = justification, AcknowledgeWarnings = acknowledge }, actor);

    private async Task<Guid> OpenClaimAsync(CreateClaimCommand? command = null)
    {
        var created = await _host.SendAsync(command ?? Fnol.Complete(Now));
        await MoveAsync(created.ClaimId, ClaimStatus.Open);
        return created.ClaimId;
    }

    private static async Task<DomainRuleViolationException> Rejected(Func<Task> action) =>
        await Should.ThrowAsync<DomainRuleViolationException>(action);

    // ------------------------------------------------------------------ Draft → Open (BR-ST-02, BR-C-02, D-07)

    [Fact]
    public async Task Complete_draft_opens()
    {
        var created = await _host.SendAsync(Fnol.Complete(Now));

        (await MoveAsync(created.ClaimId, ClaimStatus.Open)).Status.ShouldBe(ClaimStatus.Open);
    }

    [Fact]
    public async Task Draft_without_claimant_cannot_open()
    {
        var created = await _host.SendAsync(Fnol.Complete(Now) with { Parties = [] });

        (await Rejected(() => MoveAsync(created.ClaimId, ClaimStatus.Open))).Contains(ValidationIssueCodes.NoClaimant).ShouldBeTrue();
    }

    [Fact]
    public async Task Open_warnings_need_acknowledgement_which_the_transition_can_give()
    {
        var created = await _host.SendAsync(Fnol.Complete(Now) with { RiskObjects = [] });

        (await Rejected(() => MoveAsync(created.ClaimId, ClaimStatus.Open))).Contains("WARNINGS_NOT_ACKNOWLEDGED").ShouldBeTrue();

        var opened = await MoveAsync(created.ClaimId, ClaimStatus.Open, acknowledge: true);
        opened.Status.ShouldBe(ClaimStatus.Open);
        opened.ValidationIssues.Single().Status.ShouldBe(ValidationIssueStatus.Acknowledged);
    }

    [Fact]
    public async Task Draft_cannot_go_straight_to_closed_and_the_422_lists_valid_next_statuses()
    {
        var created = await _host.SendAsync(Fnol.Complete(Now));

        var error = await Rejected(() => MoveAsync(created.ClaimId, ClaimStatus.Closed));

        var violation = error.Violations.Single();
        violation.Code.ShouldBe("INVALID_TRANSITION");
        violation.Message.ShouldBe("Transition from Draft to Closed is not permitted. Valid next statuses: Open.");
        violation.Field.ShouldBe("StatusTransition");
    }

    // ------------------------------------------------------------------ Open ↔ UnderInvestigation, PendingPayment

    [Fact]
    public async Task Open_to_under_investigation_and_back()
    {
        var claimId = await OpenClaimAsync();

        (await MoveAsync(claimId, ClaimStatus.UnderInvestigation)).Status.ShouldBe(ClaimStatus.UnderInvestigation);
        (await MoveAsync(claimId, ClaimStatus.Open)).Status.ShouldBe(ClaimStatus.Open);
    }

    [Fact]
    public async Task Pending_payment_needs_an_approved_reserve()
    {
        var withoutReserve = await OpenClaimAsync();
        (await Rejected(() => MoveAsync(withoutReserve, ClaimStatus.PendingPayment))).Contains("NO_APPROVED_RESERVE").ShouldBeTrue();

        var withReserve = await OpenClaimAsync(Fnol.WithReserve(Now, 8_000m)); // auto-approved
        (await MoveAsync(withReserve, ClaimStatus.PendingPayment)).Status.ShouldBe(ClaimStatus.PendingPayment);
        (await MoveAsync(withReserve, ClaimStatus.Closed, justification: "Payment issued in full.")).Status.ShouldBe(ClaimStatus.Closed);
    }

    // ------------------------------------------------------------------ closing (S4.3 CC-01..CC-04, D-21)

    [Fact]
    public async Task Closing_with_a_pending_reserve_is_blocked_by_cc01()
    {
        var claimId = await OpenClaimAsync(Fnol.WithReserve(Now, 50_000m)); // pending approval

        var error = await Rejected(() => MoveAsync(claimId, ClaimStatus.Closed, justification: "Done."));

        error.Contains("CC-01").ShouldBeTrue();
    }

    [Fact]
    public async Task Closing_with_open_balances_needs_a_justification_and_closes_the_components()
    {
        var claimId = await OpenClaimAsync(Fnol.WithReserve(Now, 8_000m));

        var error = await Rejected(() => MoveAsync(claimId, ClaimStatus.Closed));
        error.Violations.Single().Code.ShouldBe("CC-04");
        error.Violations.Single().Field.ShouldBe("ClosureJustification");

        (await MoveAsync(claimId, ClaimStatus.Closed, justification: "Remaining reserve held for subrogation.")).Status.ShouldBe(ClaimStatus.Closed);
        var detail = await _host.SendAsync(new GetClaimQuery(claimId));
        detail.ClosedAt.ShouldBe(Now);
        detail.Reserves.Components.Single().Status.ShouldBe(ReserveComponentStatus.Closed);
        detail.Reserves.Components.Single().CurrentAmount.ShouldBe(8_000m); // balances unchanged (D-21)
    }

    [Fact]
    public async Task Claim_without_reserves_closes_without_justification()
    {
        var claimId = await OpenClaimAsync();

        (await MoveAsync(claimId, ClaimStatus.Closed)).Status.ShouldBe(ClaimStatus.Closed);
    }

    // ------------------------------------------------------------------ withdraw and reopen (S4.2, BR-ST-04, D-12)

    [Fact]
    public async Task Withdrawal_needs_a_reason()
    {
        var claimId = await OpenClaimAsync();

        (await Rejected(() => MoveAsync(claimId, ClaimStatus.Withdrawn))).Contains("REASON_REQUIRED").ShouldBeTrue();
        (await MoveAsync(claimId, ClaimStatus.Withdrawn, reason: "Insured withdrew the claim by phone.")).Status.ShouldBe(ClaimStatus.Withdrawn);
        (await Rejected(() => MoveAsync(claimId, ClaimStatus.Open))).Violations.Single().Message
            .ShouldBe("Transition from Withdrawn to Open is not permitted. Valid next statuses: none.");
    }

    [Fact]
    public async Task Reopen_needs_a_supervisor_and_a_reason_and_lands_in_open_with_reactivated_reserves()
    {
        var claimId = await OpenClaimAsync(Fnol.WithReserve(Now, 8_000m));
        await MoveAsync(claimId, ClaimStatus.Closed, justification: "Held for recovery.");

        await Should.ThrowAsync<ForbiddenAccessException>(() =>
            MoveAsync(claimId, ClaimStatus.Reopened, ApplicationHost.Handler1, reason: "New invoice"));
        (await Rejected(() => MoveAsync(claimId, ClaimStatus.Reopened, ApplicationHost.Supervisor1))).Contains("REASON_REQUIRED").ShouldBeTrue();

        var reopened = await MoveAsync(claimId, ClaimStatus.Reopened, ApplicationHost.Supervisor1, reason: "Late invoice received from the body shop.");

        reopened.Status.ShouldBe(ClaimStatus.Open);
        var detail = await _host.SendAsync(new GetClaimQuery(claimId));
        detail.ReopenCount.ShouldBe(1);
        detail.ClosedAt.ShouldBeNull();
        detail.Reserves.Components.Single().Status.ShouldBe(ReserveComponentStatus.Active);
    }

    // ------------------------------------------------------------------ task 23a: the database table is authoritative

    [Fact]
    public async Task A_pair_missing_from_the_transitions_table_is_rejected_even_though_the_domain_allows_it()
    {
        var claimId = await OpenClaimAsync();
        // Move the Open → UnderInvestigation row out of the way (unique (From, To) stays intact), then restore it.
        await db.ExecuteAsync("UPDATE ClaimStatusTransitions SET FromStatus = 'Withdrawn' WHERE FromStatus = 'Open' AND ToStatus = 'UnderInvestigation'");
        try
        {
            var error = await Rejected(() => MoveAsync(claimId, ClaimStatus.UnderInvestigation));

            error.Violations.Single().Code.ShouldBe("INVALID_TRANSITION");
            error.Violations.Single().Message.ShouldNotContain("UnderInvestigation,");
            (await _host.SendAsync(new GetAvailableTransitionsQuery(claimId))).Select(o => o.TargetStatus)
                .ShouldNotContain(ClaimStatus.UnderInvestigation);
        }
        finally
        {
            await db.ExecuteAsync("UPDATE ClaimStatusTransitions SET FromStatus = 'Open' WHERE FromStatus = 'Withdrawn' AND ToStatus = 'UnderInvestigation'");
        }

        (await MoveAsync(claimId, ClaimStatus.UnderInvestigation)).Status.ShouldBe(ClaimStatus.UnderInvestigation);
    }

    // ------------------------------------------------------------------ pre-flight (D-27)

    [Fact]
    public async Task Available_transitions_show_blocking_conditions_for_the_current_user()
    {
        var draft = await _host.SendAsync(Fnol.Complete(Now) with { Parties = [] });
        var options = await _host.SendAsync(new GetAvailableTransitionsQuery(draft.ClaimId));
        var open = options.Single();
        open.TargetStatus.ShouldBe(ClaimStatus.Open);
        open.IsAllowed.ShouldBeFalse();
        open.BlockingConditions.Select(b => b.Code).ShouldContain(ValidationIssueCodes.NoClaimant);

        var claimId = await OpenClaimAsync(Fnol.WithReserve(Now, 8_000m));
        var fromOpen = (await _host.SendAsync(new GetAvailableTransitionsQuery(claimId))).ToDictionary(o => o.TargetStatus);
        fromOpen.Keys.ShouldBe([ClaimStatus.UnderInvestigation, ClaimStatus.PendingPayment, ClaimStatus.Closed, ClaimStatus.Withdrawn], ignoreOrder: true);
        fromOpen[ClaimStatus.Closed].IsAllowed.ShouldBeTrue();
        fromOpen[ClaimStatus.Closed].CanProceed.ShouldBeFalse(); // CC-04 needs a justification
        fromOpen[ClaimStatus.Closed].ConfirmationsRequired.Single().Code.ShouldBe("CC-04");
        fromOpen[ClaimStatus.Withdrawn].RequiresReason.ShouldBeTrue();
    }
}
