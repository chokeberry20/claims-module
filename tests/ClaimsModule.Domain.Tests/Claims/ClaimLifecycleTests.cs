using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Claims.Events;
using ClaimsModule.Domain.Claims.Validation;
using ClaimsModule.Domain.Claims.Workflow;
using ClaimsModule.Domain.Common;
using static ClaimsModule.Domain.Tests.Claims.ClaimFactory;
using static ClaimsModule.Domain.Tests.TestData;

namespace ClaimsModule.Domain.Tests.Claims;

public sealed class ClaimLifecycleTests
{
    // ------------------------------------------------------------------ Draft → Open (BR-ST-02)

    [Fact]
    public void Complete_draft_opens_and_raises_STATUS_CHANGED()
    {
        var claim = NewCompleteDraft();
        claim.ClearDomainEvents();

        claim.TransitionTo(ClaimStatus.Open, TransitionRequest.None, ReserveFacts.None, Handler1, Now);

        claim.Status.ShouldBe(ClaimStatus.Open);
        var changed = claim.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ClaimStatusChanged>();
        (changed.FromStatus, changed.ToStatus).ShouldBe((ClaimStatus.Draft, ClaimStatus.Open));
    }

    [Fact]
    public void BR_ST_02b_open_requires_an_active_claimant_even_if_no_issue_is_recorded()
    {
        var claim = NewDraft();

        var ex = Should.Throw<DomainRuleViolationException>(() =>
            claim.TransitionTo(ClaimStatus.Open, TransitionRequest.None, ReserveFacts.None, Handler1, Now));

        var violation = ex.Violations.ShouldHaveSingleItem();
        violation.Code.ShouldBe(ValidationIssueCodes.NoClaimant);
        violation.Message.ShouldBe("At least one Claimant party is required to open a claim.");
        claim.Status.ShouldBe(ClaimStatus.Draft);
    }

    [Fact]
    public void No_claimant_is_reported_once_even_when_also_recorded_as_an_issue()
    {
        var claim = NewDraft();
        claim.ApplyValidationFindings([Critical(ValidationIssueCodes.NoClaimant)], Now);

        Should.Throw<DomainRuleViolationException>(() =>
                claim.TransitionTo(ClaimStatus.Open, TransitionRequest.None, ReserveFacts.None, Handler1, Now))
            .Violations.ShouldHaveSingleItem().Code.ShouldBe(ValidationIssueCodes.NoClaimant);
    }

    [Fact]
    public void BR_ST_02a_open_critical_issue_blocks_open_and_a_waiver_unblocks_it()
    {
        var claim = NewCompleteDraft();
        claim.ApplyValidationFindings([Critical(ValidationIssueCodes.CauseOfLossInactive)], Now);

        Should.Throw<DomainRuleViolationException>(() =>
                claim.TransitionTo(ClaimStatus.Open, TransitionRequest.None, ReserveFacts.None, Handler1, Now))
            .Violations.ShouldHaveSingleItem().Code.ShouldBe("OPEN_CRITICAL_ISSUES");

        claim.WaiveValidationIssue(claim.ValidationIssues.Single().Id, "Code retired after intake.", Supervisor, Now);
        claim.TransitionTo(ClaimStatus.Open, TransitionRequest.None, ReserveFacts.None, Handler1, Now);

        claim.Status.ShouldBe(ClaimStatus.Open);
    }

    [Fact]
    public void BR_C_02_unacknowledged_warnings_block_open_until_acknowledged_in_the_request()
    {
        var claim = NewCompleteDraft();
        claim.ApplyValidationFindings([Warning(ValidationIssueCodes.LossDateOutsidePolicyPeriod)], Now);

        var ex = Should.Throw<DomainRuleViolationException>(() =>
            claim.TransitionTo(ClaimStatus.Open, TransitionRequest.None, ReserveFacts.None, Handler1, Now));
        ex.Violations.ShouldHaveSingleItem().Code.ShouldBe("WARNINGS_NOT_ACKNOWLEDGED");

        claim.ClearDomainEvents();
        claim.TransitionTo(ClaimStatus.Open, new TransitionRequest(AcknowledgeWarnings: true), ReserveFacts.None, Handler1, Now);

        claim.Status.ShouldBe(ClaimStatus.Open);
        claim.ValidationIssues.Single().Status.ShouldBe(ValidationIssueStatus.Acknowledged);
        claim.DomainEvents.Select(e => e.GetType()).ShouldBe([typeof(ValidationWarningsAcknowledged), typeof(ClaimStatusChanged)]);
    }

