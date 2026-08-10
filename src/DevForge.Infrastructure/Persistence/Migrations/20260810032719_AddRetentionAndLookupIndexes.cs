using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevForge.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddRetentionAndLookupIndexes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_RecentProjects_LastOpenedAtUnixMs",
            table: "RecentProjects",
            column: "LastOpenedAtUnixMs");

        migrationBuilder.CreateIndex(
            name: "IX_ProjectRuns_Status_UpdatedAtUnixMs",
            table: "ProjectRuns",
            columns: ["Status", "UpdatedAtUnixMs"]);

        migrationBuilder.CreateIndex(
            name: "IX_EnvironmentTools_ExpiresAtUnixMs",
            table: "EnvironmentTools",
            column: "ExpiresAtUnixMs");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_RecentProjects_LastOpenedAtUnixMs",
            table: "RecentProjects");

        migrationBuilder.DropIndex(
            name: "IX_ProjectRuns_Status_UpdatedAtUnixMs",
            table: "ProjectRuns");

        migrationBuilder.DropIndex(
            name: "IX_EnvironmentTools_ExpiresAtUnixMs",
            table: "EnvironmentTools");
    }
}
