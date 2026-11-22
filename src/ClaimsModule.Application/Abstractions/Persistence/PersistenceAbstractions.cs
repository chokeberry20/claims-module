using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Common;
using ClaimsModule.Domain.Documents;
using ClaimsModule.Domain.ReferenceData;
using ClaimsModule.Domain.Reserves;

namespace ClaimsModule.Application.Abstractions.Persistence;

/// <summary>
/// Unit of Work (brief §2.4). It coordinates one atomic save across repositories and dispatches
/// domain events in two phases:
/// <list type="bullet">
/// <item>before commit, inside the same transaction (audit entries);</item>
/// <item>after commit (job enqueueing).</item>
/// </list>
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Saves all tracked changes. A concurrency conflict raises <see cref="ConcurrencyException"/>.
    /// Inside <see cref="ExecuteInTransactionAsync{T}"/>, the after-commit handlers wait until the
    /// outer transaction commits.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="work"/> in one database transaction. Needed when raw SQL (claim number
    /// allocation) and SaveChanges must commit or roll back together (D-14).
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default);
}

/// <summary>Dispatches domain events collected from tracked aggregates.</summary>
public interface IDomainEventDispatcher
{
    /// <summary>
    /// Runs inside the open transaction, before commit. Handlers may add more changes (for example
    /// audit entries) and raise further events.
    /// </summary>
    Task DispatchBeforeCommitAsync(IReadOnlyCollection<IDomainEvent> domainEvents, CancellationToken cancellationToken);

    /// <summary>Runs after a successful commit. Failures here never roll back the committed data.</summary>
    Task DispatchAfterCommitAsync(IReadOnlyCollection<IDomainEvent> domainEvents, CancellationToken cancellationToken);
}

/// <summary>Another writer changed the aggregate first (ROWVERSION mismatch). The API maps this to HTTP 409.</summary>
public sealed class ConcurrencyException : Exception
{
    public ConcurrencyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Allocates the next gapless claim number for the current organisation and year (S5.3, D-14).
/// It must be called inside <see cref="IUnitOfWork.ExecuteInTransactionAsync{T}"/>, so that a
/// rollback releases the number.
/// </summary>
public interface IClaimNumberGenerator
{
    Task<ClaimNumber> NextAsync(int year, CancellationToken cancellationToken = default);
}

public interface IClaimRepository
{
    /// <summary>Loads the full Claim aggregate: loss event, parties, risk objects and validation issues.</summary>
    Task<Claim?> GetByIdAsync(Guid claimId, CancellationToken cancellationToken = default);

    void Add(Claim claim);
}

public interface IReserveComponentRepository
{
    /// <summary>All reserve components of a claim, with their transaction history.</summary>
    Task<IReadOnlyList<ClaimReserveComponent>> ListForClaimAsync(Guid claimId, CancellationToken cancellationToken = default);

    void Add(ClaimReserveComponent component);
}

public interface IClaimDocumentRepository
{
    void Add(ClaimDocument document);
}

public interface IPolicyRepository
{
    Task<Policy?> GetByIdAsync(Guid policyId, CancellationToken cancellationToken = default);
}

public interface ICauseOfLossCodeRepository
{
    Task<CauseOfLossCode?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
}
