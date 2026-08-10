using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevForge.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialPersistenceSchema : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AppSettings",
            columns: table => new
            {
                Key = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                ValueKind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                SerializedValue = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: false),
                UpdatedAtUnixMs = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AppSettings", x => x.Key);
                table.CheckConstraint("CK_AppSettings_ValueKind", "ValueKind IN ('Text', 'BooleanFlag', 'WholeNumber', 'JsonObject')");
            });

        migrationBuilder.CreateTable(
            name: "Blueprints",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                Version = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                Source = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                Trust = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                Checksum = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                IsDisabled = table.Column<bool>(type: "INTEGER", nullable: false),
                DiscoveredAtUnixMs = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Blueprints", x => new { x.Id, x.Version });
            });

        migrationBuilder.CreateTable(
            name: "EnvironmentTools",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                ExecutablePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                Version = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                ScannedAtUnixMs = table.Column<long>(type: "INTEGER", nullable: false),
                ExpiresAtUnixMs = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EnvironmentTools", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "IdeInstallations",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                Kind = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                ExecutablePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                Version = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                ValidationState = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                ScannedAtUnixMs = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_IdeInstallations", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Presets",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                RecipeJson = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: false),
                UpdatedAtUnixMs = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Presets", x => x.Id);
                table.CheckConstraint("CK_Presets_SchemaVersion", "SchemaVersion > 0");
            });

        migrationBuilder.CreateTable(
            name: "ProjectRuns",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                RecipeId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                CurrentStepId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                CreatedAtUnixMs = table.Column<long>(type: "INTEGER", nullable: false),
                UpdatedAtUnixMs = table.Column<long>(type: "INTEGER", nullable: false),
                CompletedAtUnixMs = table.Column<long>(type: "INTEGER", nullable: true),
                StagingPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                TargetPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                ErrorsJson = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProjectRuns", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "RecentProjects",
            columns: table => new
            {
                ProjectPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                RepositoryUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                IdeId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                LastOpenedAtUnixMs = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RecentProjects", x => x.ProjectPath);
            });

        migrationBuilder.CreateTable(
            name: "TeamProfiles",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                PolicyJson = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: false),
                UpdatedAtUnixMs = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TeamProfiles", x => x.Id);
                table.CheckConstraint("CK_TeamProfiles_SchemaVersion", "SchemaVersion > 0");
            });

        migrationBuilder.CreateTable(
            name: "RunSteps",
            columns: table => new
            {
                RunId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                StepId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                AttemptNumber = table.Column<int>(type: "INTEGER", nullable: false),
                Outcome = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                StartedAtUnixMs = table.Column<long>(type: "INTEGER", nullable: false),
                CompletedAtUnixMs = table.Column<long>(type: "INTEGER", nullable: true),
                ExitCode = table.Column<int>(type: "INTEGER", nullable: true),
                ErrorCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                ErrorSummary = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                ErrorTechnicalDetail = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                ErrorPhase = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                ErrorStepId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                ErrorIsRetryable = table.Column<bool>(type: "INTEGER", nullable: true),
                ErrorSuggestedActionsJson = table.Column<string>(type: "TEXT", maxLength: 16384, nullable: true),
                ErrorContextJson = table.Column<string>(type: "TEXT", maxLength: 16384, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RunSteps", x => new { x.RunId, x.StepId, x.AttemptNumber });
                table.CheckConstraint("CK_RunSteps_AttemptNumber", "AttemptNumber > 0");
                table.ForeignKey(
                    name: "FK_RunSteps_ProjectRuns_RunId",
                    column: x => x.RunId,
                    principalTable: "ProjectRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AppSettings");

        migrationBuilder.DropTable(
            name: "Blueprints");

        migrationBuilder.DropTable(
            name: "EnvironmentTools");

        migrationBuilder.DropTable(
            name: "IdeInstallations");

        migrationBuilder.DropTable(
            name: "Presets");

        migrationBuilder.DropTable(
            name: "RecentProjects");

        migrationBuilder.DropTable(
            name: "RunSteps");

        migrationBuilder.DropTable(
            name: "TeamProfiles");

        migrationBuilder.DropTable(
            name: "ProjectRuns");
    }
}
