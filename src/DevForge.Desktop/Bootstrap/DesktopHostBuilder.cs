using System.IO;
using DevForge.Application.Contracts;
using DevForge.Application.Contracts.Persistence;
using DevForge.Application.Creation;
using DevForge.Application.Execution;
using DevForge.Application.Planning;
using DevForge.Application.Planning.CompatibilityRules;
using DevForge.Application.Publication;
using DevForge.Desktop.BlueprintCatalog;
using DevForge.Desktop.CreateProject;
using DevForge.Desktop.Dashboard;
using DevForge.Desktop.EnvironmentDoctor;
using DevForge.Desktop.Execution;
using DevForge.Desktop.Navigation;
using DevForge.Desktop.Notifications;
using DevForge.Desktop.RunHistory;
using DevForge.Desktop.Settings;
using DevForge.Desktop.Shell;
using DevForge.Desktop.Theming;
using DevForge.Infrastructure.Blueprints;
using DevForge.Infrastructure.Creation;
using DevForge.Infrastructure.Execution;
using DevForge.Infrastructure.FileSystem;
using DevForge.Infrastructure.Git;
using DevForge.Infrastructure.GitHub;
using DevForge.Infrastructure.Ide;
using DevForge.Infrastructure.Persistence;
using DevForge.Infrastructure.Persistence.Migrations;
using DevForge.Infrastructure.Persistence.Repositories;
using DevForge.Infrastructure.Processes;
using DevForge.Infrastructure.Publication;
using DevForge.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DevForge.Desktop.Bootstrap;

public static class DesktopHostBuilder
{
    public static IHost Create(
        string localDataRoot,
        IThemeResourceHost themeResourceHost,
        Action<IServiceCollection>? configureOverrides = null)
    {
        ArgumentNullException.ThrowIfNull(themeResourceHost);
        var location = DatabaseLocation.Create(localDataRoot, "devforge.db");
        if (!location.IsValid)
        {
            throw new ArgumentException(
                "A canonical local application data root is required.",
                nameof(localDataRoot));
        }

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        RegisterServices(builder.Services, location.Value, themeResourceHost);
        configureOverrides?.Invoke(builder.Services);
        return builder.Build();
    }

