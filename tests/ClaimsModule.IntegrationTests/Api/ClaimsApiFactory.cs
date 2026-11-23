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
    public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), "claims-tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:ClaimsDb", db.ConnectionString);
        // Local document storage in a per-factory temp folder (appsettings.json defaults to AzureBlob).
        builder.UseSetting("Storage:Provider", "LocalFileSystem");
        builder.UseSetting("Storage:LocalFileSystem:RootPath", StorageRoot);
        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(key, value);
        }
    }
}
