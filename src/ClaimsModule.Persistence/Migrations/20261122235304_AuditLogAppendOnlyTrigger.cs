using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClaimsModule.Persistence.Migrations
{
    /// <summary>
    /// BR-A-01 / S14.2: makes ClaimAuditLog append-only at the database level. The SQL is inlined
    /// deliberately, so this migration stays a fixed snapshot even if code constants change later.
    /// </summary>
    public partial class AuditLogAppendOnlyTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TRIGGER [dbo].[TR_ClaimAuditLog_AppendOnly]
                ON [dbo].[ClaimAuditLog]
                INSTEAD OF UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51000, 'ClaimAuditLog is append-only: UPDATE and DELETE are not permitted (BR-A-01).', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS [dbo].[TR_ClaimAuditLog_AppendOnly];");
        }
    }
}