    private static void RegisterServices(
        IServiceCollection services,
        DatabaseLocation location,
        IThemeResourceHost themeResourceHost)
    {
        services.AddSingleton(location);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<DevForgeDbContextFactory>();
        services.AddSingleton<ILocalDataRootProvisioner, LocalDataRootProvisioner>();
        services.AddSingleton<ISqliteBackupTransport, SqliteBackupTransport>();
        services.AddSingleton<IDatabaseMigrationExecutor, EfDatabaseMigrationExecutor>();
        services.AddSingleton<SqliteMigrationCoordinator>();

        services.AddSingleton<IAppSettingsStore, AppSettingsStore>();
        services.AddSingleton<IEnvironmentToolStore, EnvironmentToolStore>();
        services.AddSingleton<IRecentProjectStore, RecentProjectStore>();
        services.AddSingleton<IPresetStore, PresetStore>();
        services.AddSingleton<IRunCheckpointStore, SqliteRunCheckpointStore>();
        services.AddSingleton<IBlueprintMetadataStore, BlueprintMetadataStore>();

        services.AddSingleton<IFileSystem, WindowsFileSystem>();
        services.AddSingleton<IProjectLocationProbe, GuardedProjectLocationProbe>();
        services.AddSingleton<IProcessRunner, WindowsProcessRunner>();
        services.AddSingleton<IIdeLauncher, WindowsIdeLauncher>();
        services.AddSingleton<IEnvironmentDoctor, DeferredEnvironmentDoctor>();

        var workspaceRoot = WorkspaceRoot.Create(location.LocalDataRoot);
        if (!workspaceRoot.IsValid)
        {
            throw new InvalidOperationException("The application data workspace root is invalid.");
        }

        services.AddSingleton(workspaceRoot.Value);
        services.AddSingleton<DesktopBlueprintSourceRegistry>();
        services.AddSingleton<IEnumerable<BlueprintPackageSource>>(provider =>
            provider.GetRequiredService<DesktopBlueprintSourceRegistry>());
        services.AddSingleton<DevForge.Infrastructure.Blueprints.BlueprintCatalog>();
        services.AddSingleton<IBlueprintCatalog>(provider =>
            provider.GetRequiredService<DevForge.Infrastructure.Blueprints.BlueprintCatalog>());
        services.AddSingleton<IBlueprintExecutionSource, BlueprintExecutionSource>();
        services.AddSingleton<IBlueprintRecoveryInspector, BlueprintRecoveryInspector>();
        services.AddSingleton<IInputSchemaValidator, InputSchemaValidator>();
        services.AddSingleton<ICompatibilityRuleEvaluator, CompatibilityRuleEvaluator>();
        services.AddSingleton<IVariableTemplateResolver, VariableTemplateResolver>();
        services.AddSingleton<IPlanningRuntimeContextProvider, DesktopPlanningRuntimeContextProvider>();
        services.AddSingleton<IProjectPlanner, ProjectPlanner>();

        services.AddSingleton<WindowsProjectTargetService>();
        services.AddSingleton<IProjectTargetPreflight>(provider =>
            provider.GetRequiredService<WindowsProjectTargetService>());
        services.AddSingleton<IProjectExecutionWorkspaceFactory>(provider =>
            provider.GetRequiredService<WindowsProjectTargetService>());
        services.AddSingleton<IProjectRecoveryWorkspaceFactory>(provider =>
            provider.GetRequiredService<WindowsProjectTargetService>());
        services.AddSingleton<IRunIdentityGenerator, GuidRunIdentityGenerator>();
        services.AddSingleton<IStagingWorkspaceManager, OwnedStagingWorkspaceManager>();
        services.AddSingleton(provider => new ClosedExecutionHandlerRegistryProvider(
            provider.GetRequiredService<IProcessRunner>()));
        services.AddSingleton<IExecutionHandlerRegistryProvider>(provider =>
            provider.GetRequiredService<ClosedExecutionHandlerRegistryProvider>());
        services.AddSingleton<ISecretScanner, WorkspaceSecretScanner>();
        services.AddSingleton<LocalGitService>();
        services.AddSingleton<IGitService>(provider => provider.GetRequiredService<LocalGitService>());
        services.AddSingleton<IPublicationGitService>(provider =>
            provider.GetRequiredService<LocalGitService>());
        services.AddSingleton<GitHubCliService>();
        services.AddSingleton<IGitHubService>(provider => provider.GetRequiredService<GitHubCliService>());
        services.AddSingleton<IPublicationGitHubService>(provider =>
            provider.GetRequiredService<GitHubCliService>());
        services.AddSingleton<IPublicationLeaseProvider, WindowsPublicationLeaseProvider>();
        services.AddSingleton<IProjectPublicationWorkspaceFactory, ProjectPublicationWorkspaceFactory>();
        services.AddSingleton<IPublicationReceiptStore, AtomicPublicationReceiptStore>();
        services.AddSingleton<IPublicationNonceGenerator, CryptographicPublicationNonceGenerator>();
        services.AddSingleton<IProjectPublicationCoordinator, ProjectPublicationCoordinator>();
        services.AddSingleton<IProjectFinalizer, AtomicProjectFinalizer>();
        services.AddSingleton<IGenerationReportWriter, CanonicalGenerationReportWriter>();
        services.AddSingleton<IRunCompletionCoordinator, ValidatedRunCompletionCoordinator>();
        services.AddSingleton<IExecutionOrchestrator, CheckpointedExecutionOrchestrator>();
        services.AddSingleton<IRunRecoveryService, RunRecoveryService>();
        services.AddSingleton<IProjectCreationWorkflow, ProjectCreationWorkflow>();
        services.AddSingleton<IProjectRecoveryWorkflow, ProjectRecoveryWorkflow>();
        services.AddSingleton<ILocalReadyService, LocalReadyService>();

        services.AddSingleton(themeResourceHost);
        services.AddSingleton<ISystemThemeSource, WindowsSystemThemeSource>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IClipboardService, WindowsClipboardService>();
        services.AddSingleton<IDesktopSettingsService, DesktopSettingsService>();
        services.AddSingleton<IEnvironmentDoctorService, EnvironmentDoctorService>();
        services.AddSingleton<IDashboardService, DashboardService>();
        services.AddSingleton<IDesktopMigrationService, DesktopMigrationService>();
        services.AddSingleton<IStartupRecoveryService, StartupRecoveryService>();
        services.AddSingleton<IDesktopStartupCoordinator, DesktopStartupCoordinator>();

        services.AddSingleton<NavigationService>();
        services.AddSingleton<ProjectCreationSelection>();
        services.AddSingleton<NotificationService>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<EnvironmentDoctorViewModel>();
        services.AddSingleton<ExecutionSessionCoordinator>();
        services.AddSingleton<ExecutionCenterViewModel>();
        services.AddSingleton<RunHistoryActionCoordinator>();
        services.AddSingleton(provider => new CreateProjectViewModel(
            provider.GetRequiredService<IProjectCreationWorkflow>(),
            provider.GetRequiredService<ExecutionCenterViewModel>(),
            provider.GetRequiredService<ILocalReadyService>(),
            provider.GetRequiredService<ProjectCreationSelection>()));
        services.AddSingleton<BlueprintCatalogViewModel>();
        services.AddSingleton(provider => new RunHistoryViewModel(
            provider.GetRequiredService<IRunCheckpointStore>(),
            provider.GetRequiredService<RunHistoryActionCoordinator>(),
            provider.GetRequiredService<ExecutionCenterViewModel>(),
            provider.GetRequiredService<ILocalReadyService>()));
        services.AddTransient<MainWindow>();
    }

}
