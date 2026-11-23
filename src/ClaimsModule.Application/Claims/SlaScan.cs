using ClaimsModule.Application.Abstractions.Persistence;
using ClaimsModule.Application.Common.Messaging;
using ClaimsModule.Domain.Claims;
using MediatR;

namespace ClaimsModule.Application.Claims;

/// <summary>A Draft or Open claim with its last real activity (UpdatedAt, or CreatedAt if never updated).</summary>
public sealed record SlaCandidate(Guid ClaimId, DateTimeOffset LastActivityAt);

/// <summary>Implemented in Persistence: the activity timestamps are shadow audit columns (S15.1).</summary>
public interface ISlaQueries
{
    /// <summary>
    /// Draft/Open claims inactive since before <paramref name="inactiveBefore"/> and not flagged since
    /// before <paramref name="lastBreachBefore"/> (the 24 h dedupe).
    /// </summary>
    Task<IReadOnlyList<SlaCandidate>> FindBreachCandidatesAsync(DateTimeOffset inactiveBefore, DateTimeOffset lastBreachBefore, CancellationToken cancellationToken);
}

public sealed record SlaScanResult(int ClaimsChecked, int BreachesRecorded);

/// <summary>
/// SlaMonitoringJob body (S12.2, every 15 minutes): flags Draft/Open claims with no update for 48 h
/// and writes SLA_BREACH_DETECTED, at most once per 24 h per claim. It never changes the status (D-13).
/// </summary>
[SystemRequest]
public sealed record RunSlaScanCommand : ICommand<SlaScanResult>;

internal sealed class RunSlaScanHandler(ISlaQueries queries, IClaimRepository claims, TimeProvider clock)
    : IRequestHandler<RunSlaScanCommand, SlaScanResult>
{
    public async Task<SlaScanResult> Handle(RunSlaScanCommand request, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var candidates = await queries.FindBreachCandidatesAsync(
            now - Claim.SlaInactivityThreshold, now - Claim.SlaBreachRepeatInterval, cancellationToken);

        var recorded = 0;
        foreach (var candidate in candidates)
        {
            // The aggregate re-checks every rule (status, 48 h, 24 h dedupe) before flagging.
            var claim = await claims.GetByIdAsync(candidate.ClaimId, cancellationToken);
            if (claim?.TryRecordSlaBreach(candidate.LastActivityAt, now) == true)
            {
                recorded++;
            }
        }

        return new SlaScanResult(candidates.Count, recorded);
    }
}
