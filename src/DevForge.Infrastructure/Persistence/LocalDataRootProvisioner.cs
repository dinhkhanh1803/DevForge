using DevForge.Application.Contracts;
using DevForge.Application.Contracts.Persistence;

namespace DevForge.Infrastructure.Persistence;

public sealed class LocalDataRootProvisioner(IFileSystem fileSystem) : ILocalDataRootProvisioner
{
    private readonly IFileSystem _fileSystem =
        fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public async Task EnsureExistsAsync(
        DatabaseLocation location,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        cancellationToken.ThrowIfCancellationRequested();
        var root = WorkspaceRoot.Create(location.LocalDataRoot);
        if (!root.IsValid)
        {
            throw new InfrastructureOperationException(
                "DF-FS-001",
                "The local application data root is invalid.");
        }

        try
        {
            await _fileSystem.EnsureWorkspaceExistsAsync(root.Value, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InfrastructureOperationException)
        {
            throw new InfrastructureOperationException(
                "DF-FS-001",
                "The local application data root could not be prepared.");
        }
    }
}
