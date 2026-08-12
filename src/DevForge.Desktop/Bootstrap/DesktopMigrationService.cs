using DevForge.Application.Contracts.Persistence;
using DevForge.Infrastructure.Persistence.Migrations;

namespace DevForge.Desktop.Bootstrap;

public sealed class DesktopMigrationService : IDesktopMigrationService
{
    private readonly SqliteMigrationCoordinator _coordinator;
    private readonly DatabaseLocation _location;

    public DesktopMigrationService(
        SqliteMigrationCoordinator coordinator,
        DatabaseLocation location)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _location = location ?? throw new ArgumentNullException(nameof(location));
    }

    public async Task<DesktopMigrationOutcome> MigrateAsync(CancellationToken cancellationToken)
    {
        var result = await _coordinator.MigrateAsync(_location, cancellationToken).ConfigureAwait(false);
        return result.State switch
        {
            DatabaseMigrationState.Created
                or DatabaseMigrationState.UpToDate
                or DatabaseMigrationState.Upgraded => DesktopMigrationOutcome.Ready,
            DatabaseMigrationState.RecoveryFailed => DesktopMigrationOutcome.RecoveryFailed,
            _ => DesktopMigrationOutcome.Failed,
        };
    }
}
