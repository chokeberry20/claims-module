using System.Net;
using System.Text.Json;
using ClaimsModule.IntegrationTests.Api;
using ClaimsModule.IntegrationTests.Persistence;
using Microsoft.Extensions.Options;

namespace ClaimsModule.IntegrationTests;

/// <summary>
/// The walking-skeleton host (P2.5 task 17a): liveness, a DB-backed health check that also
/// verifies migrations, Swagger, and fail-fast configuration checks.
/// </summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class ApiHostSmokeTests(SqlServerDatabaseFixture db)
{
    [Fact]
    public async Task Liveness_endpoint_returns_200()
    {
        await using var factory = new ClaimsApiFactory(db);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
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
        migrations.GetProperty("data").GetProperty("applied").GetInt32().ShouldBe(4);
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
    public async Task Startup_fails_fast_when_tenant_does_not_match_the_seeded_organisation()
    {
        await using var factory = new ClaimsApiFactory(db, new Dictionary<string, string?> { ["Tenancy:OrganisationId"] = Guid.NewGuid().ToString() });

        Should.Throw<OptionsValidationException>(() => factory.CreateClient()).Message.ShouldContain("Tenancy:OrganisationId");
    }

    [Fact]
    public async Task Startup_fails_fast_without_a_connection_string()
    {
        await using var factory = new ClaimsApiFactory(db, new Dictionary<string, string?> { ["ConnectionStrings:ClaimsDb"] = "" });

        Should.Throw<InvalidOperationException>(() => factory.CreateClient()).Message.ShouldContain("ConnectionStrings:ClaimsDb");
    }
}
