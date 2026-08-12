namespace DevForge.Application.Contracts.Persistence;

public interface ILocalDataRootProvisioner
{
    Task EnsureExistsAsync(DatabaseLocation location, CancellationToken cancellationToken);
}
