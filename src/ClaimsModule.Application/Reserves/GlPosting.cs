using ClaimsModule.Application.Abstractions.Jobs;
using ClaimsModule.Application.Abstractions.Persistence;
using ClaimsModule.Application.Common.Events;
using ClaimsModule.Application.Common.Exceptions;
using ClaimsModule.Application.Common.Messaging;
using ClaimsModule.Domain.Reserves;
using ClaimsModule.Domain.Reserves.Events;
using MediatR;
using Microsoft.Extensions.Options;

namespace ClaimsModule.Application.Reserves;

public enum GlPostingOutcome
{
    Posted = 0,
    AlreadyPosted = 1,
    MarkedFailed = 2,
    Ignored = 3,
}

public sealed record GlPostingResult(Guid TransactionId, GlPostingOutcome Outcome, PostingStatus PostingStatus);

/// <summary>Thrown by the simulated GL call (GlPostingOptions.SimulatedFailureRate) so the job retries.</summary>
public sealed class SimulatedGlPostingException(string idempotencyKey)
    : Exception($"Simulated general ledger failure for {idempotencyKey}.");

// ------------------------------------------------------------------ PostGLReserveChangeJob body (S12.1, BR-R-06)

/// <summary>
/// Simulates posting an approved reserve change to the general ledger. Re-entrant and idempotent: a
/// repeat (Hangfire retry, sweep re-enqueue, duplicate enqueue) finds the transaction Posted and does
/// nothing, so exactly one GL_POSTING_SIMULATED entry is written. Two concurrent runs conflict on the
/// component's row version; the loser fails with a concurrency error and its retry is a no-op.
/// </summary>
[SystemRequest]
public sealed record SimulateGlPostingCommand(Guid TransactionId, string JobId) : ICommand<GlPostingResult>;

internal sealed class SimulateGlPostingHandler(
    IReserveComponentRepository reserves,
    IOptions<GlPostingOptions> options,
    TimeProvider clock) : IRequestHandler<SimulateGlPostingCommand, GlPostingResult>
{
    public async Task<GlPostingResult> Handle(SimulateGlPostingCommand request, CancellationToken cancellationToken)
    {
        var component = await reserves.GetByTransactionIdAsync(request.TransactionId, cancellationToken)
            ?? throw new NotFoundException(nameof(ReserveTransaction), request.TransactionId);
        var transaction = component.Transactions.Single(t => t.Id == request.TransactionId);

        if (transaction.PostingStatus == PostingStatus.Posted)
        {
            return new GlPostingResult(transaction.Id, GlPostingOutcome.AlreadyPosted, transaction.PostingStatus);
        }

        if (options.Value.SimulatedFailureRate > 0 && Random.Shared.NextDouble() < options.Value.SimulatedFailureRate)
        {
            throw new SimulatedGlPostingException(transaction.IdempotencyKey);
        }

        var posted = component.MarkPosted(transaction.Id, request.JobId, clock.GetUtcNow());
        return new GlPostingResult(transaction.Id, posted ? GlPostingOutcome.Posted : GlPostingOutcome.AlreadyPosted, transaction.PostingStatus);
    }
}

/// <summary>Sent by the job's final-failure filter after the last retry (S12.1): PostingStatus = Failed, GL_POSTING_FAILED.</summary>
[SystemRequest]
public sealed record MarkGlPostingFailedCommand(Guid TransactionId, string Reason) : ICommand<GlPostingResult>;

internal sealed class MarkGlPostingFailedHandler(IReserveComponentRepository reserves, TimeProvider clock)
    : IRequestHandler<MarkGlPostingFailedCommand, GlPostingResult>
{
    public async Task<GlPostingResult> Handle(MarkGlPostingFailedCommand request, CancellationToken cancellationToken)
    {
        var component = await reserves.GetByTransactionIdAsync(request.TransactionId, cancellationToken)
            ?? throw new NotFoundException(nameof(ReserveTransaction), request.TransactionId);
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? "GL posting failed after all retries." : request.Reason.Trim();
        var marked = component.MarkPostingFailed(request.TransactionId, reason, clock.GetUtcNow());
        var status = component.Transactions.Single(t => t.Id == request.TransactionId).PostingStatus;
        return new GlPostingResult(request.TransactionId, marked ? GlPostingOutcome.MarkedFailed : GlPostingOutcome.Ignored, status);
    }
}

// ------------------------------------------------------------------ stale-Pending sweep (task 27a, D-56)

public sealed record StalePostingSweepResult(int Requeued);

/// <summary>
/// StalePostingSweepJob body (every 5 minutes): re-enqueues approved postings still Pending after
/// <see cref="GlPostingOptions.StalePendingAfter"/>, i.e. whose after-commit enqueue was lost. Each
/// re-enqueue is stamped, so the same transaction waits another threshold before the next one.
/// </summary>
[SystemRequest]
public sealed record RunStalePostingSweepCommand : ICommand<StalePostingSweepResult>;

internal sealed class RunStalePostingSweepHandler(
    IReserveComponentRepository reserves,
    IOptions<GlPostingOptions> options,
    TimeProvider clock) : IRequestHandler<RunStalePostingSweepCommand, StalePostingSweepResult>
{
    public async Task<StalePostingSweepResult> Handle(RunStalePostingSweepCommand request, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var cutoff = now - options.Value.StalePendingAfter;
        var components = await reserves.ListWithStalePendingPostingsAsync(cutoff, cancellationToken);

        var requeued = 0;
        foreach (var component in components)
        {
            foreach (var transaction in component.Transactions.Where(t => IsStale(t, cutoff)).ToList())
            {
                if (component.MarkPostingRequeued(transaction.Id, now))
                {
                    requeued++;
                }
            }
        }

        return new StalePostingSweepResult(requeued);
    }

    internal static bool IsStale(ReserveTransaction t, DateTimeOffset cutoff) =>
        t.IsApproved
        && t.PostingStatus == PostingStatus.Pending
        && t.ApprovedAt < cutoff
        && (t.PostingLastEnqueuedAt is null || t.PostingLastEnqueuedAt < cutoff);
}

// ------------------------------------------------------------------ after commit: enqueue the GL job

/// <summary>
/// Enqueues PostGLReserveChangeJob after commit (PLAN §4): on approval (auto or manual), on a
/// supervisor retry (D-28) and on a sweep re-enqueue (27a). Always the same idempotency key.
/// </summary>
internal sealed class EnqueueGlPostingHandler(IJobScheduler scheduler) :
    INotificationHandler<AfterCommit<ReserveTransactionApproved>>,
    INotificationHandler<AfterCommit<ReservePostingRetryRequested>>,
    INotificationHandler<AfterCommit<ReservePostingRequeued>>
{
    public Task Handle(AfterCommit<ReserveTransactionApproved> notification, CancellationToken cancellationToken) =>
        Enqueue(notification.Event.TransactionId, notification.Event.IdempotencyKey);

    public Task Handle(AfterCommit<ReservePostingRetryRequested> notification, CancellationToken cancellationToken) =>
        Enqueue(notification.Event.TransactionId, notification.Event.IdempotencyKey);

    public Task Handle(AfterCommit<ReservePostingRequeued> notification, CancellationToken cancellationToken) =>
        Enqueue(notification.Event.TransactionId, notification.Event.IdempotencyKey);

    private Task Enqueue(Guid transactionId, string idempotencyKey)
    {
        scheduler.EnqueueGlPosting(transactionId, idempotencyKey);
        return Task.CompletedTask;
    }
}
