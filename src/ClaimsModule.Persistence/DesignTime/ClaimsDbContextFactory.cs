using ClaimsModule.Application.Abstractions;
using ClaimsModule.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ClaimsModule.Persistence.DesignTime;

/// <summary>
/// Used by <c>dotnet ef</c> (migrations add / database update / migrations script / bundle).
/// The connection string comes from the <c>ConnectionStrings__ClaimsDb</c> environment variable
/// (the CI service container or Azure SQL), and falls back to the local SQL Express instance (D-42).
/// </summary>
internal sealed class ClaimsDbContextFactory : IDesignTimeDbContextFactory<ClaimsDbContext>
{
    private const string LocalConnectionString =
        @"Server=.\SQLEXPRESS01;Database=ClaimsModule;Trusted_Connection=True;TrustServerCertificate=True";

    public ClaimsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__ClaimsDb") is { Length: > 0 } fromEnv
            ? fromEnv
            : LocalConnectionString;

        var options = new DbContextOptionsBuilder<ClaimsDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "dbo"))
            .Options;

        return new ClaimsDbContext(options, new DesignTimeTenantProvider());
    }

    private sealed class DesignTimeTenantProvider : ITenantProvider
    {
        public Guid OrganisationId => SeedData.DefaultOrganisationId;
    }
}