    [Fact]
    public void Draft_to_open_reports_all_unmet_conditions_together()
    {
        var claim = NewDraft();
        claim.ApplyValidationFindings([Critical(ValidationIssueCodes.CauseOfLossInactive), Warning(ValidationIssueCodes.NoRiskObjects)], Now);

        Should.Throw<DomainRuleViolationException>(() =>
                claim.TransitionTo(ClaimStatus.Open, TransitionRequest.None, ReserveFacts.None, Handler1, Now))
            .Violations.Select(v => v.Code)
            .ShouldBe([ValidationIssueCodes.NoClaimant, "OPEN_CRITICAL_ISSUES", "WARNINGS_NOT_ACKNOWLEDGED"], ignoreOrder: true);
    }

    // ------------------------------------------------------------------ Open ⇄ UnderInvestigation, PendingPayment

    [Fact]
    public void Open_to_under_investigation_and_back()
    {
        var claim = ClaimScenarios.InStatus(ClaimStatus.Open);

        claim.TransitionTo(ClaimStatus.UnderInvestigation, TransitionRequest.None, ReserveFacts.None, Handler1, Now);
        claim.TransitionTo(ClaimStatus.Open, TransitionRequest.None, ReserveFacts.None, Handler1, Now);

        claim.Status.ShouldBe(ClaimStatus.Open);
        claim.DomainEvents.OfType<ClaimStatusChanged>().Count().ShouldBe(2);
    }

    [Theory]
    [InlineData(ClaimStatus.Open)]
    [InlineData(ClaimStatus.UnderInvestigation)]
    public void Pending_payment_requires_an_approved_reserve(ClaimStatus from)
    {
        var claim = ClaimScenarios.InStatus(from);

        Should.Throw<DomainRuleViolationException>(() =>
                claim.TransitionTo(ClaimStatus.PendingPayment, TransitionRequest.None, ReserveFacts.None, Handler1, Now))
            .Violations.ShouldHaveSingleItem().Code.ShouldBe("NO_APPROVED_RESERVE");

        claim.TransitionTo(ClaimStatus.PendingPayment, TransitionRequest.None, ClaimScenarios.ApprovedReserve, Handler1, Now);
        claim.Status.ShouldBe(ClaimStatus.PendingPayment);
    }

    // ------------------------------------------------------------------ Closed (S4.3, BR-ST-03)

    [Theory]
    [InlineData(ClaimStatus.Open)]
    [InlineData(ClaimStatus.UnderInvestigation)]
    [InlineData(ClaimStatus.PendingPayment)]
    public void Claim_closes_when_all_conditions_hold(ClaimStatus from)
    {
        var claim = ClaimScenarios.InStatus(from);

        claim.TransitionTo(ClaimStatus.Closed, new TransitionRequest("Settled in full."), ReserveFacts.None, Handler1, Now);

        claim.Status.ShouldBe(ClaimStatus.Closed);
        claim.ClosedAt.ShouldBe(Now);
        claim.ClosureReason.ShouldBe("Settled in full.");
        var closed = claim.DomainEvents.OfType<ClaimClosed>().ShouldHaveSingleItem();
        closed.ClosedWithOpenReserves.ShouldBeFalse();
    }

    [Fact]
    public void CC_01_pending_reserve_approval_blocks_closure()
    {
        var claim = ClaimScenarios.InStatus(ClaimStatus.Open);
        var facts = new ReserveFacts(HasApprovedTransaction: true, HasPendingApproval: true, HasOpenReserveBalance: false);

        var violation = Should.Throw<DomainRuleViolationException>(() =>
                claim.TransitionTo(ClaimStatus.Closed, TransitionRequest.None, facts, Handler1, Now))
            .Violations.ShouldHaveSingleItem();

        violation.Code.ShouldBe("CC-01");
        violation.Message.ShouldBe("Claim cannot be closed — CC-01 (no reserve transactions pending approval) is not satisfied.");
    }

    [Fact]
    public void CC_02_unresolved_critical_issue_blocks_closure_until_waived()
    {
        var claim = ClaimScenarios.InStatus(ClaimStatus.Open);
        claim.ApplyValidationFindings([Critical(ValidationIssueCodes.CauseOfLossInactive)], Now);

        Should.Throw<DomainRuleViolationException>(() =>
                claim.TransitionTo(ClaimStatus.Closed, TransitionRequest.None, ReserveFacts.None, Handler1, Now))
            .Violations.ShouldHaveSingleItem().Code.ShouldBe("CC-02");

        claim.WaiveValidationIssue(claim.ValidationIssues.Single(i => i.IsOpenCritical).Id, "Accepted.", Supervisor, Now);
        claim.TransitionTo(ClaimStatus.Closed, TransitionRequest.None, ReserveFacts.None, Handler1, Now);
        claim.Status.ShouldBe(ClaimStatus.Closed);
    }

