using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ClaimsModule.API.Hosting;

/// <summary>
/// Runs the /health checks once before the app starts serving, so the first managed-identity token,
/// the first SQL connection and the EF model are ready by the time App Service's warm-up probe
/// succeeds. In Azure the first /health had taken 11.6 s (migrations check 10.4 s against its 10 s
/// cap). The checks keep their own short timeouts; a failure here is logged, never fatal: the app
/// still starts and /health reports the problem (D-65). Configuration: Database:WarmUpOnStartup.
/// </summary>
internal static partial class StartupWarmUp
{
    public const string SettingKey = "Database:WarmUpOnStartup";

    public static async Task<HealthStatus?> RunAsync(IServiceProvider services, IConfiguration configuration, ILogger logger)
    {
        if (!configuration.GetValue(SettingKey, defaultValue: true))
        {
            return null;
        }

        try
        {
            var report = await services.GetRequiredService<HealthCheckService>().CheckHealthAsync();
            LogWarmUp(logger, report.Status, report.TotalDuration.TotalMilliseconds);
            return report.Status;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogWarmUpFailed(logger, ex);
            return HealthStatus.Unhealthy;
        }
    }

    /// <summary>
    /// Runs the warm-up as the host starts, not before <c>RunAsync</c> (P5): App Insights never received
    /// the line when it was logged before the host started (the OpenTelemetry pipeline saw it, the Azure
    /// Monitor exporter didn't send it), while the host's own start-up lines always arrived. Hosted
    /// services start before the web server listens, so the app is still warm before the first request.
    /// </summary>
    internal sealed class HostedService(IServiceProvider services, IConfiguration configuration, ILogger<HostedService> logger) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => RunAsync(services, configuration, logger);

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Startup warm-up: database health {Status} in {ElapsedMs:0} ms")]
    private static partial void LogWarmUp(ILogger logger, HealthStatus status, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Startup warm-up failed; the app starts anyway and /health reports the problem")]
    private static partial void LogWarmUpFailed(ILogger logger, Exception exception);
}
