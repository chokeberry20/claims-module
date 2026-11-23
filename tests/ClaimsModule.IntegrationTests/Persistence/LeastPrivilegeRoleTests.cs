using Microsoft.Data.SqlClient;

namespace ClaimsModule.IntegrationTests.Persistence;

/// <summary>
/// db/scripts/app-role.sql (P2.5 task 17f) against the migrated test database. A user without a
/// login stands in for the API's managed identity (CREATE USER FROM EXTERNAL PROVIDER exists only on
/// Azure SQL); the tests impersonate it with EXECUTE AS USER and check what it can and cannot do.
/// </summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class LeastPrivilegeRoleTests(SqlServerDatabaseFixture db) : IAsyncLifetime
{
    private const string ProbeUser = "claims_app_probe";
    private const int PermissionDenied = 229;

    public async Task InitializeAsync()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(RepoRoot(), "db", "scripts", "app-role.sql"));
        await db.ExecuteAsync(script);
        await db.ExecuteAsync(script); // idempotent: a second run must not fail
        await db.ExecuteAsync($"""
            IF DATABASE_PRINCIPAL_ID(N'{ProbeUser}') IS NULL CREATE USER {ProbeUser} WITHOUT LOGIN;
            IF IS_ROLEMEMBER(N'claims_app', N'{ProbeUser}') = 0 ALTER ROLE claims_app ADD MEMBER {ProbeUser};
            """);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("dbo.Claims", "SELECT")]
    [InlineData("dbo.Claims", "INSERT")]
    [InlineData("dbo.Claims", "UPDATE")]
    [InlineData("dbo.Claims", "DELETE")]
    [InlineData("dbo.ClaimNumberSequences", "UPDATE")]
    [InlineData("dbo.ClaimAuditLog", "SELECT")]
    [InlineData("dbo.ClaimAuditLog", "INSERT")]
    public async Task App_role_has_data_access(string table, string permission)
    {
        (await HasPermissionAsync(table, permission)).ShouldBe(1);
    }

    [Theory]
    [InlineData("UPDATE")]
    [InlineData("DELETE")]
    public async Task App_role_cannot_change_or_delete_audit_entries(string permission)
    {
        (await HasPermissionAsync("dbo.ClaimAuditLog", permission)).ShouldBe(0);

        var sql = permission == "UPDATE"
            ? "UPDATE dbo.ClaimAuditLog SET EventType = EventType"
            : "DELETE FROM dbo.ClaimAuditLog";
        var error = await Should.ThrowAsync<SqlException>(() => RunAsProbeAsync(sql));

        // Refused by the permission check (229) before the append-only trigger (51000) is reached.
        error.Number.ShouldBe(PermissionDenied);
    }

    [Theory]
    [InlineData("CREATE TABLE dbo.PrivilegeProbe (Id int)", 2760)] // no ALTER on schema dbo
    [InlineData("ALTER TABLE dbo.Claims ADD PrivilegeProbe int NULL", 1088)] // no ALTER on the table
    [InlineData("DROP TABLE dbo.ClaimAuditLog", 3701)] // no CONTROL on the table
    public async Task App_role_cannot_change_the_application_schema(string ddl, int expectedError)
    {
        var error = await Should.ThrowAsync<SqlException>(() => RunAsProbeAsync(ddl));

        error.Number.ShouldBe(expectedError);
    }

    [Fact]
    public async Task App_role_can_create_tables_in_the_hangfire_schema()
    {
        await RunAsProbeAsync("CREATE TABLE HangFire.PrivilegeProbe (Id int); DROP TABLE HangFire.PrivilegeProbe;");
    }

    [Fact]
    public async Task App_role_has_no_server_or_database_wide_control()
    {
        (await ScalarAsProbeAsync<int>("SELECT IS_MEMBER('db_owner') + IS_MEMBER('db_ddladmin') + IS_MEMBER('db_datawriter')")).ShouldBe(0);
        (await ScalarAsProbeAsync<int>("SELECT HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'ALTER')")).ShouldBe(0);
    }

    private Task<int> HasPermissionAsync(string table, string permission) =>
        ScalarAsProbeAsync<int>($"SELECT HAS_PERMS_BY_NAME(N'{table}', 'OBJECT', '{permission}')");

    private async Task<T> ScalarAsProbeAsync<T>(string sql)
    {
        await using var connection = await OpenAsProbeAsync();
        await using var command = new SqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private async Task RunAsProbeAsync(string sql)
    {
        await using var connection = await OpenAsProbeAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<SqlConnection> OpenAsProbeAsync()
    {
        // Pooling off: an impersonated session must never go back to the pool.
        var builder = new SqlConnectionStringBuilder(db.ConnectionString) { Pooling = false };
        var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var impersonate = new SqlCommand($"EXECUTE AS USER = N'{ProbeUser}';", connection);
        await impersonate.ExecuteNonQueryAsync();
        return connection;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ClaimsModule.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("ClaimsModule.sln not found above test output directory.");
    }
}