    [Fact]
    public void CC_03_cannot_be_violated_because_an_open_claim_cannot_lose_its_last_claimant()
    {
        // CC-03 is checked defensively. In practice it can't fail: Open needs a Claimant
        // (BR-ST-02b), and the last active Claimant can't be removed (FR-30).
        var claim = ClaimScenarios.InStatus(ClaimStatus.Open);

        Should.Throw<DomainRuleViolationException>(() =>
            claim.RemoveParty(claim.ActiveClaimants.Single().Id, Handler1, Now));

        claim.EvaluateTransition(ClaimStatus.Closed, ReserveFacts.None, Handler1).BlockingConditions.ShouldBeEmpty();
    }

    [Fact]
    public void CC_04_open_reserve_balance_requires_a_justification_note()
    {
        var claim = ClaimScenarios.InStatus(ClaimStatus.Open);
        var facts = new ReserveFacts(HasApprovedTransaction: true, HasPendingApproval: false, HasOpenReserveBalance: true);

        var violation = Should.Throw<DomainRuleViolationException>(() =>
                claim.TransitionTo(ClaimStatus.Closed, TransitionRequest.None, facts, Handler1, Now))
            .Violations.ShouldHaveSingleItem();
        violation.Code.ShouldBe("CC-04");
        violation.Field.ShouldBe("ClosureJustification");

        claim.TransitionTo(ClaimStatus.Closed, new TransitionRequest(ClosureJustification: "Remaining reserve covers a known late invoice."), facts, Handler1, Now);

        var closed = claim.DomainEvents.OfType<ClaimClosed>().ShouldHaveSingleItem();
        closed.ClosedWithOpenReserves.ShouldBeTrue();
        closed.OpenReservesJustification.ShouldBe("Remaining reserve covers a known late invoice.");
        claim.ClosureReason.ShouldBe("Remaining reserve covers a known late invoice.");
    }

    [Fact]
    public void All_closure_failures_are_reported_together()
    {
        var claim = ClaimScenarios.InStatus(ClaimStatus.Open);
        claim.ApplyValidationFindings([Critical(ValidationIssueCodes.CauseOfLossInactive)], Now);
        var facts = new ReserveFacts(true, true, true);

        Should.Throw<DomainRuleViolationException>(() =>
                claim.TransitionTo(ClaimStatus.Closed, TransitionRequest.None, facts, Handler1, Now))
            .Violations.Select(v => v.Code).ShouldBe(["CC-01", "CC-02", "CC-04"], ignoreOrder: true);
    }

    [Fact]
    public void Closed_claim_rejects_edits()
    {
        var claim = ClaimScenarios.InStatus(ClaimStatus.Closed);

        Should.Throw<DomainRuleViolationException>(() => claim.AddParty(Witness(), Handler1, Now))
            .Violations.ShouldHaveSingleItem().Code.ShouldBe("CLAIM_NOT_EDITABLE");
        Should.Throw<DomainRuleViolationException>(claim.EnsureReserveChangesAllowed)
            .Violations.ShouldHaveSingleItem().Code.ShouldBe("RESERVES_NOT_ALLOWED_IN_STATUS");
    }

    // ------------------------------------------------------------------ Withdrawn

    [Theory]
    [InlineData(ClaimStatus.Open)]
    [InlineData(ClaimStatus.UnderInvestigation)]
    public void Withdrawal_requires_a_reason_and_is_terminal(ClaimStatus from)
    {
        var claim = ClaimScenarios.InStatus(from);

        Should.Throw<DomainRuleViolationException>(() =>
                claim.TransitionTo(ClaimStatus.Withdrawn, new TransitionRequest("   "), ReserveFacts.None, Handler1, Now))
            .Violations.ShouldHaveSingleItem().Code.ShouldBe("REASON_REQUIRED");

        claim.TransitionTo(ClaimStatus.Withdrawn, new TransitionRequest("Claimant withdrew in writing."), ReserveFacts.None, Handler1, Now);

        claim.Status.ShouldBe(ClaimStatus.Withdrawn);
        claim.ClosureReason.ShouldBe("Claimant withdrew in writing.");
        claim.DomainEvents.OfType<ClaimWithdrawn>().ShouldHaveSingleItem().Reason.ShouldBe("Claimant withdrew in writing.");
        Should.Throw<DomainRuleViolationException>(() => claim.UpdateNotes("x", Handler1, Now));
    }

    [Fact]
    public void Transition_reason_longer_than_500_characters_is_rejected()
    {
        var claim = ClaimScenarios.InStatus(ClaimStatus.Open);

        Should.Throw<DomainRuleViolationException>(() =>
                claim.TransitionTo(ClaimStatus.Withdrawn, new TransitionRequest(new string('r', 501)), ReserveFacts.None, Handler1, Now))
            .Violations.ShouldHaveSingleItem().Code.ShouldBe("REASON_TOO_LONG");
    }

