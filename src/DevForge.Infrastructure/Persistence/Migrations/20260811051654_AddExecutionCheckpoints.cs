using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevForge.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddExecutionCheckpoints : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "OutputDigest",
            table: "RunSteps",
            type: "TEXT",
            maxLength: 71,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "SequenceNumber",
            table: "RunSteps",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "BlueprintChecksum",
            table: "ProjectRuns",
            type: "TEXT",
            maxLength: 71,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "BlueprintId",
            table: "ProjectRuns",
            type: "TEXT",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "BlueprintPackageDirectory",
            table: "ProjectRuns",
            type: "TEXT",
            maxLength: 1024,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "BlueprintSourceId",
            table: "ProjectRuns",
            type: "TEXT",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "BlueprintTrust",
            table: "ProjectRuns",
            type: "TEXT",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "BlueprintVersion",
            table: "ProjectRuns",
            type: "TEXT",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "CrossVolumeTemporaryPath",
            table: "ProjectRuns",
            type: "TEXT",
            maxLength: 1024,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "EvidenceJson",
            table: "ProjectRuns",
            type: "TEXT",
            maxLength: 262144,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "FinalizationState",
            table: "ProjectRuns",
            type: "TEXT",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "OwnershipMarkerId",
            table: "ProjectRuns",
            type: "TEXT",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "OwnershipMarkerPath",
            table: "ProjectRuns",
            type: "TEXT",
            maxLength: 1024,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PlanBodyChecksum",
            table: "ProjectRuns",
            type: "TEXT",
            maxLength: 71,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PlanHash",
            table: "ProjectRuns",
            type: "TEXT",
            maxLength: 71,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PlanJson",
            table: "ProjectRuns",
            type: "TEXT",
            maxLength: 1048576,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ReportState",
            table: "ProjectRuns",
            type: "TEXT",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RunArtifactRoot",
            table: "ProjectRuns",
            type: "TEXT",
            maxLength: 1024,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "StagingPayloadPath",
            table: "ProjectRuns",
            type: "TEXT",
            maxLength: 1024,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "TargetParentRoot",
            table: "ProjectRuns",
            type: "TEXT",
            maxLength: 1024,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_RunSteps_RunId_SequenceNumber",
            table: "RunSteps",
            columns: ["RunId", "SequenceNumber"],
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_RunSteps_RunId_SequenceNumber",
            table: "RunSteps");

        migrationBuilder.DropColumn(
            name: "OutputDigest",
            table: "RunSteps");

        migrationBuilder.DropColumn(
            name: "SequenceNumber",
            table: "RunSteps");

        migrationBuilder.DropColumn(
            name: "BlueprintChecksum",
            table: "ProjectRuns");

        migrationBuilder.DropColumn(
            name: "BlueprintId",
            table: "ProjectRuns");

        migrationBuilder.DropColumn(
            name: "BlueprintPackageDirectory",
            table: "ProjectRuns");

        migrationBuilder.DropColumn(
            name: "BlueprintSourceId",
            table: "ProjectRuns");

        migrationBuilder.DropColumn(
            name: "BlueprintTrust",
            table: "ProjectRuns");

        migrationBuilder.DropColumn(
            name: "BlueprintVersion",
            table: "ProjectRuns");

        migrationBuilder.DropColumn(
            name: "CrossVolumeTemporaryPath",
            table: "ProjectRuns");

        migrationBuilder.DropColumn(
            name: "EvidenceJson",
            table: "ProjectRuns");

        migrationBuilder.DropColumn(
            name: "FinalizationState",
            table: "ProjectRuns");

        migrationBuilder.DropColumn(
            name: "OwnershipMarkerId",
            table: "ProjectRuns");

        migrationBuilder.DropColumn(
            name: "OwnershipMarkerPath",
            table: "ProjectRuns");

        migrationBuilder.DropColumn(
            name: "PlanBodyChecksum",
            table: "ProjectRuns");

        migrationBuilder.DropColumn(
            name: "PlanHash",
            table: "ProjectRuns");

        migrationBuilder.DropColumn(
            name: "PlanJson",
            table: "ProjectRuns");

        migrationBuilder.DropColumn(
            name: "ReportState",
            table: "ProjectRuns");

        migrationBuilder.DropColumn(
            name: "RunArtifactRoot",
            table: "ProjectRuns");

        migrationBuilder.DropColumn(
            name: "StagingPayloadPath",
            table: "ProjectRuns");

        migrationBuilder.DropColumn(
            name: "TargetParentRoot",
            table: "ProjectRuns");
    }
}
