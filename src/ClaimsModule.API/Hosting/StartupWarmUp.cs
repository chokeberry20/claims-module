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

    [LoggerMessage(Level = LogLevel.Information, Message = "Startup warm-up: database health {Status} in {ElapsedMs:0} ms")]
    private static partial void LogWarmUp(ILogger logger, HealthStatus status, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Startup warm-up failed; the app starts anyway and /health reports the problem")]
    private static partial void LogWarmUpFailed(ILogger logger, Exception exception);
}
