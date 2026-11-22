using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ClaimsModule.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedReferenceData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "CauseOfLossCodes",
                columns: new[] { "CauseOfLossCodeId", "Code", "CreatedAt", "DeletedAt", "IsActive", "Name", "OrganisationId", "PerilCategory", "SortOrder", "UpdatedAt", "UserCreated", "UserModified" },
                values: new object[,]
                {
                    { new Guid("0e0b415b-a154-8104-b800-f6d8a05f0ae6"), "COL-WIND", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, "Wind / Storm", new Guid("3f2b8c1e-6d4a-4e9b-a7c2-5d1e0f9b8a64"), "Weather", 80, null, null, null },
                    { new Guid("383fac17-4a98-8fc6-a29b-693cc88cdeb4"), "COL-VEH-COL", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, "Vehicle Collision", new Guid("3f2b8c1e-6d4a-4e9b-a7c2-5d1e0f9b8a64"), "Auto", 40, null, null, null },
                    { new Guid("3bc61cd4-c73d-8b59-957d-53faa0398ddc"), "COL-LIAB", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, "Third Party Liability", new Guid("3f2b8c1e-6d4a-4e9b-a7c2-5d1e0f9b8a64"), "Liability", 60, null, null, null },
                    { new Guid("3c9af9e4-008e-8d32-8fd5-860cf423fcbd"), "COL-THEFT", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, "Theft", new Guid("3f2b8c1e-6d4a-4e9b-a7c2-5d1e0f9b8a64"), "Crime", 30, null, null, null },
                    { new Guid("46549c37-d254-8dd5-8a49-ce73eac26c88"), "COL-FLOOD", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, "Flood", new Guid("3f2b8c1e-6d4a-4e9b-a7c2-5d1e0f9b8a64"), "Weather", 20, null, null, null },
                    { new Guid("867bd163-8091-8c53-8aa4-1ab764733da9"), "COL-EQUIP", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, "Equipment Breakdown", new Guid("3f2b8c1e-6d4a-4e9b-a7c2-5d1e0f9b8a64"), "Equipment", 70, null, null, null },
                    { new Guid("a203b2b1-fe5d-8135-a92b-e71d6571ac45"), "COL-INJURY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, "Bodily Injury", new Guid("3f2b8c1e-6d4a-4e9b-a7c2-5d1e0f9b8a64"), "Liability", 90, null, null, null },
                    { new Guid("aba0e4f8-34d3-8171-9a31-f6b9c5a1889f"), "COL-FIRE", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, "Fire", new Guid("3f2b8c1e-6d4a-4e9b-a7c2-5d1e0f9b8a64"), "Property", 10, null, null, null },
                    { new Guid("b1fb919c-f42d-8cbc-a910-ba8a9a9b2534"), "COL-OTHER", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, "Other / Unknown", new Guid("3f2b8c1e-6d4a-4e9b-a7c2-5d1e0f9b8a64"), "General", 100, null, null, null }
                });

            migrationBuilder.InsertData(
                table: "CauseOfLossCodes",
                columns: new[] { "CauseOfLossCodeId", "Code", "CreatedAt", "DeletedAt", "Name", "OrganisationId", "PerilCategory", "SortOrder", "UpdatedAt", "UserCreated", "UserModified" },
                values: new object[] { new Guid("c1752185-79d8-8d29-9a17-4e2b0e3c31a2"), "COL-LEGACY-SUBS", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Subsidence (legacy, retired)", new Guid("3f2b8c1e-6d4a-4e9b-a7c2-5d1e0f9b8a64"), "Property", 900, null, null, null });

            migrationBuilder.InsertData(
                table: "CauseOfLossCodes",
                columns: new[] { "CauseOfLossCodeId", "Code", "CreatedAt", "DeletedAt", "IsActive", "Name", "OrganisationId", "PerilCategory", "SortOrder", "UpdatedAt", "UserCreated", "UserModified" },
                values: new object[] { new Guid("e54f4051-c2ce-8202-89ab-02206b25b815"), "COL-VEH-COMP", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, "Vehicle Comprehensive", new Guid("3f2b8c1e-6d4a-4e9b-a7c2-5d1e0f9b8a64"), "Auto", 50, null, null, null });

            migrationBuilder.InsertData(
                table: "ClaimStatusTransitions",
                columns: new[] { "ClaimStatusTransitionId", "Description", "FromStatus", "RequiredPermission", "ToStatus" },
                values: new object[,]
                {
                    { new Guid("1cb480b3-fdf6-8919-8f9d-a3370950ebb6"), "No unresolved or unwaived Critical issues, all Warnings acknowledged, at least one Claimant, handler assigned", "Draft", "Handler", "Open" },
                    { new Guid("2fb9998a-47cd-8204-802a-83104409cb6e"), "At least one approved reserve transaction", "Open", "Handler", "PendingPayment" },
                    { new Guid("36c74ad0-00b4-8f6e-b601-d814c2cda8ec"), "Handler manually reverts", "UnderInvestigation", "Handler", "Open" }
                });

            migrationBuilder.InsertData(
                table: "ClaimStatusTransitions",
                columns: new[] { "ClaimStatusTransitionId", "Description", "FromStatus", "RequiredPermission", "RequiresReason", "ToStatus" },
                values: new object[] { new Guid("44b876d3-c64e-87d7-ac43-06f73771eb5d"), "Reopen reason provided; Supervisor role required", "Closed", "Supervisor", true, "Reopened" });

            migrationBuilder.InsertData(
                table: "ClaimStatusTransitions",
                columns: new[] { "ClaimStatusTransitionId", "Description", "FromStatus", "RequiredPermission", "ToStatus" },
                values: new object[,]
                {
                    { new Guid("48ac7583-674f-844f-97ed-c7492afd1a0a"), "At least one approved reserve transaction", "UnderInvestigation", "Handler", "PendingPayment" },
                    { new Guid("5e2b8ca4-d26a-8233-bfc2-7ff6973cc4aa"), "Immediately on reopen", "Reopened", "Supervisor", "Open" }
                });

            migrationBuilder.InsertData(
                table: "ClaimStatusTransitions",
                columns: new[] { "ClaimStatusTransitionId", "Description", "FromStatus", "RequiredPermission", "RequiresReason", "ToStatus" },
                values: new object[] { new Guid("5f16c8d2-ac9b-84c1-87c3-e50e02f6a3aa"), "Withdrawal reason provided", "UnderInvestigation", "Handler", true, "Withdrawn" });

            migrationBuilder.InsertData(
                table: "ClaimStatusTransitions",
                columns: new[] { "ClaimStatusTransitionId", "Description", "FromStatus", "RequiredPermission", "ToStatus" },
                values: new object[] { new Guid("8d3a7088-e8a1-8cc9-9130-1b5f7ce34e70"), "All closure conditions satisfied (CC-01..CC-04)", "Open", "Handler", "Closed" });

            migrationBuilder.InsertData(
                table: "ClaimStatusTransitions",
                columns: new[] { "ClaimStatusTransitionId", "Description", "FromStatus", "RequiredPermission", "RequiresReason", "ToStatus" },
                values: new object[] { new Guid("a333b102-bbe7-83e7-b1f3-2ec0ab420b06"), "Withdrawal reason provided", "Open", "Handler", true, "Withdrawn" });

            migrationBuilder.InsertData(
                table: "ClaimStatusTransitions",
                columns: new[] { "ClaimStatusTransitionId", "Description", "FromStatus", "RequiredPermission", "ToStatus" },
                values: new object[,]
                {
                    { new Guid("ad6d0e3f-d37f-807f-b7b8-c4a4b219ea88"), "No additional conditions", "Open", "Handler", "UnderInvestigation" },
                    { new Guid("dca4d7c1-d6c5-8489-b11c-e3d055477025"), "All closure conditions satisfied (CC-01..CC-04)", "PendingPayment", "Handler", "Closed" },
                    { new Guid("f0d4a9b9-d8c2-8272-8db5-3ab6dc09cd1b"), "All closure conditions satisfied (CC-01..CC-04)", "UnderInvestigation", "Handler", "Closed" }
                });

            migrationBuilder.InsertData(
                table: "Policies",
                columns: new[] { "PolicyId", "ClientName", "CreatedAt", "DeletedAt", "EffectiveDate", "ExpirationDate", "OrganisationId", "PolicyNumber", "UpdatedAt", "UserCreated", "UserModified", "CoverageTypes" },
                values: new object[,]
                {
                    { new Guid("030b49e8-6eff-8ceb-b232-cbc13593c216"), "Bluewater Marina Holdings", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, new DateOnly(2026, 3, 1), new DateOnly(2028, 2, 29), new Guid("3f2b8c1e-6d4a-4e9b-a7c2-5d1e0f9b8a64"), "POL-2026-003002", null, null, null, "[\"Property\",\"Liability\",\"Equipment\"]" },
                    { new Guid("13b458fc-40ac-87e5-a29d-d96e9cfc4258"), "Meridian Transport LLC", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, new DateOnly(2024, 1, 1), new DateOnly(2026, 12, 31), new Guid("3f2b8c1e-6d4a-4e9b-a7c2-5d1e0f9b8a64"), "POL-2024-001001", null, null, null, "[\"Vehicle\",\"Cargo\"]" },
                    { new Guid("5a2514ef-117c-81cb-a805-f18c2dc1c85a"), "Stanton Medical Group", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, new DateOnly(2025, 1, 1), new DateOnly(2026, 12, 31), new Guid("3f2b8c1e-6d4a-4e9b-a7c2-5d1e0f9b8a64"), "POL-2025-002002", null, null, null, "[\"Liability\",\"Vehicle\"]" },
                    { new Guid("72dd9d26-58c9-8523-80b3-08e511c0864b"), "Archived Corp", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, new DateOnly(2020, 1, 1), new DateOnly(2021, 12, 31), new Guid("3f2b8c1e-6d4a-4e9b-a7c2-5d1e0f9b8a64"), "POL-2023-000099", null, null, null, "[\"Property\"]" },
                    { new Guid("8624ba21-e892-832a-99a0-579cc34e987c"), "Northwind Logistics Ltd", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, new DateOnly(2026, 1, 1), new DateOnly(2027, 12, 31), new Guid("3f2b8c1e-6d4a-4e9b-a7c2-5d1e0f9b8a64"), "POL-2026-003001", null, null, null, "[\"Vehicle\",\"Cargo\"]" },
                    { new Guid("9853294c-0443-85d4-b328-010e41788d89"), "Coastal Builders Group", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, new DateOnly(2025, 3, 1), new DateOnly(2027, 2, 28), new Guid("3f2b8c1e-6d4a-4e9b-a7c2-5d1e0f9b8a64"), "POL-2025-002001", null, null, null, "[\"Property\",\"Equipment\"]" },
                    { new Guid("a13fcfc3-0ea9-8671-bd79-b31715a86394"), "Harborview Properties Inc", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, new DateOnly(2024, 6, 1), new DateOnly(2026, 5, 31), new Guid("3f2b8c1e-6d4a-4e9b-a7c2-5d1e0f9b8a64"), "POL-2024-001002", null, null, null, "[\"Property\",\"Liability\"]" },
                    { new Guid("ddea2f79-7c44-8857-9600-7113b98fb8ae"), "Summit Engineering Partners", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, new DateOnly(2025, 7, 1), new DateOnly(2027, 6, 30), new Guid("3f2b8c1e-6d4a-4e9b-a7c2-5d1e0f9b8a64"), "POL-2025-002010", null, null, null, "[\"Equipment\",\"Property\"]" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "CauseOfLossCodes",
                keyColumn: "CauseOfLossCodeId",
                keyValue: new Guid("0e0b415b-a154-8104-b800-f6d8a05f0ae6"));

            migrationBuilder.DeleteData(
                table: "CauseOfLossCodes",
                keyColumn: "CauseOfLossCodeId",
                keyValue: new Guid("383fac17-4a98-8fc6-a29b-693cc88cdeb4"));

            migrationBuilder.DeleteData(
                table: "CauseOfLossCodes",
                keyColumn: "CauseOfLossCodeId",
                keyValue: new Guid("3bc61cd4-c73d-8b59-957d-53faa0398ddc"));

            migrationBuilder.DeleteData(
                table: "CauseOfLossCodes",
                keyColumn: "CauseOfLossCodeId",
                keyValue: new Guid("3c9af9e4-008e-8d32-8fd5-860cf423fcbd"));

            migrationBuilder.DeleteData(
                table: "CauseOfLossCodes",
                keyColumn: "CauseOfLossCodeId",
                keyValue: new Guid("46549c37-d254-8dd5-8a49-ce73eac26c88"));

            migrationBuilder.DeleteData(
                table: "CauseOfLossCodes",
                keyColumn: "CauseOfLossCodeId",
                keyValue: new Guid("867bd163-8091-8c53-8aa4-1ab764733da9"));

            migrationBuilder.DeleteData(
                table: "CauseOfLossCodes",
                keyColumn: "CauseOfLossCodeId",
                keyValue: new Guid("a203b2b1-fe5d-8135-a92b-e71d6571ac45"));

            migrationBuilder.DeleteData(
                table: "CauseOfLossCodes",
                keyColumn: "CauseOfLossCodeId",
                keyValue: new Guid("aba0e4f8-34d3-8171-9a31-f6b9c5a1889f"));

            migrationBuilder.DeleteData(
                table: "CauseOfLossCodes",
                keyColumn: "CauseOfLossCodeId",
                keyValue: new Guid("b1fb919c-f42d-8cbc-a910-ba8a9a9b2534"));

            migrationBuilder.DeleteData(
                table: "CauseOfLossCodes",
                keyColumn: "CauseOfLossCodeId",
                keyValue: new Guid("c1752185-79d8-8d29-9a17-4e2b0e3c31a2"));

            migrationBuilder.DeleteData(
                table: "CauseOfLossCodes",
                keyColumn: "CauseOfLossCodeId",
                keyValue: new Guid("e54f4051-c2ce-8202-89ab-02206b25b815"));

            migrationBuilder.DeleteData(
                table: "ClaimStatusTransitions",
                keyColumn: "ClaimStatusTransitionId",
                keyValue: new Guid("1cb480b3-fdf6-8919-8f9d-a3370950ebb6"));

            migrationBuilder.DeleteData(
                table: "ClaimStatusTransitions",
                keyColumn: "ClaimStatusTransitionId",
                keyValue: new Guid("2fb9998a-47cd-8204-802a-83104409cb6e"));

            migrationBuilder.DeleteData(
                table: "ClaimStatusTransitions",
                keyColumn: "ClaimStatusTransitionId",
                keyValue: new Guid("36c74ad0-00b4-8f6e-b601-d814c2cda8ec"));

            migrationBuilder.DeleteData(
                table: "ClaimStatusTransitions",
                keyColumn: "ClaimStatusTransitionId",
                keyValue: new Guid("44b876d3-c64e-87d7-ac43-06f73771eb5d"));

            migrationBuilder.DeleteData(
                table: "ClaimStatusTransitions",
                keyColumn: "ClaimStatusTransitionId",
                keyValue: new Guid("48ac7583-674f-844f-97ed-c7492afd1a0a"));

            migrationBuilder.DeleteData(
                table: "ClaimStatusTransitions",
                keyColumn: "ClaimStatusTransitionId",
                keyValue: new Guid("5e2b8ca4-d26a-8233-bfc2-7ff6973cc4aa"));

            migrationBuilder.DeleteData(
                table: "ClaimStatusTransitions",
                keyColumn: "ClaimStatusTransitionId",
                keyValue: new Guid("5f16c8d2-ac9b-84c1-87c3-e50e02f6a3aa"));

            migrationBuilder.DeleteData(
                table: "ClaimStatusTransitions",
                keyColumn: "ClaimStatusTransitionId",
                keyValue: new Guid("8d3a7088-e8a1-8cc9-9130-1b5f7ce34e70"));

            migrationBuilder.DeleteData(
                table: "ClaimStatusTransitions",
                keyColumn: "ClaimStatusTransitionId",
                keyValue: new Guid("a333b102-bbe7-83e7-b1f3-2ec0ab420b06"));

            migrationBuilder.DeleteData(
                table: "ClaimStatusTransitions",
                keyColumn: "ClaimStatusTransitionId",
                keyValue: new Guid("ad6d0e3f-d37f-807f-b7b8-c4a4b219ea88"));

            migrationBuilder.DeleteData(
                table: "ClaimStatusTransitions",
                keyColumn: "ClaimStatusTransitionId",
                keyValue: new Guid("dca4d7c1-d6c5-8489-b11c-e3d055477025"));

            migrationBuilder.DeleteData(
                table: "ClaimStatusTransitions",
                keyColumn: "ClaimStatusTransitionId",
                keyValue: new Guid("f0d4a9b9-d8c2-8272-8db5-3ab6dc09cd1b"));

            migrationBuilder.DeleteData(
                table: "Policies",
                keyColumn: "PolicyId",
                keyValue: new Guid("030b49e8-6eff-8ceb-b232-cbc13593c216"));

            migrationBuilder.DeleteData(
                table: "Policies",
                keyColumn: "PolicyId",
                keyValue: new Guid("13b458fc-40ac-87e5-a29d-d96e9cfc4258"));

            migrationBuilder.DeleteData(
                table: "Policies",
                keyColumn: "PolicyId",
                keyValue: new Guid("5a2514ef-117c-81cb-a805-f18c2dc1c85a"));

            migrationBuilder.DeleteData(
                table: "Policies",
                keyColumn: "PolicyId",
                keyValue: new Guid("72dd9d26-58c9-8523-80b3-08e511c0864b"));

            migrationBuilder.DeleteData(
                table: "Policies",
                keyColumn: "PolicyId",
                keyValue: new Guid("8624ba21-e892-832a-99a0-579cc34e987c"));

            migrationBuilder.DeleteData(
                table: "Policies",
                keyColumn: "PolicyId",
                keyValue: new Guid("9853294c-0443-85d4-b328-010e41788d89"));

            migrationBuilder.DeleteData(
                table: "Policies",
                keyColumn: "PolicyId",
                keyValue: new Guid("a13fcfc3-0ea9-8671-bd79-b31715a86394"));

            migrationBuilder.DeleteData(
                table: "Policies",
                keyColumn: "PolicyId",
                keyValue: new Guid("ddea2f79-7c44-8857-9600-7113b98fb8ae"));
        }
    }
}
