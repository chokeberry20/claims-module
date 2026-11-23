using System.ComponentModel;
using System.Diagnostics;
using ClaimsModule.Application.Abstractions.Jobs;
using ClaimsModule.Application.Claims;
using ClaimsModule.Application.Reserves;
using Hangfire;
using Hangfire.Common;
using Hangfire.Server;
using Hangfire.States;
using Hangfire.Storage;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClaimsModule.Infrastructure.Jobs;

// The job classes are thin (PLAN §4): each one sends a system command, so the logic and its tests
// live in Application, and Infrastructure never references Persistence.

/// <summary>
/// Telemetry for job runs (D-71). Hangfire creates no activities, so each job run starts its own: the
/// run shows up in App Insights with its SQL calls as children, while Hangfire's own parentless polling
/// SQL is filtered out by the API host.
/// </summary>
public static class JobTelemetry
{
    public const string ActivitySourceName = "ClaimsModule.Jobs";

    public static ActivitySource Source { get; } = new(ActivitySourceName);

    public static async Task RunAsync(string name, string? jobId, Func<Task> run)
    {
        using var activity = Source.StartActivity($"Job {name}", ActivityKind.Internal);
        activity?.SetTag("job.name", name);
        activity?.SetTag("job.id", jobId);
        try
        {
            await run();
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }
}

/// <summary>PostGLReserveChangeJob (S12.1, BR-R-06): posts one approved reserve change. Idempotent (task 26).</summary>
public sealed class PostGlReserveChangeJob(ISender sender)
{
    [DisplayName("GL posting {1}")]
    public Task RunAsync(Guid transactionId, string idempotencyKey, PerformContext? context) =>
        JobTelemetry.RunAsync("GL posting", context?.BackgroundJob.Id, () =>
            sender.Send(new SimulateGlPostingCommand(transactionId, context?.BackgroundJob.Id ?? "inline")));
}

/// <summary>SlaMonitoringJob (S12.2): every 15 minutes.</summary>
public sealed class SlaMonitoringJob(ISender sender)
{
    public const string RecurringJobId = "sla-monitoring";
    public const string Cron = "*/15 * * * *";

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public Task RunAsync() => JobTelemetry.RunAsync(RecurringJobId, null, () => sender.Send(new RunSlaScanCommand()));
}

/// <summary>StalePostingSweepJob (task 27a): every 5 minutes, re-enqueues lost GL postings.</summary>
public sealed class StalePostingSweepJob(ISender sender)
{
    public const string RecurringJobId = "stale-posting-sweep";
    public const string Cron = "*/5 * * * *";

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public Task RunAsync() => JobTelemetry.RunAsync(RecurringJobId, null, () => sender.Send(new RunStalePostingSweepCommand()));
}

/// <summary>IJobScheduler on Hangfire: called after commit, so the job never sees uncommitted data.</summary>
internal sealed class HangfireJobScheduler(IBackgroundJobClient jobs) : IJobScheduler
{
    public void EnqueueGlPosting(Guid transactionId, string idempotencyKey) =>
        jobs.Enqueue<PostGlReserveChangeJob>(j => j.RunAsync(transactionId, idempotencyKey, null));
}

/// <summary>
/// When a GL posting job reaches Failed (all automatic retries used up), records the final failure:
/// PostingStatus = Failed and GL_POSTING_FAILED (S12.1). Intermediate failures go back to Scheduled
/// and are not reported. A supervisor can then retry the posting (D-28).
/// </summary>
internal sealed partial class GlPostingFinalFailureFilter : JobFilterAttribute, IApplyStateFilter
{
    /// <summary>
    /// Hangfire filters are process-global, so there is exactly one instance; it uses the services of the
    /// host that configured Hangfire last (one host in production; tests create many in turn).
    /// </summary>
    public static GlPostingFinalFailureFilter Instance { get; } = new();

    private volatile IServiceProvider? _services;

    private GlPostingFinalFailureFilter()
    {
    }

    public void Attach(IServiceProvider services) => _services = services;

    public void OnStateApplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
        if (context.NewState is not FailedState failed
            || context.BackgroundJob.Job?.Type != typeof(PostGlReserveChangeJob)
            || context.BackgroundJob.Job.Args.Count == 0
            || context.BackgroundJob.Job.Args[0] is not Guid transactionId)
        {
            return;
        }

        if (_services is not { } services)
        {
            return;
        }

        var reason = $"GL posting failed after all retries: {failed.Exception.GetType().Name}: {failed.Exception.Message}";
        try
        {
            using var scope = services.GetRequiredService<IServiceScopeFactory>().CreateScope();
            scope.ServiceProvider.GetRequiredService<ISender>()
                .Send(new MarkGlPostingFailedCommand(transactionId, reason)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            // The job stays Failed in Hangfire either way; the posting stays Pending and the sweep retries it.
            LogMarkFailedError(services.GetRequiredService<ILogger<GlPostingFinalFailureFilter>>(), ex, transactionId);
        }
    }

    public void OnStateUnapplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not record the final GL posting failure for transaction {TransactionId}")]
    private static partial void LogMarkFailedError(ILogger logger, Exception exception, Guid transactionId);
}

/// <summary>Configuration section <c>Hangfire</c>.</summary>
public sealed class JobsOptions
{
    public const string SectionName = "Hangfire";

    /// <summary>Run a job server in this process. Off for test hosts that don't exercise jobs.</summary>
    public bool ServerEnabled { get; set; } = true;

    /// <summary>Automatic retries before a job is Failed (S12.1). Short delays so a demo doesn't wait hours (PLAN §6.5).</summary>
    public int RetryAttempts { get; set; } = 3;

    public int[] RetryDelaysSeconds { get; set; } = [10, 30, 60];

    /// <summary>How often idle workers poll SQL Server for new jobs.</summary>
    public TimeSpan QueuePollInterval { get; set; } = TimeSpan.FromSeconds(5);

    public int WorkerCount { get; set; } = 2;
}
