using ClaimsModule.Domain.Claims.Workflow;
using ClaimsModule.Domain.Common;

namespace ClaimsModule.Domain.Reserves;

/// <summary>Claim-level reserve figures derived from all of a claim's reserve components.</summary>
public static class ReserveSummary
{
    /// <summary>D-19: the approved gross total, Indemnity + Expense + ALAE. Subrogation is excluded.</summary>
    public static Money ApprovedGrossTotal(IEnumerable<ClaimReserveComponent> components)
    {
        ArgumentNullException.ThrowIfNull(components);
        return components
            .Where(c => c.ComponentType.CountsTowardsAggregateLimit())
            .Aggregate(Money.Zero, (total, c) => total + c.CurrentAmount);
    }

    /// <summary>The facts the claim needs for the PendingPayment and Closed transitions (S4.2, S4.3, D-11).</summary>
    public static ReserveFacts ToReserveFacts(IEnumerable<ClaimReserveComponent> components)
    {
        ArgumentNullException.ThrowIfNull(components);
        var list = components.ToList();
        return new ReserveFacts(
            HasApprovedTransaction: list.Any(c => c.Transactions.Any(t => t.IsApproved)),
            HasPendingApproval: list.Any(c => c.HasPendingApproval),
            HasOpenReserveBalance: list.Any(c => c.CurrentAmount.IsPositive));
    }
}
