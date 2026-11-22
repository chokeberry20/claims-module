using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Claims.Workflow;
using ClaimsModule.Domain.Common;
using static ClaimsModule.Domain.Tests.TestData;

namespace ClaimsModule.Domain.Tests.Claims;

/// <summary>The transition table against S4.2, checked exhaustively over all 7×7 = 49 status pairs.</summary>
public sealed class ClaimWorkflowTableTests
{
    /// <summary>S4.2 copied by hand from the spec. Keep it independent of ClaimWorkflow.</summary>
    private static readonly HashSet<(ClaimStatus From, ClaimStatus To)> SpecTransitions =
    [
        (ClaimStatus.Draft, ClaimStatus.Open),
        (ClaimStatus.Open, ClaimStatus.UnderInvestigation),
        (ClaimStatus.Open, ClaimStatus.PendingPayment),
        (ClaimStatus.Open, ClaimStatus.Closed),
        (ClaimStatus.Open, ClaimStatus.Withdrawn),
        (ClaimStatus.UnderInvestigation, ClaimStatus.Open),
        (ClaimStatus.UnderInvestigation, ClaimStatus.PendingPayment),
        (ClaimStatus.UnderInvestigation, ClaimStatus.Closed),
        (ClaimStatus.UnderInvestigation, ClaimStatus.Withdrawn),
        (ClaimStatus.PendingPayment, ClaimStatus.Closed),
        (ClaimStatus.Closed, ClaimStatus.Reopened),
        (ClaimStatus.Reopened, ClaimStatus.Open),
    ];

    public static TheoryData<ClaimStatus, ClaimStatus> AllPairs()
    {
        var data = new TheoryData<ClaimStatus, ClaimStatus>();
        foreach (var from in Enum.GetValues<ClaimStatus>())
        {
            foreach (var to in Enum.GetValues<ClaimStatus>())
            {
                data.Add(from, to);
            }
        }

        return data;
    }

    /// <summary>The resting statuses a claim can actually be in. Reopened is transient (D-12).</summary>
    public static TheoryData<ClaimStatus, ClaimStatus> ReachablePairs()
    {
        var data = new TheoryData<ClaimStatus, ClaimStatus>();
        foreach (var from in Enum.GetValues<ClaimStatus>().Where(s => s != ClaimStatus.Reopened))
        {
            foreach (var to in Enum.GetValues<ClaimStatus>())
            {
                data.Add(from, to);
            }
        }

        return data;
    }

    [Fact]
    public void All_49_pairs_are_covered() => AllPairs().Count.ShouldBe(49);

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void Table_defines_exactly_the_S4_2_transitions(ClaimStatus from, ClaimStatus to) =>
        (ClaimWorkflow.Find(from, to) is not null).ShouldBe(SpecTransitions.Contains((from, to)));

    [Fact]
    public void Table_has_no_duplicate_rows() =>
        ClaimWorkflow.Transitions.Select(t => (t.From, t.To)).Distinct().Count().ShouldBe(ClaimWorkflow.Transitions.Count);

    [Theory]
    [MemberData(nameof(ReachablePairs))]
    public void BR_ST_01_undefined_transitions_are_rejected_on_a_real_claim(ClaimStatus from, ClaimStatus to)
    {
        if (SpecTransitions.Contains((from, to)))
        {
            return; // Valid pairs are exercised with their preconditions in ClaimLifecycleTests.
        }

        var claim = ClaimScenarios.InStatus(from);

        var ex = Should.Throw<DomainRuleViolationException>(() =>
            claim.TransitionTo(to, new TransitionRequest("reason", "justification", true), ClaimScenarios.ApprovedReserve, Manager, Now));

        var violation = ex.Violations.ShouldHaveSingleItem();
        violation.Code.ShouldBe("INVALID_TRANSITION");
        violation.Field.ShouldBe("StatusTransition");
        violation.Message.ShouldStartWith($"Transition from {from} to {to} is not permitted.");
        claim.Status.ShouldBe(from);
    }

    [Theory]
    [InlineData(ClaimStatus.Draft, "Valid next statuses: Open.")]
    [InlineData(ClaimStatus.Open, "Valid next statuses: UnderInvestigation, PendingPayment, Closed, Withdrawn.")]
    [InlineData(ClaimStatus.Closed, "Valid next statuses: Reopened.")]
    [InlineData(ClaimStatus.Withdrawn, "Valid next statuses: none.")]
    public void BR_ST_01_rejection_message_lists_the_valid_next_statuses(ClaimStatus from, string expectedSuffix)
    {
        var claim = ClaimScenarios.InStatus(from);
        var invalidTarget = from == ClaimStatus.Draft ? ClaimStatus.Closed : ClaimStatus.Draft;

        Should.Throw<DomainRuleViolationException>(() =>
                claim.TransitionTo(invalidTarget, TransitionRequest.None, ReserveFacts.None, Manager, Now))
            .Violations.Single().Message.ShouldEndWith(expectedSuffix);
    }

    [Fact]
    public void Brief_BR_C_06_draft_cannot_go_directly_to_closed() =>
        ClaimWorkflow.Find(ClaimStatus.Draft, ClaimStatus.Closed).ShouldBeNull();

    [Fact]
    public void Reopen_requires_supervisor_and_a_reason_withdrawal_requires_a_reason()
    {
        var reopen = ClaimWorkflow.Find(ClaimStatus.Closed, ClaimStatus.Reopened).ShouldNotBeNull();
        reopen.RequiredRole.ShouldBe(UserRole.Supervisor);
        reopen.RequiresReason.ShouldBeTrue();

        ClaimWorkflow.Transitions.Where(t => t.To == ClaimStatus.Withdrawn).ShouldAllBe(t => t.RequiresReason);
        ClaimWorkflow.Transitions.Where(t => t.From != ClaimStatus.Closed && t.From != ClaimStatus.Reopened)
            .ShouldAllBe(t => t.RequiredRole == UserRole.Handler);
    }
}
