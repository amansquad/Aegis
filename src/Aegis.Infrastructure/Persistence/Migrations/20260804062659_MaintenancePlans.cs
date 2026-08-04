using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aegis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MaintenancePlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "maintenance");

            migrationBuilder.AddColumn<Guid>(
                name: "MaintenancePlanId",
                schema: "workorders",
                table: "WorkOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MaintenancePlans",
                schema: "maintenance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    FrequencyDays = table.Column<int>(type: "int", nullable: false),
                    NextDueOnUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastCompletedOnUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedOnUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedOnUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenancePlans", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_MaintenancePlanId_Status",
                schema: "workorders",
                table: "WorkOrders",
                columns: new[] { "MaintenancePlanId", "Status" },
                filter: "[MaintenancePlanId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenancePlans_AssetId",
                schema: "maintenance",
                table: "MaintenancePlans",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenancePlans_Organization_Active_NextDue",
                schema: "maintenance",
                table: "MaintenancePlans",
                columns: new[] { "OrganizationId", "IsActive", "NextDueOnUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_MaintenancePlans_Organization_Reference",
                schema: "maintenance",
                table: "MaintenancePlans",
                columns: new[] { "OrganizationId", "Reference" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MaintenancePlans",
                schema: "maintenance");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_MaintenancePlanId_Status",
                schema: "workorders",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "MaintenancePlanId",
                schema: "workorders",
                table: "WorkOrders");
        }
    }
}
