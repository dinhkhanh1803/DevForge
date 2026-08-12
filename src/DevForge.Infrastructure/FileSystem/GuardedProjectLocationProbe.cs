using DevForge.Application.Contracts;

namespace DevForge.Infrastructure.FileSystem;

public sealed class GuardedProjectLocationProbe : IProjectLocationProbe
{
    private readonly IFileSystem _fileSystem;

    public GuardedProjectLocationProbe(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public async Task<ProjectLocationStatus> InspectAsync(
        string? canonicalRoot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = WorkspaceRoot.Create(canonicalRoot);
        if (!root.IsValid)
        {
            return ProjectLocationStatus.Invalid;
        }

        try
        {
            await _fileSystem.OpenWorkspaceAsync(root.Value, cancellationToken).ConfigureAwait(false);
            return ProjectLocationStatus.Available;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InfrastructureOperationException exception) when (
            exception.Code is "DF-FS-001" or "DF-FS-003")
        {
            return ProjectLocationStatus.Unavailable;
        }
    }
}
