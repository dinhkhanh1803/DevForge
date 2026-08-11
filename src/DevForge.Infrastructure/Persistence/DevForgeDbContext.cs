using DevForge.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevForge.Infrastructure.Persistence;

public sealed class DevForgeDbContext : DbContext
{
    public DevForgeDbContext(DbContextOptions<DevForgeDbContext> options)
        : base(options)
    {
    }

    internal DbSet<AppSettingEntity> AppSettings => Set<AppSettingEntity>();

    internal DbSet<IdeInstallationEntity> IdeInstallations => Set<IdeInstallationEntity>();

    internal DbSet<EnvironmentToolEntity> EnvironmentTools => Set<EnvironmentToolEntity>();

    internal DbSet<BlueprintEntity> Blueprints => Set<BlueprintEntity>();

    internal DbSet<TeamProfileEntity> TeamProfiles => Set<TeamProfileEntity>();

    internal DbSet<PresetEntity> Presets => Set<PresetEntity>();

    internal DbSet<ProjectRunEntity> ProjectRuns => Set<ProjectRunEntity>();

    internal DbSet<RunStepEntity> RunSteps => Set<RunStepEntity>();

    internal DbSet<RecentProjectEntity> RecentProjects => Set<RecentProjectEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ConfigureAppSettings(modelBuilder);
        ConfigureIdeInstallations(modelBuilder);
        ConfigureEnvironmentTools(modelBuilder);
        ConfigureBlueprints(modelBuilder);
        ConfigureTeamProfiles(modelBuilder);
        ConfigurePresets(modelBuilder);
        ConfigureProjectRuns(modelBuilder);
        ConfigureRunSteps(modelBuilder);
        ConfigureRecentProjects(modelBuilder);
    }

    private static void ConfigureAppSettings(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<AppSettingEntity>();
        entity.ToTable("AppSettings", table => table.HasCheckConstraint(
            "CK_AppSettings_ValueKind",
            "ValueKind IN ('Text', 'BooleanFlag', 'WholeNumber', 'JsonObject')"));
        entity.HasKey(item => item.Key);
        entity.Property(item => item.Key).HasMaxLength(128);
        entity.Property(item => item.ValueKind).HasMaxLength(32);
        entity.Property(item => item.SerializedValue).HasMaxLength(65_536);
    }

    private static void ConfigureIdeInstallations(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<IdeInstallationEntity>();
        entity.ToTable("IdeInstallations");
        entity.HasKey(item => item.Id);
        entity.Property(item => item.Id).HasMaxLength(128);
        entity.Property(item => item.Kind).HasMaxLength(64);
        entity.Property(item => item.ExecutablePath).HasMaxLength(1_024);
        entity.Property(item => item.Version).HasMaxLength(64);
        entity.Property(item => item.ValidationState).HasMaxLength(32);
    }

    private static void ConfigureEnvironmentTools(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<EnvironmentToolEntity>();
        entity.ToTable("EnvironmentTools");
        entity.HasKey(item => item.Id);
        entity.Property(item => item.Id).HasMaxLength(128);
        entity.Property(item => item.ExecutablePath).HasMaxLength(1_024);
        entity.Property(item => item.Version).HasMaxLength(64);
        entity.Property(item => item.Status).HasMaxLength(32);
        entity.HasIndex(item => item.ExpiresAtUnixMs);
    }

    private static void ConfigureBlueprints(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<BlueprintEntity>();
        entity.ToTable("Blueprints");
        entity.HasKey(item => new { item.Id, item.Version });
        entity.Property(item => item.Id).HasMaxLength(128);
        entity.Property(item => item.Version).HasMaxLength(64);
        entity.Property(item => item.Source).HasMaxLength(32);
        entity.Property(item => item.Trust).HasMaxLength(32);
        entity.Property(item => item.Checksum).HasMaxLength(64).IsFixedLength();
    }

    private static void ConfigureTeamProfiles(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<TeamProfileEntity>();
        entity.ToTable("TeamProfiles", table => table.HasCheckConstraint(
            "CK_TeamProfiles_SchemaVersion",
            "SchemaVersion > 0"));
        entity.HasKey(item => item.Id);
        entity.Property(item => item.Id).HasMaxLength(128);
        entity.Property(item => item.Name).HasMaxLength(200);
        entity.Property(item => item.PolicyJson).HasMaxLength(65_536);
    }

    private static void ConfigurePresets(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PresetEntity>();
        entity.ToTable("Presets", table => table.HasCheckConstraint(
            "CK_Presets_SchemaVersion",
            "SchemaVersion > 0"));
        entity.HasKey(item => item.Id);
        entity.Property(item => item.Id).HasMaxLength(128);
        entity.Property(item => item.Name).HasMaxLength(200);
        entity.Property(item => item.RecipeJson).HasMaxLength(65_536);
    }

    private static void ConfigureProjectRuns(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ProjectRunEntity>();
        entity.ToTable("ProjectRuns");
        entity.HasKey(item => item.Id);
        entity.Property(item => item.Id).HasMaxLength(128);
        entity.Property(item => item.RecipeId).HasMaxLength(128);
        entity.Property(item => item.Status).HasMaxLength(32);
        entity.Property(item => item.CurrentStepId).HasMaxLength(128);
        entity.Property(item => item.StagingPath).HasMaxLength(1_024);
        entity.Property(item => item.TargetPath).HasMaxLength(1_024);
        entity.Property(item => item.PlanHash).HasMaxLength(71);
        entity.Property(item => item.PlanJson).HasMaxLength(1_048_576);
        entity.Property(item => item.PlanBodyChecksum).HasMaxLength(71);
        entity.Property(item => item.BlueprintId).HasMaxLength(128);
        entity.Property(item => item.BlueprintVersion).HasMaxLength(64);
        entity.Property(item => item.BlueprintSourceId).HasMaxLength(128);
        entity.Property(item => item.BlueprintPackageDirectory).HasMaxLength(1_024);
        entity.Property(item => item.BlueprintTrust).HasMaxLength(32);
        entity.Property(item => item.BlueprintChecksum).HasMaxLength(71);
        entity.Property(item => item.StagingPayloadPath).HasMaxLength(1_024);
        entity.Property(item => item.OwnershipMarkerPath).HasMaxLength(1_024);
        entity.Property(item => item.OwnershipMarkerId).HasMaxLength(128);
        entity.Property(item => item.TargetParentRoot).HasMaxLength(1_024);
        entity.Property(item => item.CrossVolumeTemporaryPath).HasMaxLength(1_024);
        entity.Property(item => item.RunArtifactRoot).HasMaxLength(1_024);
        entity.Property(item => item.EvidenceJson).HasMaxLength(262_144);
        entity.Property(item => item.FinalizationState).HasMaxLength(32);
        entity.Property(item => item.ReportState).HasMaxLength(32);
        entity.Property(item => item.ErrorsJson).HasMaxLength(65_536);
        entity.HasIndex(item => new { item.Status, item.UpdatedAtUnixMs });
    }

    private static void ConfigureRunSteps(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<RunStepEntity>();
        entity.ToTable("RunSteps", table => table.HasCheckConstraint(
            "CK_RunSteps_AttemptNumber",
            "AttemptNumber > 0"));
        entity.HasKey(item => new { item.RunId, item.StepId, item.AttemptNumber });
        entity.Property(item => item.RunId).HasMaxLength(128);
        entity.Property(item => item.StepId).HasMaxLength(128);
        entity.Property(item => item.Outcome).HasMaxLength(32);
        entity.Property(item => item.OutputDigest).HasMaxLength(71);
        entity.HasIndex(item => new { item.RunId, item.SequenceNumber }).IsUnique();
        entity.Property(item => item.ErrorCode).HasMaxLength(64);
        entity.Property(item => item.ErrorSummary).HasMaxLength(1_024);
        entity.Property(item => item.ErrorTechnicalDetail).HasMaxLength(4_096);
        entity.Property(item => item.ErrorPhase).HasMaxLength(128);
        entity.Property(item => item.ErrorStepId).HasMaxLength(128);
        entity.Property(item => item.ErrorSuggestedActionsJson).HasMaxLength(16_384);
        entity.Property(item => item.ErrorContextJson).HasMaxLength(16_384);
        entity.HasOne(item => item.Run)
            .WithMany(run => run.Steps)
            .HasForeignKey(item => item.RunId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureRecentProjects(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<RecentProjectEntity>();
        entity.ToTable("RecentProjects");
        entity.HasKey(item => item.ProjectPath);
        entity.Property(item => item.ProjectPath).HasMaxLength(1_024);
        entity.Property(item => item.DisplayName).HasMaxLength(200);
        entity.Property(item => item.RepositoryUrl).HasMaxLength(2_048);
        entity.Property(item => item.IdeId).HasMaxLength(128);
        entity.HasIndex(item => item.LastOpenedAtUnixMs);
    }
}
