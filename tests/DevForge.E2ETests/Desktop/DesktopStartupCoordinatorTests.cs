using DevForge.Application.Contracts;
using DevForge.Desktop.Bootstrap;
using DevForge.Desktop.Dashboard;
using DevForge.Desktop.EnvironmentDoctor;
using DevForge.Desktop.Navigation;
using DevForge.Desktop.Settings;
using DevForge.Desktop.Theming;

namespace DevForge.E2ETests.Desktop;

public sealed class DesktopStartupCoordinatorTests
{
    [Fact]
    public async Task NormalStartupUsesApprovedOrder()
    {
        var calls = new List<string>();
        var settings = Defaults();
        var environment = EmptyEnvironment();
        var dashboard = new DashboardSnapshot([], [], [], environment);
        var sut = new DesktopStartupCoordinator(
            new FakeMigration(calls, DesktopMigrationOutcome.Ready),
            new FakeRecovery(calls, succeeds: true),
            new FakeSettings(calls, settings),
            new FakeTheme(calls),
            new FakeEnvironment(calls, environment),
            new FakeDashboard(calls, dashboard));

        var result = await sut.InitializeAsync(CancellationToken.None);

        Assert.Equal(
            ["migrate", "recover", "settings", "theme", "environment", "dashboard"],
            calls);
        Assert.Equal(DesktopStartupMode.Normal, result.Mode);
        Assert.Same(dashboard, result.Dashboard);
        Assert.Equal(DesktopRoute.Settings, result.InitialRoute);
    }

    [Theory]
    [InlineData(DesktopMigrationOutcome.Failed)]
    [InlineData(DesktopMigrationOutcome.RecoveryFailed)]
    public async Task MigrationFailureEntersReadOnlySafeModeWithoutRecoveryOrScan(
        DesktopMigrationOutcome outcome)
    {
        var calls = new List<string>();
        var environment = EmptyEnvironment();
        var sut = new DesktopStartupCoordinator(
            new FakeMigration(calls, outcome),
            new FakeRecovery(calls, succeeds: true),
            new FakeSettings(calls, Defaults()),
            new FakeTheme(calls),
            new FakeEnvironment(calls, environment),
            new FakeDashboard(calls, new DashboardSnapshot([], [], [], environment)));

        var result = await sut.InitializeAsync(CancellationToken.None);

        Assert.Equal(["migrate", "settings", "theme", "environment-cache"], calls);
        Assert.Equal(DesktopStartupMode.SafeReadOnly, result.Mode);
        Assert.Equal(DesktopRoute.Settings, result.InitialRoute);
        Assert.Null(result.Dashboard);
        Assert.False(string.IsNullOrWhiteSpace(result.UserSafeMessage));
    }

    [Fact]
    public async Task RecoveryFailureEntersReadOnlySafeModeBeforeMutableFeatures()
    {
        var calls = new List<string>();
        var environment = EmptyEnvironment();
        var sut = new DesktopStartupCoordinator(
            new FakeMigration(calls, DesktopMigrationOutcome.Ready),
            new FakeRecovery(calls, succeeds: false),
            new FakeSettings(calls, Defaults()),
            new FakeTheme(calls),
            new FakeEnvironment(calls, environment),
            new FakeDashboard(calls, new DashboardSnapshot([], [], [], environment)));

        var result = await sut.InitializeAsync(CancellationToken.None);

        Assert.Equal(["migrate", "recover", "settings", "theme", "environment-cache"], calls);
        Assert.Equal(DesktopStartupMode.SafeReadOnly, result.Mode);
        Assert.Equal(DesktopRoute.Settings, result.InitialRoute);
    }

    [Theory]
    [InlineData(false, DesktopRoute.Settings)]
    [InlineData(true, DesktopRoute.Dashboard)]
    public async Task NormalStartupRouteMatrixFollowsOnboardingOnly(
        bool onboardingCompleted,
        DesktopRoute expectedRoute)
    {
        var calls = new List<string>();
        var settings = Defaults() with { OnboardingCompleted = onboardingCompleted };
        var environment = EmptyEnvironment();
        var sut = new DesktopStartupCoordinator(
            new FakeMigration(calls, DesktopMigrationOutcome.Ready),
            new FakeRecovery(calls, succeeds: true),
            new FakeSettings(calls, settings),
            new FakeTheme(calls),
            new FakeEnvironment(calls, environment),
            new FakeDashboard(calls, new DashboardSnapshot([], [], [], environment)));

        var result = await sut.InitializeAsync(CancellationToken.None);

        Assert.Equal(DesktopStartupMode.Normal, result.Mode);
        Assert.Equal(expectedRoute, result.InitialRoute);
    }

    private static DesktopSettings Defaults() =>
        new(string.Empty, "none", "none", "en-US", ThemePreference.System, false);

    private static EnvironmentHealthSnapshot EmptyEnvironment() =>
        new([], null, EnvironmentSnapshotSource.Cache, true, false);

    private sealed class FakeMigration(List<string> calls, DesktopMigrationOutcome outcome)
        : IDesktopMigrationService
    {
        public Task<DesktopMigrationOutcome> MigrateAsync(CancellationToken cancellationToken)
        {
            calls.Add("migrate");
            return Task.FromResult(outcome);
        }
    }

    private sealed class FakeRecovery(List<string> calls, bool succeeds) : IStartupRecoveryService
    {
        public Task<bool> RecoverAsync(CancellationToken cancellationToken)
        {
            calls.Add("recover");
            return Task.FromResult(succeeds);
        }
    }

    private sealed class FakeSettings(List<string> calls, DesktopSettings settings)
        : IDesktopSettingsService
    {
        public Task<DesktopSettings> LoadAsync(CancellationToken cancellationToken)
        {
            calls.Add("settings");
            return Task.FromResult(settings);
        }

        public Task<DevForge.Domain.Validation.ValidationResult<DesktopSettings>> SaveAsync(
            DesktopSettingsDraft draft,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeTheme(List<string> calls) : IThemeService
    {
        public void Apply(ThemePreference preference) => calls.Add("theme");
    }

    private sealed class FakeEnvironment(List<string> calls, EnvironmentHealthSnapshot snapshot)
        : IEnvironmentDoctorService
    {
        public Task<EnvironmentHealthSnapshot> LoadAsync(bool forceRefresh, CancellationToken cancellationToken)
        {
            calls.Add("environment");
            return Task.FromResult(snapshot);
        }

        public Task<EnvironmentHealthSnapshot> LoadCachedAsync(CancellationToken cancellationToken)
        {
            calls.Add("environment-cache");
            return Task.FromResult(snapshot);
        }
    }

    private sealed class FakeDashboard(List<string> calls, DashboardSnapshot snapshot) : IDashboardService
    {
        public Task<DashboardSnapshot> LoadAsync(CancellationToken cancellationToken)
        {
            calls.Add("dashboard");
            return Task.FromResult(snapshot);
        }
    }
}
