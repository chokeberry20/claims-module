using ClaimsModule.Application.Abstractions.Persistence;
using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Documents;
using ClaimsModule.Domain.ReferenceData;
using ClaimsModule.Domain.Reserves;
using Microsoft.EntityFrameworkCore;

namespace ClaimsModule.Persistence.Repositories;

/// <summary>Loads and adds the full Claim aggregate. Global filters apply tenant and soft-delete scoping.</summary>
internal sealed class ClaimRepository(ClaimsDbContext context) : IClaimRepository
{
    public Task<Claim?> GetByIdAsync(Guid claimId, CancellationToken cancellationToken = default) =>
        context.Claims
            .Include(c => c.LossEvent)
            .Include(c => c.Parties)
            .Include(c => c.RiskObjects)
            .Include(c => c.ValidationIssues)
            .AsSplitQuery()
            .SingleOrDefaultAsync(c => c.Id == claimId, cancellationToken);

    public void Add(Claim claim) => context.Claims.Add(claim);
}

internal sealed class ReserveComponentRepository(ClaimsDbContext context) : IReserveComponentRepository
{
    public async Task<IReadOnlyList<ClaimReserveComponent>> ListForClaimAsync(Guid claimId, CancellationToken cancellationToken = default) =>
        await context.ReserveComponents
            .Include(c => c.Transactions)
            .Where(c => c.ClaimId == claimId)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

    public Task<ClaimReserveComponent?> GetByTransactionIdAsync(Guid transactionId, CancellationToken cancellationToken = default) =>
        context.ReserveComponents
            .Include(c => c.Transactions)
            .SingleOrDefaultAsync(c => c.Transactions.Any(t => t.Id == transactionId), cancellationToken);

    public async Task<IReadOnlyList<ClaimReserveComponent>> ListWithStalePendingPostingsAsync(DateTimeOffset approvedBefore, CancellationToken cancellationToken = default) =>
        await context.ReserveComponents
            .Include(c => c.Transactions)
            .Where(c => c.Transactions.Any(t =>
                (t.ApprovalStatus == ApprovalStatus.AutoApproved || t.ApprovalStatus == ApprovalStatus.Approved)
                && t.PostingStatus == PostingStatus.Pending
                && t.ApprovedAt < approvedBefore
                && (t.PostingLastEnqueuedAt == null || t.PostingLastEnqueuedAt < approvedBefore)))
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

    public void Add(ClaimReserveComponent component) => context.ReserveComponents.Add(component);
}

internal sealed class ClaimDocumentRepository(ClaimsDbContext context) : IClaimDocumentRepository
{
    public void Add(ClaimDocument document) => context.ClaimDocuments.Add(document);
}

internal sealed class PolicyRepository(ClaimsDbContext context) : IPolicyRepository
{
    public Task<Policy?> GetByIdAsync(Guid policyId, CancellationToken cancellationToken = default) =>
        context.Policies.SingleOrDefaultAsync(p => p.Id == policyId, cancellationToken);
}

internal sealed class CauseOfLossCodeRepository(ClaimsDbContext context) : ICauseOfLossCodeRepository
{
    public Task<CauseOfLossCode?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        context.CauseOfLossCodes.SingleOrDefaultAsync(c => c.Code == code, cancellationToken);
}
