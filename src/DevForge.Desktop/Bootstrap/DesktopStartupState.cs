using DevForge.Desktop.Dashboard;
using DevForge.Desktop.EnvironmentDoctor;
using DevForge.Desktop.Navigation;
using DevForge.Desktop.Settings;

namespace DevForge.Desktop.Bootstrap;

public enum DesktopStartupMode
{
    Normal = 1,
    SafeReadOnly = 2,
}

public enum DesktopMigrationOutcome
{
    Ready = 1,
    Failed = 2,
    RecoveryFailed = 3,
}

public sealed record DesktopStartupState(
    DesktopStartupMode Mode,
    DesktopRoute InitialRoute,
    string? UserSafeMessage,
    DesktopSettings Settings,
    EnvironmentHealthSnapshot EnvironmentHealth,
    DashboardSnapshot? Dashboard);

public interface IDesktopMigrationService
{
    Task<DesktopMigrationOutcome> MigrateAsync(CancellationToken cancellationToken);
}

public interface IStartupRecoveryService
{
    Task<bool> RecoverAsync(CancellationToken cancellationToken);
}
