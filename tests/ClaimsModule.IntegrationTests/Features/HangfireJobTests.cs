using ClaimsModule.Application.Abstractions.Audit;
using ClaimsModule.Application.Abstractions.Jobs;
using ClaimsModule.Application.Abstractions.Persistence;
using ClaimsModule.Application.Audit;
using ClaimsModule.Domain.Reserves;
using ClaimsModule.Infrastructure.Jobs;
using ClaimsModule.IntegrationTests.Persistence;
using Hangfire;
using Hangfire.AspNetCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClaimsModule.IntegrationTests.Features;

/// <summary>
/// Task 30: Hangfire end to end on SQL Server storage (the test database, schema HangFire): the
/// after-commit enqueue, PostGLReserveChangeJob, the final-failure filter and the recurring jobs.
/// Waits poll the database with a generous bound; nothing depends on a fixed sleep.
/// </summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class HangfireJobTests(SqlServerDatabaseFixture db)
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

    private ApplicationHost NewHost(double failureRate = 0) => new(db, services =>
    {
        services.AddClaimsJobs(db.ConnectionString, new JobsOptions
        {
            ServerEnabled = false, // started explicitly below
            RetryAttempts = 0, // fail straight to Failed, so the final-failure path runs without waiting
            QueuePollInterval = TimeSpan.FromMilliseconds(200),
        });
        services.Configure<GlPostingOptions>(o => o.SimulatedFailureRate = failureRate);
    });

    private static BackgroundJobServer StartServer(ApplicationHost host)
    {
        _ = host.Services.GetRequiredService<IGlobalConfiguration>(); // applies the Hangfire configuration
        return new BackgroundJobServer(
            new BackgroundJobServerOptions
            {
                WorkerCount = 1,
                SchedulePollingInterval = TimeSpan.FromMilliseconds(200),
                Activator = new AspNetCoreJobActivator(host.Services.GetRequiredService<IServiceScopeFactory>()),
            },
            host.Services.GetRequiredService<JobStorage>());
    }

    private static async Task<ReserveTransaction> TransactionAsync(ApplicationHost host, Guid transactionId) =>
        await host.InScopeAsync(async sp =>
            (await sp.GetRequiredService<IReserveComponentRepository>().GetByTransactionIdAsync(transactionId))!
                .Transactions.Single(t => t.Id == transactionId));

    private static async Task WaitForAsync(ApplicationHost host, Guid transactionId, PostingStatus status)
    {
        var deadline = DateTime.UtcNow + Bound;
        while ((await TransactionAsync(host, transactionId)).PostingStatus != status)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Posting of {transactionId} did not become {status} within {Bound}.");
            }

            await Task.Delay(200);
        }
    }

    [Fact]
    public async Task Approval_enqueues_the_job_after_commit_and_the_server_posts_it()
    {
        await using var host = NewHost();
        using var server = StartServer(host);

        var created = await host.SendAsync(Fnol.WithReserve(host.Clock.Now, 5_000m)); // auto-approved → enqueued
        var transactionId = created.InitialReserve!.TransactionId;

        await WaitForAsync(host, transactionId, PostingStatus.Posted);

        var txn = await TransactionAsync(host, transactionId);
        txn.PostingJobId.ShouldNotBeNullOrWhiteSpace();
        txn.PostingJobId.ShouldNotBe("inline"); // the Hangfire job id
        var audit = (await host.SendAsync(new GetAuditLogQuery(created.ClaimId) { EventType = AuditEventTypes.GlPostingSimulated })).Items;
        audit.Count.ShouldBe(1);
        audit[0].NewValue.ShouldNotBeNull().ShouldContain(txn.PostingJobId);
    }

    [Fact]
    public async Task Final_failure_marks_the_posting_failed_and_audits_it()
    {
        await using var host = NewHost(failureRate: 1); // the simulated GL call always fails
        using var server = StartServer(host);

        var created = await host.SendAsync(Fnol.WithReserve(host.Clock.Now, 5_000m));
        var transactionId = created.InitialReserve!.TransactionId;

        await WaitForAsync(host, transactionId, PostingStatus.Failed);

        (await TransactionAsync(host, transactionId)).PostingFailureReason.ShouldNotBeNull().ShouldContain("SimulatedGlPostingException");
        var failed = (await host.SendAsync(new GetAuditLogQuery(created.ClaimId) { EventType = AuditEventTypes.GlPostingFailed })).Items;
        failed.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Recurring_jobs_are_registered_with_their_schedules()
    {
        await using var host = NewHost();
        _ = host.Services.GetRequiredService<IGlobalConfiguration>();

        JobsRegistration.RegisterRecurringJobs(host.Services.GetRequiredService<IRecurringJobManager>());

        using var connection = host.Services.GetRequiredService<JobStorage>().GetConnection();
        var recurring = Hangfire.Storage.StorageConnectionExtensions.GetRecurringJobs(connection).ToDictionary(j => j.Id, j => j.Cron);
        recurring[SlaMonitoringJob.RecurringJobId].ShouldBe("*/15 * * * *");
        recurring[StalePostingSweepJob.RecurringJobId].ShouldBe("*/5 * * * *");
    }

    [Fact]
    public async Task The_recurring_job_bodies_send_their_system_commands()
    {
        await using var host = NewHost();

        await host.InScopeAsync(async sp =>
        {
            await ActivatorUtilities.CreateInstance<SlaMonitoringJob>(sp).RunAsync();
            await ActivatorUtilities.CreateInstance<StalePostingSweepJob>(sp).RunAsync();
            return 0;
        });
    }
}
