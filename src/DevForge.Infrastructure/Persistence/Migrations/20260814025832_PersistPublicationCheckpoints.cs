using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevForge.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class PersistPublicationCheckpoints : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "PublicationBodyChecksum",
            table: "ProjectRuns",
            type: "TEXT",
            maxLength: 71,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PublicationJson",
            table: "ProjectRuns",
            type: "TEXT",
            maxLength: 16384,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "PublicationBodyChecksum",
            table: "ProjectRuns");

        migrationBuilder.DropColumn(
            name: "PublicationJson",
            table: "ProjectRuns");
    }
}
