using ClaimsModule.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ClaimsModule.IntegrationTests.Api;

/// <summary>
/// The real API host on the integration test database (never the dev database). The "Testing"
/// environment means appsettings.Development.json isn't loaded. Extra settings can be passed in,
/// for example to prove the fail-fast startup checks.
/// </summary>
internal sealed class ClaimsApiFactory(SqlServerDatabaseFixture db, IReadOnlyDictionary<string, string?>? overrides = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:ClaimsDb", db.ConnectionString);
        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(key, value);
        }
    }
}
