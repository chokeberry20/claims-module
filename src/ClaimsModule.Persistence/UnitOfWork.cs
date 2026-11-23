using ClaimsModule.Application.Abstractions.Persistence;
using ClaimsModule.Application.Common.Exceptions;
using ClaimsModule.Persistence.Entities;
using Microsoft.Data.SqlClient;
using ClaimsModule.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClaimsModule.Persistence;

/// <summary>
/// Unit of Work over <see cref="ClaimsDbContext"/> (brief §2.4, PLAN §4 command flow):
/// <list type="number">
/// <item>Collect domain events from tracked aggregates and dispatch them <b>before commit</b>.
/// Handlers write audit entries into the same context, so they commit atomically with the change.
/// Handlers may cause further events, so dispatch repeats until it settles.</item>
/// <item>SaveChanges. Interceptors stamp tenant, audit and soft-delete columns. A RowVer conflict
/// becomes <see cref="ConcurrencyException"/>.</item>
/// <item>After the commit, dispatch the same events <b>after commit</b> (Hangfire enqueueing). Inside
/// <see cref="ExecuteInTransactionAsync{T}"/> this waits for the outer commit and is dropped on
/// rollback. A failure here is logged, never rethrown: the data is already committed, and the
/// stale-posting sweep re-enqueues anything missed (PLAN §6.3).</item>
/// </list>
/// </summary>
internal sealed partial class UnitOfWork(
    ClaimsDbContext context,
    IDomainEventDispatcher dispatcher,
    ILogger<UnitOfWork> logger) : IUnitOfWork
{
    private const int MaxDispatchRounds = 10;
    private readonly List<IDomainEvent> _awaitingCommit = [];

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await DispatchBeforeCommitAsync(cancellationToken);
            var written = await context.SaveChangesAsync(cancellationToken);

            if (context.Database.CurrentTransaction is null)
            {
                await FlushAfterCommitAsync(cancellationToken);
            }

            return written;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _awaitingCommit.Clear();
            throw new ConcurrencyException("The record was changed by another user. Reload it and try again.", ex);
        }
        catch (DbUpdateException ex) when (IsDuplicateIdempotencyKey(ex))
        {
            // Two requests with the same Idempotency-Key ran concurrently; the first one wins (D-66).
            _awaitingCommit.Clear();
            throw new IdempotencyConflictException("A request with this Idempotency-Key is already being processed. Retry shortly.");
        }
        catch
        {
            _awaitingCommit.Clear();
            throw;
        }
    }

    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        if (context.Database.CurrentTransaction is not null)
        {
            // Join the transaction that is already open; the outermost call commits and flushes.
            return await work(cancellationToken);
        }

        // With EnableRetryOnFailure, a user-initiated transaction must run inside the execution
        // strategy, otherwise the first EF operation in it throws (D-56, verified in P2). The strategy
        // replays the whole delegate after a transient failure, so each attempt starts clean: the
        // failed attempt's rolled-back entities and its collected events are discarded. That is why
        // `work` must load and change everything itself (see IUnitOfWork.ExecuteInTransactionAsync).
        var attempt = 0;
        try
        {
            var result = await context.Database.CreateExecutionStrategy().ExecuteAsync(
                async ct =>
                {
                    if (attempt++ > 0)
                    {
                        context.ChangeTracker.Clear();
                        _awaitingCommit.Clear();
                    }

                    // Disposing an uncommitted transaction rolls it back.
                    await using var transaction = await context.Database.BeginTransactionAsync(ct);
                    var value = await work(ct);
                    await transaction.CommitAsync(ct);
                    return value;
                },
                cancellationToken);

            await FlushAfterCommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            _awaitingCommit.Clear();
            throw;
        }
    }

    private async Task DispatchBeforeCommitAsync(CancellationToken cancellationToken)
    {
        for (var round = 0; ; round++)
        {
            var events = CollectAndClearDomainEvents();
            if (events.Count == 0)
            {
                return;
            }

            if (round == MaxDispatchRounds)
            {
                throw new InvalidOperationException($"Domain event dispatch did not settle after {MaxDispatchRounds} rounds.");
            }

            await dispatcher.DispatchBeforeCommitAsync(events, cancellationToken);
            _awaitingCommit.AddRange(events);
        }
    }

    private async Task FlushAfterCommitAsync(CancellationToken cancellationToken)
    {
        if (_awaitingCommit.Count == 0)
        {
            return;
        }

        var events = _awaitingCommit.ToList();
        _awaitingCommit.Clear();
        try
        {
            await dispatcher.DispatchAfterCommitAsync(events, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogAfterCommitFailure(logger, ex, events.Count);
        }
    }

    private static bool IsDuplicateIdempotencyKey(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 }
        && ex.Entries.Any(e => e.Entity is IdempotencyRecord);

    private List<IDomainEvent> CollectAndClearDomainEvents()
    {
        var aggregates = context.ChangeTracker.Entries<AggregateRoot>()
            .Select(e => e.Entity)
            .Where(a => a.DomainEvents.Count > 0)
            .ToList();

        var events = aggregates.SelectMany(a => a.DomainEvents).OrderBy(e => e.OccurredAt).ToList();
        aggregates.ForEach(a => a.ClearDomainEvents());
        return events;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "After-commit dispatch of {EventCount} domain events failed; the data is committed.")]
    private static partial void LogAfterCommitFailure(ILogger logger, Exception exception, int eventCount);
}
