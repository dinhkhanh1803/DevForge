using DevForge.Desktop.Dashboard;
using DevForge.Desktop.EnvironmentDoctor;
using DevForge.Desktop.Navigation;
using DevForge.Desktop.Settings;
using DevForge.Desktop.Theming;

namespace DevForge.Desktop.Bootstrap;

public interface IDesktopStartupCoordinator
{
    Task<DesktopStartupState> InitializeAsync(CancellationToken cancellationToken);
}

public sealed class DesktopStartupCoordinator : IDesktopStartupCoordinator
{
    private readonly IDesktopMigrationService _migration;
    private readonly IStartupRecoveryService _recovery;
    private readonly IDesktopSettingsService _settings;
    private readonly IThemeService _theme;
    private readonly IEnvironmentDoctorService _environment;
    private readonly IDashboardService _dashboard;

    public DesktopStartupCoordinator(
        IDesktopMigrationService migration,
        IStartupRecoveryService recovery,
        IDesktopSettingsService settings,
        IThemeService theme,
        IEnvironmentDoctorService environment,
        IDashboardService dashboard)
    {
        _migration = migration ?? throw new ArgumentNullException(nameof(migration));
        _recovery = recovery ?? throw new ArgumentNullException(nameof(recovery));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _dashboard = dashboard ?? throw new ArgumentNullException(nameof(dashboard));
    }

    public async Task<DesktopStartupState> InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var migration = await _migration.MigrateAsync(cancellationToken).ConfigureAwait(false);
        if (migration != DesktopMigrationOutcome.Ready)
        {
            return await CreateSafeModeAsync(
                "Local data could not be prepared safely. DevForge is read-only.",
                cancellationToken).ConfigureAwait(false);
        }

        if (!await _recovery.RecoverAsync(cancellationToken).ConfigureAwait(false))
        {
            return await CreateSafeModeAsync(
                "Interrupted work could not be recovered safely. DevForge is read-only.",
                cancellationToken).ConfigureAwait(false);
        }

        var settings = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        _theme.Apply(settings.Theme);
        var environment = await _environment.LoadAsync(
            forceRefresh: false,
            cancellationToken).ConfigureAwait(false);
        var dashboard = await _dashboard.LoadAsync(cancellationToken).ConfigureAwait(false);
        return new DesktopStartupState(
            DesktopStartupMode.Normal,
            settings.OnboardingCompleted ? DesktopRoute.Dashboard : DesktopRoute.Settings,
            null,
            settings,
            environment,
            dashboard);
    }

    private async Task<DesktopStartupState> CreateSafeModeAsync(
        string message,
        CancellationToken cancellationToken)
    {
        DesktopSettings settings;
        try
        {
            settings = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            settings = new DesktopSettings(
                string.Empty,
                "none",
                "none",
                "en-US",
                ThemePreference.System,
                OnboardingCompleted: false);
        }

        _theme.Apply(settings.Theme);
        EnvironmentHealthSnapshot environment;
        try
        {
            environment = await _environment.LoadCachedAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            environment = new EnvironmentHealthSnapshot(
                [], null, EnvironmentSnapshotSource.Cache, IsStale: true, ScanFailed: false);
        }

        return new DesktopStartupState(
            DesktopStartupMode.SafeReadOnly,
            DesktopRoute.Settings,
            message,
            settings,
            environment,
            Dashboard: null);
    }
}
