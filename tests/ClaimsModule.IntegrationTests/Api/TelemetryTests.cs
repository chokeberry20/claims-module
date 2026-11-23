using System.Net;
using ClaimsModule.IntegrationTests.Persistence;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;

namespace ClaimsModule.IntegrationTests.Api;

/// <summary>Azure Monitor OpenTelemetry is on exactly when an App Insights connection string is configured (P4).</summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class TelemetryTests(SqlServerDatabaseFixture db)
{
    [Fact]
    public async Task Telemetry_is_off_without_a_connection_string()
    {
        await using var factory = new ClaimsApiFactory(db);
        using var client = factory.CreateClient();

        factory.Services.GetService<TracerProvider>().ShouldBeNull();
    }

    [Fact]
    public async Task Telemetry_is_on_with_a_connection_string_and_the_app_still_serves()
    {
        // A dummy endpoint: the exporter's background sends fail quietly, nothing leaves the machine.
        await using var factory = new ClaimsApiFactory(db, new Dictionary<string, string?>
        {
            ["APPLICATIONINSIGHTS_CONNECTION_STRING"] = "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://127.0.0.1:1/",
        });
        using var client = factory.CreateClient();

        factory.Services.GetService<TracerProvider>().ShouldNotBeNull();
        (await client.GetAsync(new Uri("/health/live", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
