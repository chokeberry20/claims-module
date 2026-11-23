using ClaimsModule.IntegrationTests.Persistence;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;

namespace ClaimsModule.IntegrationTests.Api;

/// <summary>
/// Azure Monitor OpenTelemetry is on exactly when an App Insights connection string is configured (P4).
/// The "on" case lives in <see cref="TelemetryPipelineTests"/>, which keeps the test run to a single
/// telemetry-enabled host (see the note there).
/// </summary>
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
}
