using System.Text.Json;
using ClaimsModule.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ClaimsModule.API.Hosting;

/// <summary>
/// Connection settings for the health checks. A health check must fail fast, so it doesn't go
/// through EF Core's retrying execution strategy (which retries a failing connection for about a
/// minute, D-56): it opens its own short-timeout connection with no connection retries.
/// </summary>
internal sealed class HealthCheckDatabase(string connectionString)
{
    public static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(10);
    private const int ConnectTimeoutSeconds = 5;
    private const int CommandTimeoutSeconds = 5;

    private readonly string _connectionString = new SqlConnectionStringBuilder(connectionString)
    {
        ConnectTimeout = ConnectTimeoutSeconds,
        ConnectRetryCount = 0,
        ApplicationName = "ClaimsModule.HealthCheck",
    }.ConnectionString;

    public async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public static SqlCommand Command(SqlConnection connection, string sql) =>
        new(sql, connection) { CommandTimeout = CommandTimeoutSeconds };
}

/// <summary>Healthy when the database accepts a connection and answers a trivial query.</summary>
internal sealed class DatabaseHealthCheck(HealthCheckDatabase database) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext healthContext, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await database.OpenAsync(cancellationToken);
            await using var command = HealthCheckDatabase.Command(connection, "SELECT 1");
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or TimeoutException)
        {
            return HealthCheckResult.Unhealthy(ex.Message);
        }
    }
}

/// <summary>
/// Unhealthy when the database has migrations that haven't been applied. The deploy smoke test
/// (17e) therefore proves the pipeline really migrated Azure SQL. The expected list comes from the
/// compiled migrations (no database access); the applied list from __EFMigrationsHistory.
/// </summary>
internal sealed class MigrationsHealthCheck(ClaimsDbContext context, HealthCheckDatabase database) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext healthContext, CancellationToken cancellationToken = default)
    {
        var applied = new List<string>();
        try
        {
            await using var connection = await database.OpenAsync(cancellationToken);
            await using var command = HealthCheckDatabase.Command(connection,
                "IF OBJECT_ID(N'dbo.__EFMigrationsHistory') IS NOT NULL SELECT MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId");
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                applied.Add(reader.GetString(0));
            }
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or TimeoutException)
        {
            return HealthCheckResult.Unhealthy(ex.Message);
        }

        var pending = context.Database.GetMigrations().Except(applied, StringComparer.Ordinal).ToList();
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
            version = BuildInfo.Version,
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

/// <summary>Healthy when the document store is reachable (Blob container exists, or the local folder).
/// In Azure this also proves the Key Vault reference for the storage connection string resolved (31a).</summary>
internal sealed class StorageHealthCheck(ClaimsModule.Application.Abstractions.Storage.IStorageService storage) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await storage.CheckAsync(cancellationToken);
            return result.IsHealthy ? HealthCheckResult.Healthy(result.Description) : HealthCheckResult.Unhealthy(result.Description);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy(ex.GetType().Name + ": " + ex.Message);
        }
    }
}