    // ------------------------------------------------------------------ Reopen (BR-ST-04, D-12)

    [Fact]
    public void BR_ST_04_handler_cannot_reopen()
    {
        var claim = ClaimScenarios.InStatus(ClaimStatus.Closed);

        Should.Throw<DomainForbiddenException>(() =>
                claim.TransitionTo(ClaimStatus.Reopened, new TransitionRequest("New evidence."), ReserveFacts.None, Handler1, Now))
            .Code.ShouldBe("TRANSITION_ROLE");
        claim.Status.ShouldBe(ClaimStatus.Closed);
    }

    [Fact]
    public void BR_ST_04_reopen_requires_a_reason()
    {
        var claim = ClaimScenarios.InStatus(ClaimStatus.Closed);

        Should.Throw<DomainRuleViolationException>(() =>
                claim.TransitionTo(ClaimStatus.Reopened, TransitionRequest.None, ReserveFacts.None, Supervisor, Now))
            .Violations.ShouldHaveSingleItem().Code.ShouldBe("REASON_REQUIRED");
    }

    [Theory]
    [InlineData(UserRole.Supervisor)]
    [InlineData(UserRole.Manager)]
    public void BR_ST_04_reopen_goes_through_Reopened_straight_to_Open(UserRole role)
    {
        var claim = ClaimScenarios.InStatus(ClaimStatus.Closed);
        var actor = role == UserRole.Manager ? Manager : Supervisor;

        claim.TransitionTo(ClaimStatus.Reopened, new TransitionRequest("New medical evidence received."), ReserveFacts.None, actor, Now);

        claim.Status.ShouldBe(ClaimStatus.Open);
        claim.ClosedAt.ShouldBeNull();
        claim.ClosureReason.ShouldBeNull();
        claim.ReopenedAt.ShouldBe(Now);
        claim.ReopenCount.ShouldBe(1);

        claim.DomainEvents.Select(e => e.GetType())
            .ShouldBe([typeof(ClaimStatusChanged), typeof(ClaimReopened), typeof(ClaimStatusChanged)]);
        var changes = claim.DomainEvents.OfType<ClaimStatusChanged>().Select(e => (e.FromStatus, e.ToStatus)).ToList();
        changes.ShouldBe([(ClaimStatus.Closed, ClaimStatus.Reopened), (ClaimStatus.Reopened, ClaimStatus.Open)]);
    }

    // ------------------------------------------------------------------ pre-flight (D-27)

    [Fact]
    public void Available_transitions_report_blockers_and_confirmations_per_target()
    {
        var claim = ClaimScenarios.InStatus(ClaimStatus.Open);
        var facts = new ReserveFacts(HasApprovedTransaction: false, HasPendingApproval: false, HasOpenReserveBalance: true);

        var options = claim.GetAvailableTransitions(facts, Handler1).ToDictionary(e => e.Target);

        options.Keys.ShouldBe(
            [ClaimStatus.UnderInvestigation, ClaimStatus.PendingPayment, ClaimStatus.Closed, ClaimStatus.Withdrawn],
            ignoreOrder: true);
        options[ClaimStatus.UnderInvestigation].CanProceed.ShouldBeTrue();
        options[ClaimStatus.PendingPayment].IsAllowed.ShouldBeFalse();
        options[ClaimStatus.Closed].IsAllowed.ShouldBeTrue();
        options[ClaimStatus.Closed].ConfirmationsRequired.ShouldHaveSingleItem().Code.ShouldBe("CC-04");
        options[ClaimStatus.Withdrawn].RequiresReason.ShouldBeTrue();
        options[ClaimStatus.Withdrawn].ConfirmationsRequired.ShouldHaveSingleItem().Code.ShouldBe("REASON_REQUIRED");
    }

    [Fact]
    public void Available_transitions_on_closed_claim_show_reopen_needs_supervisor_for_a_handler()
    {
        var claim = ClaimScenarios.InStatus(ClaimStatus.Closed);

        var reopen = claim.GetAvailableTransitions(ReserveFacts.None, Handler1).ShouldHaveSingleItem();

        reopen.Target.ShouldBe(ClaimStatus.Reopened);
        reopen.HasRequiredRole.ShouldBeFalse();
        reopen.IsAllowed.ShouldBeFalse();
        claim.GetAvailableTransitions(ReserveFacts.None, Supervisor).Single().HasRequiredRole.ShouldBeTrue();
    }

    [Fact]
    public void Withdrawn_claim_has_no_available_transitions() =>
        ClaimScenarios.InStatus(ClaimStatus.Withdrawn).GetAvailableTransitions(ReserveFacts.None, Manager).ShouldBeEmpty();
}
