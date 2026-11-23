using System.Diagnostics;
using System.Net;
using System.Text.Json;
using ClaimsModule.IntegrationTests.Api;
using ClaimsModule.IntegrationTests.Persistence;
using Microsoft.Data.SqlClient;
using ClaimsModule.API.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ClaimsModule.IntegrationTests;

/// <summary>
/// The walking-skeleton host (P2.5 task 17a): liveness, a DB-backed health check that also
/// verifies migrations, Swagger, and fail-fast configuration checks.
/// </summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class ApiHostSmokeTests(SqlServerDatabaseFixture db)
{
    /// <summary>Every migration compiled into the Persistence assembly (so adding one never needs a test edit).</summary>
    private static readonly int CompiledMigrationCount = typeof(global::ClaimsModule.Persistence.ClaimsDbContext).Assembly.GetTypes()
        .Count(t => t.GetCustomAttributes(typeof(Microsoft.EntityFrameworkCore.Migrations.MigrationAttribute), inherit: false).Length > 0);

    [Fact]
    public async Task Liveness_endpoint_returns_200()
    {
        await using var factory = new ClaimsApiFactory(db);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("version").GetString().ShouldBe(BuildInfo.Version); // the smoke test waits for it
        BuildInfo.Version.ShouldContain("+"); // informational version carries the commit SHA
    }

    [Fact]
    public async Task Health_reports_database_and_migrations_healthy()
    {
        await using var factory = new ClaimsApiFactory(db);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        root.GetProperty("status").GetString().ShouldBe("Healthy");
        root.GetProperty("entries").GetProperty("database").GetProperty("status").GetString().ShouldBe("Healthy");
        var migrations = root.GetProperty("entries").GetProperty("migrations");
        migrations.GetProperty("status").GetString().ShouldBe("Healthy");
        migrations.GetProperty("data").GetProperty("pending").GetInt32().ShouldBe(0);
        migrations.GetProperty("data").GetProperty("applied").GetInt32().ShouldBe(CompiledMigrationCount);
    }

    [Theory]
    [InlineData("tcp:127.0.0.1,1")] // connection refused immediately
    [InlineData("tcp:192.0.2.1,1433")] // TEST-NET-1, never routed: the connect attempt hangs until its timeout
    public async Task Health_fails_fast_when_the_database_is_unreachable(string server)
    {
        // With the EF retrying strategy inside the checks this took 17 s and 85 s (measured, P2.5).
        var unreachable = new SqlConnectionStringBuilder(db.ConnectionString) { DataSource = server }.ConnectionString;
        await using var factory = new ClaimsApiFactory(db, new Dictionary<string, string?> { ["ConnectionStrings:ClaimsDb"] = unreachable, [StartupWarmUp.SettingKey] = "false" });
        using var client = factory.CreateClient();

        var stopwatch = Stopwatch.StartNew();
        var response = await client.GetAsync(new Uri("/health", UriKind.Relative));
        stopwatch.Stop();

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("status").GetString().ShouldBe("Unhealthy");
        json.RootElement.GetProperty("entries").GetProperty("database").GetProperty("status").GetString().ShouldBe("Unhealthy");
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(15)); // checks run in parallel, each capped at 10 s
    }

    [Fact]
    public async Task Swagger_document_and_ui_are_served()
    {
        await using var factory = new ClaimsApiFactory(db);
        using var client = factory.CreateClient();

        var document = await client.GetStringAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative));
        var ui = await client.GetAsync(new Uri("/swagger/index.html", UriKind.Relative));

        document.ShouldContain("Claims Module API");
        document.ShouldContain("/health/live");
        ui.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Root_redirects_to_swagger()
    {
        await using var factory = new ClaimsApiFactory(db);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync(new Uri("/", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().ShouldBe("/swagger");
    }

    [Fact]
    public void Startup_fails_fast_when_tenant_does_not_match_the_seeded_organisation()
    {
        // The host's own registrations, and the startup validator that ValidateOnStart registers: the
        // same check the host runs in StartAsync. Not via WebApplicationFactory.CreateClient(): when
        // startup throws there, the app's entry-point thread disposes the service provider while the
        // factory still reads it, and under CPU load the test saw ObjectDisposedException instead
        // (flaky; reproduced in 1 of 3 runs with every core busy, see ai-log).
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Tenancy:OrganisationId"] = Guid.NewGuid().ToString() })
            .Build();
        using var services = new ServiceCollection().AddClaimsHosting(configuration).BuildServiceProvider();

        var validator = services.GetRequiredService<IStartupValidator>();

        Should.Throw<OptionsValidationException>(validator.Validate).Message.ShouldContain("Tenancy:OrganisationId");
    }

    [Fact]
    public void Startup_validation_passes_for_the_seeded_organisation()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Tenancy:OrganisationId"] = SqlServerDatabaseFixture.OrganisationId.ToString() })
            .Build();
        using var services = new ServiceCollection().AddClaimsHosting(configuration).BuildServiceProvider();

        Should.NotThrow(services.GetRequiredService<IStartupValidator>().Validate);
    }

    [Fact]
    public async Task Startup_fails_fast_without_a_connection_string()
    {
        await using var factory = new ClaimsApiFactory(db, new Dictionary<string, string?> { ["ConnectionStrings:ClaimsDb"] = "" });

        Should.Throw<InvalidOperationException>(() => factory.CreateClient()).Message.ShouldContain("ConnectionStrings:ClaimsDb");
    }
}
