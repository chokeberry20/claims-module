using System.Text.Json;
using ClaimsModule.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ClaimsModule.API.Hosting;

/// <summary>
/// Unhealthy when the database has migrations that haven't been applied. The deploy smoke test
/// (17e) therefore proves the pipeline really migrated Azure SQL.
/// </summary>
internal sealed class MigrationsHealthCheck(ClaimsDbContext context) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext healthContext, CancellationToken cancellationToken = default)
    {
        var applied = (await context.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
        var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

        var data = new Dictionary<string, object>
        {
            ["applied"] = applied.Count,
            ["pending"] = pending.Count,
            ["latest"] = applied.LastOrDefault() ?? "none",
        };

        return pending.Count == 0
            ? HealthCheckResult.Healthy($"{applied.Count} migrations applied.", data)
            : HealthCheckResult.Unhealthy($"{pending.Count} pending migrations: {string.Join(", ", pending)}.", data: data);
    }
}

internal static class HealthResponseWriter
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    /// <summary>JSON health report. It contains no secrets: no connection strings or exception details.</summary>
    public static Task WriteAsync(HttpContext httpContext, HealthReport report)
    {
        httpContext.Response.ContentType = "application/json";
        var body = new
        {
            status = report.Status.ToString(),
            totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds),
            version = typeof(HealthResponseWriter).Assembly.GetName().Version?.ToString(),
            entries = report.Entries.ToDictionary(
                e => e.Key,
                e => new
                {
                    status = e.Value.Status.ToString(),
                    description = e.Value.Description,
                    durationMs = Math.Round(e.Value.Duration.TotalMilliseconds),
                    data = e.Value.Data,
                }),
        };
        return httpContext.Response.WriteAsync(JsonSerializer.Serialize(body, Json));
    }
}
