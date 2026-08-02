using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aegis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AssetRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "assets");

            migrationBuilder.CreateTable(
                name: "Assets",
                schema: "assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Condition = table.Column<int>(type: "int", nullable: false),
                    Criticality = table.Column<int>(type: "int", nullable: false),
                    Latitude = table.Column<double>(type: "float(9)", precision: 9, scale: 6, nullable: true),
                    Longitude = table.Column<double>(type: "float(9)", precision: 9, scale: 6, nullable: true),
                    ParentAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Manufacturer = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ModelNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SerialNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    InstalledOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ExpectedLifespanYears = table.Column<int>(type: "int", nullable: true),
                    LastInspectedOnUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecommissionedOnUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
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
                    table.PrimaryKey("PK_Assets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Assets_Assets_ParentAssetId",
                        column: x => x.ParentAssetId,
                        principalSchema: "assets",
                        principalTable: "Assets",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AssetInspections",
                schema: "assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Condition = table.Column<int>(type: "int", nullable: false),
                    InspectedOnUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    InspectedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetInspections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetInspections_Assets_AssetId",
                        column: x => x.AssetId,
                        principalSchema: "assets",
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssetInspections_Asset_InspectedOn",
                schema: "assets",
                table: "AssetInspections",
                columns: new[] { "AssetId", "InspectedOnUtc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Assets_Latitude_Longitude",
                schema: "assets",
                table: "Assets",
                columns: new[] { "Latitude", "Longitude" });

            migrationBuilder.CreateIndex(
                name: "IX_Assets_Organization_LastInspected",
                schema: "assets",
                table: "Assets",
                columns: new[] { "OrganizationId", "LastInspectedOnUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Assets_Organization_Type_Status",
                schema: "assets",
                table: "Assets",
                columns: new[] { "OrganizationId", "Type", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Assets_ParentAssetId",
                schema: "assets",
                table: "Assets",
                column: "ParentAssetId",
                filter: "[ParentAssetId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Assets_Organization_Code",
                schema: "assets",
                table: "Assets",
                columns: new[] { "OrganizationId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssetInspections",
                schema: "assets");

            migrationBuilder.DropTable(
                name: "Assets",
                schema: "assets");
        }
    }
}
