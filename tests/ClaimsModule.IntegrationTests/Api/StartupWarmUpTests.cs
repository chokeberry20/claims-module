using System.Diagnostics;
using ClaimsModule.API.Hosting;
using ClaimsModule.IntegrationTests.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClaimsModule.IntegrationTests.Api;

/// <summary>
/// The startup warm-up (P3 deploy incident): it runs the /health checks once before the app serves,
/// never fails startup, and can be switched off.
/// </summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class StartupWarmUpTests(SqlServerDatabaseFixture db)
{
    private static IConfiguration Config(bool? enabled) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(enabled is null ? [] : new Dictionary<string, string?> { [StartupWarmUp.SettingKey] = enabled.Value.ToString() })
            .Build();

    [Fact]
    public async Task Warm_up_runs_the_health_checks_against_the_database()
    {
        await using var factory = new ClaimsApiFactory(db, new Dictionary<string, string?> { [StartupWarmUp.SettingKey] = "false" });
        _ = factory.Server; // start the host without its own warm-up

        (await StartupWarmUp.RunAsync(factory.Services, Config(enabled: null), NullLogger.Instance)).ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Warm_up_never_fails_startup_and_is_bounded_by_the_check_timeouts()
    {
        var unreachable = new SqlConnectionStringBuilder(db.ConnectionString) { DataSource = "tcp:192.0.2.1,1433" }.ConnectionString;
        await using var factory = new ClaimsApiFactory(db, new Dictionary<string, string?>
        {
            ["ConnectionStrings:ClaimsDb"] = unreachable,
            [StartupWarmUp.SettingKey] = "false",
        });
        _ = factory.Server;

        var stopwatch = Stopwatch.StartNew();
        var status = await StartupWarmUp.RunAsync(factory.Services, Config(enabled: true), NullLogger.Instance);

        status.ShouldBe(HealthStatus.Unhealthy);
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30)); // the 10 s per-check cap applies
    }

    [Fact]
    public async Task Warm_up_can_be_switched_off()
    {
        await using var factory = new ClaimsApiFactory(db, new Dictionary<string, string?> { [StartupWarmUp.SettingKey] = "false" });
        _ = factory.Server;

        (await StartupWarmUp.RunAsync(factory.Services, Config(enabled: false), NullLogger.Instance)).ShouldBeNull();
    }
}
