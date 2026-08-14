using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevForge.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class PersistRunPlanPreview : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "PlanPreviewBodyChecksum",
            table: "ProjectRuns",
            type: "TEXT",
            maxLength: 71,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PlanPreviewJson",
            table: "ProjectRuns",
            type: "TEXT",
            maxLength: 1048576,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "PlanPreviewBodyChecksum",
            table: "ProjectRuns");

        migrationBuilder.DropColumn(
            name: "PlanPreviewJson",
            table: "ProjectRuns");
    }
}
