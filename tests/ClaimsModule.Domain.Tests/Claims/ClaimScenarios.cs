using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Claims.Workflow;
using static ClaimsModule.Domain.Tests.Claims.ClaimFactory;
using static ClaimsModule.Domain.Tests.TestData;

namespace ClaimsModule.Domain.Tests.Claims;

/// <summary>Moves claims into each reachable status through the public workflow only.</summary>
internal static class ClaimScenarios
{
    public static readonly ReserveFacts ApprovedReserve = new(HasApprovedTransaction: true, HasPendingApproval: false, HasOpenReserveBalance: false);

    public static Claim InStatus(ClaimStatus status)
    {
        var claim = NewCompleteDraft();
        switch (status)
        {
            case ClaimStatus.Draft:
                break;
            case ClaimStatus.Open:
                Open(claim);
                break;
            case ClaimStatus.UnderInvestigation:
                Open(claim);
                claim.TransitionTo(ClaimStatus.UnderInvestigation, TransitionRequest.None, ReserveFacts.None, Handler1, Now);
                break;
            case ClaimStatus.PendingPayment:
                Open(claim);
                claim.TransitionTo(ClaimStatus.PendingPayment, TransitionRequest.None, ApprovedReserve, Handler1, Now);
                break;
            case ClaimStatus.Closed:
                Open(claim);
                claim.TransitionTo(ClaimStatus.Closed, TransitionRequest.None, ReserveFacts.None, Handler1, Now);
                break;
            case ClaimStatus.Withdrawn:
                Open(claim);
                claim.TransitionTo(ClaimStatus.Withdrawn, new TransitionRequest("Claimant withdrew by phone."), ReserveFacts.None, Handler1, Now);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, "Not reachable as a resting status.");
        }

        claim.ClearDomainEvents();
        return claim;
    }

    public static void Open(Claim claim) =>
        claim.TransitionTo(ClaimStatus.Open, TransitionRequest.None, ReserveFacts.None, Handler1, Now);
}
