using System.Diagnostics;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using ClaimsModule.Infrastructure.Jobs;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace ClaimsModule.API.Hosting;

/// <summary>
/// Azure Monitor OpenTelemetry (P4, your decision; not the classic App Insights SDK): requests,
/// dependencies (SQL, HTTP, Blob) and logs go to App Insights when its connection string is set,
/// which the Bicep template does in Azure. Locally and in tests it is off. Our job runs get their own
/// activity (<see cref="JobTelemetry"/>), and Hangfire's own polling SQL is dropped (D-71).
/// </summary>
internal static class Telemetry
{
    public const string ConnectionStringKey = "APPLICATIONINSIGHTS_CONNECTION_STRING";
    public const string LiveMetricsKey = "Telemetry:EnableLiveMetrics";

    public static IServiceCollection AddClaimsTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration[ConnectionStringKey]))
        {
            return services;
        }

        // Live Metrics streams to its own Microsoft endpoint; tests switch it off (no outbound calls).
        var liveMetrics = configuration.GetValue(LiveMetricsKey, defaultValue: true);
        services.AddOpenTelemetry().UseAzureMonitor(options => options.EnableLiveMetrics = liveMetrics);
        services.ConfigureOpenTelemetryTracerProvider(tracing => tracing
            .AddSource(JobTelemetry.ActivitySourceName)
            .AddProcessor(new BackgroundSqlNoiseFilter()));
        return services;
    }
}

/// <summary>
/// Drops SQL dependencies that have no parent operation (D-71). In this app every SQL call made on
/// behalf of a user or a job runs inside an operation: an HTTP request, or a job run's own activity.
/// A SQL call with no parent is background infrastructure: Hangfire's queue polling, distributed locks
/// (sp_getapplock), heartbeats and state bookkeeping, plus the one-off startup warm-up. In Azure they
/// were 3,343 of 3,364 SQL dependencies in a day, against the 1 GB/day Log Analytics cap.
/// <para>
/// The activity is unmarked as recorded when it <b>starts</b>, so the exporter, which only exports
/// recorded activities when they end, never sees it. This doesn't depend on the order of processors.
/// Pre-aggregated standard metrics may still count the call; they are cheap and not per-item.
/// </para>
/// </summary>
internal sealed class BackgroundSqlNoiseFilter : BaseProcessor<Activity>
{
    /// <summary>
    /// The distro vendors the SqlClient instrumentation, and its activities come from the distro's own
    /// source with this operation name (observed in P5; an integration test guards both, so a distro
    /// upgrade that changes them fails the build rather than silently letting the noise back in).
    /// Tags such as db.system aren't set yet when an activity starts, so names are all there is.
    /// </summary>
    public const string SqlClientSourceName = "Azure.Monitor.OpenTelemetry.AspNetCore";

    public const string SqlClientOperationName = "Azure.Monitor.OpenTelemetry.AspNetCore.Execute";

    public override void OnStart(Activity data)
    {
        if (IsBackgroundSql(data))
        {
            data.IsAllDataRequested = false;
            data.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;
        }
    }

    public static bool IsBackgroundSql(Activity activity) =>
        activity.Source.Name == SqlClientSourceName
        && activity.OperationName == SqlClientOperationName
        && activity.Parent is null
        && activity.ParentSpanId == default;
}
