using DevForge.Application.Contracts.Persistence;
using DevForge.Infrastructure.Persistence.Migrations;

namespace DevForge.Desktop.Bootstrap;

public sealed class DesktopMigrationService : IDesktopMigrationService
{
    private readonly SqliteMigrationCoordinator _coordinator;
    private readonly DatabaseLocation _location;
    private readonly ILocalDataRootProvisioner _provisioner;

    public DesktopMigrationService(
        SqliteMigrationCoordinator coordinator,
        DatabaseLocation location,
        ILocalDataRootProvisioner provisioner)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _location = location ?? throw new ArgumentNullException(nameof(location));
        _provisioner = provisioner ?? throw new ArgumentNullException(nameof(provisioner));
    }

    public async Task<DesktopMigrationOutcome> MigrateAsync(CancellationToken cancellationToken)
    {
        await _provisioner.EnsureExistsAsync(_location, cancellationToken).ConfigureAwait(false);
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
