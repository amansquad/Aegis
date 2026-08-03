using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aegis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Incidents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "incidents");

            migrationBuilder.CreateTable(
                name: "Incidents",
                schema: "incidents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ReportText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    Severity = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PublicSafetyRisk = table.Column<bool>(type: "bit", nullable: false),
                    ClassificationMethod = table.Column<int>(type: "int", nullable: false),
                    ClassificationConfidence = table.Column<double>(type: "float", nullable: true),
                    ProposedCategory = table.Column<int>(type: "int", nullable: true),
                    ProposedSeverity = table.Column<int>(type: "int", nullable: true),
                    LocationHint = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Latitude = table.Column<double>(type: "float(9)", precision: 9, scale: 6, nullable: true),
                    Longitude = table.Column<double>(type: "float(9)", precision: 9, scale: 6, nullable: true),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReporterName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ReporterContact = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ReportedOnUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TriagedOnUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TriagedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResolvedOnUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ResolvedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResolutionNotes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DuplicateOfIncidentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    table.PrimaryKey("PK_Incidents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_AssetId",
                schema: "incidents",
                table: "Incidents",
                column: "AssetId",
                filter: "[AssetId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_Latitude_Longitude",
                schema: "incidents",
                table: "Incidents",
                columns: new[] { "Latitude", "Longitude" });

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_Organization_Category_Reported",
                schema: "incidents",
                table: "Incidents",
                columns: new[] { "OrganizationId", "Category", "ReportedOnUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_Organization_Status_Reported",
                schema: "incidents",
                table: "Incidents",
                columns: new[] { "OrganizationId", "Status", "ReportedOnUtc" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "UX_Incidents_Organization_Reference",
                schema: "incidents",
                table: "Incidents",
                columns: new[] { "OrganizationId", "Reference" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Incidents",
                schema: "incidents");
        }
    }
}
