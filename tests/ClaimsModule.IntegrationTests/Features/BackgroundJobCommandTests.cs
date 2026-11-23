using ClaimsModule.Application.Abstractions.Persistence;
using ClaimsModule.Application.Claims;
using ClaimsModule.Application.Reserves;
using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Reserves;
using ClaimsModule.IntegrationTests.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace ClaimsModule.IntegrationTests.Features;

/// <summary>
/// Tasks 26 and 27a: the GL posting job bodies and the stale-posting sweep as system commands,
/// and the after-commit enqueueing. Hangfire itself arrives in P4; here a recording scheduler stands in.
/// The scans see the whole shared test database, so assertions look only at this test's own rows.
/// </summary>
[Collection(SqlServerTestGroup.Name)]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA1001", Justification = "_host is disposed in IAsyncLifetime.DisposeAsync, which xUnit calls after every test.")]
public sealed class BackgroundJobCommandTests(SqlServerDatabaseFixture db) : IAsyncLifetime
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10); // GlPostingOptions default

    private ApplicationHost _host = null!;

    public Task InitializeAsync()
    {
        _host = new ApplicationHost(db);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private DateTimeOffset Now => _host.Clock.Now;

    private async Task<(Guid ClaimId, ReserveSubmissionDto Reserve)> ClaimWithReserveAsync(decimal amount)
    {
        var created = await _host.SendAsync(Fnol.WithReserve(Now, amount));
        return (created.ClaimId, created.InitialReserve!);
    }

    private async Task<ReserveTransaction> TransactionAsync(Guid transactionId) =>
        await _host.InScopeAsync(async sp =>
        {
            var component = await sp.GetRequiredService<IReserveComponentRepository>().GetByTransactionIdAsync(transactionId);
            return component!.Transactions.Single(t => t.Id == transactionId);
        });

    // ------------------------------------------------------------------ task 26: enqueue after commit, idempotent posting

    [Fact]
    public async Task Auto_approval_enqueues_the_gl_job_after_commit_with_the_reserve_idempotency_key()
    {
        var (_, reserve) = await ClaimWithReserveAsync(5_000m);

        var job = _host.Jobs.Enqueued.Single(j => j.TransactionId == reserve.TransactionId);
        job.IdempotencyKey.ShouldBe($"Reserve:{reserve.ReserveComponentId}:Change:1");
    }

    [Fact]
    public async Task Pending_approval_enqueues_nothing_until_a_supervisor_approves()
    {
        var (claimId, reserve) = await ClaimWithReserveAsync(50_000m);
        _host.Jobs.Enqueued.ShouldNotContain(j => j.TransactionId == reserve.TransactionId);

        await _host.SendAsync(new ApproveReserveTransactionCommand(claimId, reserve.TransactionId), ApplicationHost.Supervisor1);

        _host.Jobs.Enqueued.Count(j => j.TransactionId == reserve.TransactionId).ShouldBe(1);
    }

    [Fact]
    public async Task Posting_is_idempotent()
    {
        var (_, reserve) = await ClaimWithReserveAsync(5_000m);

        var first = await _host.SendAsync(new SimulateGlPostingCommand(reserve.TransactionId, "job-1"));
        var second = await _host.SendAsync(new SimulateGlPostingCommand(reserve.TransactionId, "job-2"));

        first.Outcome.ShouldBe(GlPostingOutcome.Posted);
        second.Outcome.ShouldBe(GlPostingOutcome.AlreadyPosted);
        var txn = await TransactionAsync(reserve.TransactionId);
        txn.PostingStatus.ShouldBe(PostingStatus.Posted);
        txn.PostingJobId.ShouldBe("job-1");
    }

    [Fact]
    public async Task Concurrent_postings_post_exactly_once()
    {
        var (_, reserve) = await ClaimWithReserveAsync(5_000m);

        var runs = await Task.WhenAll(Enumerable.Range(1, 2).Select(async i =>
        {
            try
            {
                return (await _host.SendAsync(new SimulateGlPostingCommand(reserve.TransactionId, $"job-{i}"))).Outcome.ToString();
            }
            catch (ConcurrencyException)
            {
                return "Conflict"; // Hangfire retries it, and the retry finds it Posted
            }
        }));

        runs.Count(r => r == nameof(GlPostingOutcome.Posted)).ShouldBe(1);
        (await TransactionAsync(reserve.TransactionId)).PostingStatus.ShouldBe(PostingStatus.Posted);
        (await _host.SendAsync(new SimulateGlPostingCommand(reserve.TransactionId, "job-3"))).Outcome.ShouldBe(GlPostingOutcome.AlreadyPosted);
    }

    [Fact]
    public async Task Final_failure_marks_the_posting_failed_and_a_supervisor_retry_re_enqueues_it()
    {
        var (claimId, reserve) = await ClaimWithReserveAsync(5_000m);

        (await _host.SendAsync(new MarkGlPostingFailedCommand(reserve.TransactionId, "GL endpoint timed out 3 times"))).Outcome
            .ShouldBe(GlPostingOutcome.MarkedFailed);
        (await TransactionAsync(reserve.TransactionId)).PostingStatus.ShouldBe(PostingStatus.Failed);

        await _host.SendAsync(new RetryGlPostingCommand(claimId, reserve.TransactionId), ApplicationHost.Supervisor1);

        (await TransactionAsync(reserve.TransactionId)).PostingStatus.ShouldBe(PostingStatus.Pending);
        _host.Jobs.Enqueued.Count(j => j.TransactionId == reserve.TransactionId).ShouldBe(2); // approval + retry
    }

    [Fact]
    public async Task Failure_reported_after_posting_is_ignored()
    {
        var (_, reserve) = await ClaimWithReserveAsync(5_000m);
        await _host.SendAsync(new SimulateGlPostingCommand(reserve.TransactionId, "job-1"));

        (await _host.SendAsync(new MarkGlPostingFailedCommand(reserve.TransactionId, "late failure"))).Outcome.ShouldBe(GlPostingOutcome.Ignored);
        (await TransactionAsync(reserve.TransactionId)).PostingStatus.ShouldBe(PostingStatus.Posted);
    }

    // ------------------------------------------------------------------ task 27a: stale-Pending sweep

    [Fact]
    public async Task Sweep_re_enqueues_only_stale_approved_pending_postings_once_per_threshold()
    {
        var (_, stale) = await ClaimWithReserveAsync(5_000m);
        var (_, posted) = await ClaimWithReserveAsync(5_000m);
        await _host.SendAsync(new SimulateGlPostingCommand(posted.TransactionId, "job-1"));
        var (_, failed) = await ClaimWithReserveAsync(5_000m);
        await _host.SendAsync(new MarkGlPostingFailedCommand(failed.TransactionId, "down"));
        var (_, awaitingApproval) = await ClaimWithReserveAsync(50_000m);
        _host.Clock.Now += StaleAfter + TimeSpan.FromMinutes(1);
        var (_, fresh) = await ClaimWithReserveAsync(5_000m);
        int Requeues(Guid id) => _host.Jobs.Enqueued.Count(j => j.TransactionId == id) - 1; // minus the approval enqueue

        await _host.SendAsync(new RunStalePostingSweepCommand());

        Requeues(stale.TransactionId).ShouldBe(1);
        (await TransactionAsync(stale.TransactionId)).PostingLastEnqueuedAt.ShouldBe(Now);
        Requeues(posted.TransactionId).ShouldBe(0);
        Requeues(failed.TransactionId).ShouldBe(0);
        Requeues(fresh.TransactionId).ShouldBe(0);
        _host.Jobs.Enqueued.ShouldNotContain(j => j.TransactionId == awaitingApproval.TransactionId);

        _host.Clock.Now += TimeSpan.FromMinutes(5); // within the threshold of the last re-enqueue
        await _host.SendAsync(new RunStalePostingSweepCommand());
        Requeues(stale.TransactionId).ShouldBe(1);

        _host.Clock.Now += StaleAfter; // past it again: still Pending, so once more
        await _host.SendAsync(new RunStalePostingSweepCommand());
        Requeues(stale.TransactionId).ShouldBe(2);
    }

    [Fact]
    public async Task A_lost_enqueue_is_recovered_by_the_sweep_and_ends_posted()
    {
        _host.Jobs.FailEnqueue = true; // the after-commit enqueue fails; the UoW logs it, the data is committed
        var (_, reserve) = await ClaimWithReserveAsync(5_000m);
        _host.Jobs.FailEnqueue = false;
        _host.Jobs.Enqueued.ShouldNotContain(j => j.TransactionId == reserve.TransactionId);
        (await TransactionAsync(reserve.TransactionId)).PostingStatus.ShouldBe(PostingStatus.Pending);

        _host.Clock.Now += StaleAfter + TimeSpan.FromMinutes(1);
        await _host.SendAsync(new RunStalePostingSweepCommand());
        var job = _host.Jobs.Enqueued.Single(j => j.TransactionId == reserve.TransactionId);
        await _host.SendAsync(new SimulateGlPostingCommand(job.TransactionId, "job-after-sweep"));

        (await TransactionAsync(reserve.TransactionId)).PostingStatus.ShouldBe(PostingStatus.Posted);
    }
}
