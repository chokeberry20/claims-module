using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClaimsModule.Persistence.Migrations
{
    /// <summary>
    /// D-42: run with SQL Server 2022 behaviour (compatibility level 160), both on the local
    /// SQL Server 2025 Express instance and on Azure SQL. ALTER DATABASE can't run inside a
    /// transaction. EF recommends putting such an operation in its own migration, so a failure can't
    /// leave a larger migration half-applied. The statement is idempotent.
    /// </summary>
    public partial class SetCompatibilityLevel160 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER DATABASE CURRENT SET COMPATIBILITY_LEVEL = 160;", suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally a no-op: the previous level depends on the server, and leaving 160 is harmless.
        }
    }
}
