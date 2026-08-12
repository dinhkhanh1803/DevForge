using DevForge.Application.Contracts;
using DevForge.Application.Contracts.Persistence;
using DevForge.Desktop.Dashboard;
using DevForge.Desktop.EnvironmentDoctor;
using DevForge.Desktop.Navigation;
using DevForge.Desktop.Notifications;
using DevForge.Desktop.Settings;
using DevForge.Desktop.Shell;
using DevForge.Desktop.Theming;
using DevForge.Infrastructure.FileSystem;
using DevForge.Infrastructure.Persistence;
using DevForge.Infrastructure.Persistence.Migrations;
using DevForge.Infrastructure.Persistence.Repositories;
using DevForge.Infrastructure.Processes;
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

        services.AddSingleton<IFileSystem, WindowsFileSystem>();
        services.AddSingleton<IProjectLocationProbe, GuardedProjectLocationProbe>();
        services.AddSingleton<IProcessRunner, WindowsProcessRunner>();
        services.AddSingleton<IEnvironmentDoctor, DeferredEnvironmentDoctor>();

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
        services.AddSingleton<NotificationService>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<EnvironmentDoctorViewModel>();
        services.AddTransient<MainWindow>();
    }
}
