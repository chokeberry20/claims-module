using ClaimsModule.Application.Abstractions.Jobs;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClaimsModule.Infrastructure.Jobs;

public static class JobsRegistration
{
    public const string SchemaName = "HangFire";

    /// <summary>
    /// Hangfire on SQL Server (schema <c>HangFire</c>, D-39, D-62) with Microsoft.Data.SqlClient, so the
    /// managed-identity connection string works. Replaces any other IJobScheduler. Retries follow
    /// <see cref="JobsOptions"/>; the final-failure filter records GL_POSTING_FAILED.
    /// </summary>
    public static IServiceCollection AddClaimsJobs(this IServiceCollection services, string connectionString, JobsOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(options);
        services.AddHangfire((sp, config) =>
        {
            config
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
                {
                    SchemaName = SchemaName,
                    PrepareSchemaIfNecessary = true,
                    QueuePollInterval = options.QueuePollInterval,
                    SqlClientFactory = Microsoft.Data.SqlClient.SqlClientFactory.Instance,
                    DisableGlobalLocks = true,
                });

            // Process-global filter: add it once, and point it at this host's services.
            GlPostingFinalFailureFilter.Instance.Attach(sp);
            if (!GlobalJobFilters.Filters.Any(f => f.Instance is GlPostingFinalFailureFilter))
            {
                config.UseFilter(GlPostingFinalFailureFilter.Instance);
            }

            // Configure the global retry filter (one instance, default 10 attempts) instead of adding a second one.
            var retry = GlobalJobFilters.Filters.Select(f => f.Instance).OfType<AutomaticRetryAttribute>().Single();
            retry.Attempts = options.RetryAttempts;
            retry.DelaysInSeconds = options.RetryDelaysSeconds;
        });

        if (options.ServerEnabled)
        {
            services.AddHangfireServer(server =>
            {
                server.WorkerCount = options.WorkerCount;
                server.SchedulePollingInterval = options.QueuePollInterval;
            });
        }

        services.Replace(ServiceDescriptor.Scoped<IJobScheduler, HangfireJobScheduler>());
        return services;
    }

    /// <summary>Registers (or updates) the recurring jobs: SLA scan every 15 minutes, stale-posting sweep every 5.</summary>
    public static void RegisterRecurringJobs(IRecurringJobManager recurring)
    {
        recurring.AddOrUpdate<SlaMonitoringJob>(SlaMonitoringJob.RecurringJobId, j => j.RunAsync(), SlaMonitoringJob.Cron);
        recurring.AddOrUpdate<StalePostingSweepJob>(StalePostingSweepJob.RecurringJobId, j => j.RunAsync(), StalePostingSweepJob.Cron);
    }
}
