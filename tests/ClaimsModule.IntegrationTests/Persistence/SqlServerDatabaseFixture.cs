using ClaimsModule.Persistence;
using ClaimsModule.Persistence.Seed;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ClaimsModule.IntegrationTests.Persistence;

/// <summary>
/// A real SQL Server database for the persistence tests (D-42). Locally it is ClaimsModule_Tests on
/// .\SQLEXPRESS01 (testsettings.json). In CI, <c>ConnectionStrings__ClaimsDb</c> points at the SQL
/// Server 2022 service container. The database is dropped and re-created from the migrations once
/// per test run, which also proves the migrations rebuild a fresh database (S15.4).
/// </summary>
public sealed class SqlServerDatabaseFixture : IAsyncLifetime
{
    public SqlServerDatabaseFixture()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("testsettings.json", optional: false)
            .AddEnvironmentVariables()
            .Build();

        ConnectionString = configuration.GetConnectionString("ClaimsDb")
            ?? throw new InvalidOperationException("ConnectionStrings:ClaimsDb is not configured for tests.");

        var database = new SqlConnectionStringBuilder(ConnectionString).InitialCatalog;
        if (!database.EndsWith("_Tests", StringComparison.Ordinal) && Environment.GetEnvironmentVariable("CI") is null)
        {
            // Safety net: this fixture drops the database.
            throw new InvalidOperationException($"Refusing to drop non-test database '{database}'.");
        }
    }

    public string ConnectionString { get; }

    public static Guid OrganisationId => SeedData.DefaultOrganisationId;

    public DbContextOptions<ClaimsDbContext> Options(params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors) =>
        new DbContextOptionsBuilder<ClaimsDbContext>()
            .UseSqlServer(ConnectionString)
            .AddInterceptors(interceptors)
            .EnableSensitiveDataLogging()
            .Options;

    public ClaimsDbContext CreateContext(Guid? organisationId = null) =>
        new(Options(), new TestTenantProvider(organisationId ?? OrganisationId));

    public async Task InitializeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    public async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class SqlServerTestGroup : ICollectionFixture<SqlServerDatabaseFixture>
{
    public const string Name = "SQL Server database";
}
