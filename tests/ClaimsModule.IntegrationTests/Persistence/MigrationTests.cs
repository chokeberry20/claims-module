using Microsoft.EntityFrameworkCore;

namespace ClaimsModule.IntegrationTests.Persistence;

/// <summary>The migrations rebuild a fresh database with the S15.1 column types (S15.4).</summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class MigrationTests(SqlServerDatabaseFixture db)
{
    [Fact]
    public async Task Migrations_are_fully_applied_and_match_the_model()
    {
        await using var context = db.CreateContext();

        (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        context.Database.HasPendingModelChanges().ShouldBeFalse("the model has changes that are not in a migration");
    }

    [Fact]
    public async Task D_42_database_runs_at_compatibility_level_160() =>
        (await db.ScalarAsync<byte>("SELECT compatibility_level FROM sys.databases WHERE name = DB_NAME()")).ShouldBe((byte)160);

    [Theory]
    [InlineData("Claims", "ClaimId", "uniqueidentifier")]
    [InlineData("Claims", "ClaimNumber", "nvarchar(50)")]
    [InlineData("Claims", "Status", "nvarchar(50)")]
    [InlineData("Claims", "ReportedDate", "datetimeoffset(7)")]
    [InlineData("Claims", "RowVer", "timestamp")]
    [InlineData("Claims", "Notes", "nvarchar(max)")]
    [InlineData("Claims", "OrganisationId", "uniqueidentifier")]
    [InlineData("Claims", "IsDeleted", "bit")]
    [InlineData("ClaimReserveComponents", "RowVer", "timestamp")]
    [InlineData("ClaimReserveComponents", "Component", "nvarchar(50)")]
    [InlineData("ClaimReserveComponents", "CurrentAmount", "decimal(19,4)")]
    [InlineData("ReserveHistory", "Amount", "decimal(19,4)")]
    [InlineData("ReserveHistory", "PreviousBalance", "decimal(19,4)")]
    [InlineData("ReserveHistory", "IdempotencyKey", "nvarchar(200)")]
    [InlineData("LossEvents", "LossDate", "datetimeoffset(7)")]
    [InlineData("LossEvents", "EstimatedLossAmount", "decimal(19,4)")]
    [InlineData("LossEvents", "LossLocation", "nvarchar(500)")]
    [InlineData("ClaimParties", "PartyType", "nvarchar(20)")]
    [InlineData("ClaimDocuments", "FileSizeBytes", "bigint")]
    [InlineData("ClaimAuditLog", "OldValue", "nvarchar(max)")]
    [InlineData("Policies", "EffectiveDate", "date")]
    [InlineData("Policies", "CoverageTypes", "nvarchar(max)")]
    public async Task Column_has_the_S15_type(string table, string column, string expectedType)
    {
        var actual = await db.ScalarAsync<string>($"""
            SELECT DATA_TYPE
                 + CASE
                     WHEN DATA_TYPE IN ('nvarchar','varchar') THEN '(' + CASE WHEN CHARACTER_MAXIMUM_LENGTH = -1 THEN 'max' ELSE CAST(CHARACTER_MAXIMUM_LENGTH AS varchar(10)) END + ')'
                     WHEN DATA_TYPE = 'decimal' THEN '(' + CAST(NUMERIC_PRECISION AS varchar(5)) + ',' + CAST(NUMERIC_SCALE AS varchar(5)) + ')'
                     WHEN DATA_TYPE = 'datetimeoffset' THEN '(' + CAST(DATETIME_PRECISION AS varchar(5)) + ')'
                     ELSE '' END
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = '{table}' AND COLUMN_NAME = '{column}'
            """);

        actual.ShouldBe(expectedType);
    }

    [Theory]
    [InlineData("Claims", "ClaimId")]
    [InlineData("ReserveHistory", "ReserveHistoryId")]
    [InlineData("ClaimAuditLog", "AuditLogId")]
    public async Task Primary_key_defaults_to_newsequentialid(string table, string column) =>
        (await db.ScalarAsync<string>($"SELECT COLUMN_DEFAULT FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}' AND COLUMN_NAME = '{column}'"))
            .ShouldBe("(newsequentialid())");

    [Fact]
    public async Task No_foreign_key_cascades_on_delete_in_the_database() =>
        (await db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.foreign_keys WHERE delete_referential_action <> 0")).ShouldBe(0);
}
