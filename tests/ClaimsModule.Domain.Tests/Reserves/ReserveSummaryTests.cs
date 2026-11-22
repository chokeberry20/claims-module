using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Claims.Workflow;
using ClaimsModule.Domain.Common;
using ClaimsModule.Domain.Reserves;
using ClaimsModule.Domain.Tests.Claims;
using static ClaimsModule.Domain.Tests.TestData;

namespace ClaimsModule.Domain.Tests.Reserves;

public sealed class ReserveSummaryTests
{
    private static readonly Guid ClaimId = Guid.NewGuid();

    private static ClaimReserveComponent WithAutoApproved(ReserveComponentType type, decimal amount)
    {
        var c = ClaimReserveComponent.Create(ClaimId, type);
        c.Submit(ReserveTransactionType.Add, amount, "reason", Handler1, Money.Zero, Now);
        return c;
    }

    [Fact]
    public void D_19_gross_total_excludes_subrogation_recoveries()
    {
        // S6.2 example: Indemnity 25k, Expense 5k, SubrogationRecoverable −8k (amounts scaled to auto-approval).
        var components = new[]
        {
            WithAutoApproved(ReserveComponentType.Indemnity, 2_500m),
            WithAutoApproved(ReserveComponentType.Expense, 500m),
            WithAutoApproved(ReserveComponentType.ALAE, 250m),
            WithAutoApproved(ReserveComponentType.SubrogationRecoverable, -800m),
        };

        ReserveSummary.ApprovedGrossTotal(components).ShouldBe(Money.Of(3_250m));
    }

    [Fact]
    public void Pending_amounts_do_not_count_towards_the_approved_total()
    {
        var c = ClaimReserveComponent.Create(ClaimId, ReserveComponentType.Indemnity);
        c.Submit(ReserveTransactionType.Add, 50_000m, "reason", Handler1, Money.Zero, Now);

        ReserveSummary.ApprovedGrossTotal([c]).ShouldBe(Money.Zero);
    }

    [Fact]
    public void Reserve_facts_reflect_approved_pending_and_open_balances()
    {
        var approved = WithAutoApproved(ReserveComponentType.Indemnity, 5_000m);
        var pending = ClaimReserveComponent.Create(ClaimId, ReserveComponentType.Expense);
        pending.Submit(ReserveTransactionType.Add, 20_000m, "reason", Handler1, Money.Zero, Now);

        ReserveSummary.ToReserveFacts([]).ShouldBe(ReserveFacts.None);
        ReserveSummary.ToReserveFacts([approved, pending]).ShouldBe(new ReserveFacts(true, true, true));
    }

    [Fact]
    public void Negative_recovery_alone_is_not_an_open_reserve_balance_for_CC_04() =>
        ReserveSummary.ToReserveFacts([WithAutoApproved(ReserveComponentType.SubrogationRecoverable, -800m)])
            .HasOpenReserveBalance.ShouldBeFalse();

    [Fact]
    public void Facts_drive_the_claim_transition_end_to_end()
    {
        var claim = ClaimScenarios.InStatus(ClaimStatus.Open);
        var reserve = WithAutoApproved(ReserveComponentType.Indemnity, 5_000m);

        claim.TransitionTo(ClaimStatus.PendingPayment, TransitionRequest.None, ReserveSummary.ToReserveFacts([reserve]), Handler1, Now);

        claim.Status.ShouldBe(ClaimStatus.PendingPayment);
        Should.Throw<DomainRuleViolationException>(() =>
                claim.TransitionTo(ClaimStatus.Closed, TransitionRequest.None, ReserveSummary.ToReserveFacts([reserve]), Handler1, Now))
            .Violations.ShouldHaveSingleItem().Code.ShouldBe("CC-04");
    }
}
