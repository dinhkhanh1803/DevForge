using DevForge.Application.Contracts;

namespace DevForge.Infrastructure.FileSystem;

public sealed class WindowsFileSystem : IFileSystem
{
    public Task EnsureWorkspaceExistsAsync(
        WorkspaceRoot allowedRoot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(allowedRoot);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            WorkspacePathGuard.RejectExistingAncestorReparsePoints(
                allowedRoot,
                cancellationToken);
            Directory.CreateDirectory(allowedRoot.RevealForFileSystem());
            cancellationToken.ThrowIfCancellationRequested();
            WorkspacePathGuard.RejectExistingAncestorReparsePoints(
                allowedRoot,
                cancellationToken);
            _ = WorkspacePathGuard.Open(allowedRoot);
            return Task.CompletedTask;
        }
        catch (WorkspaceContainmentException)
        {
            throw new InfrastructureOperationException(
                "DF-FS-003",
                "Workspace containment could not be proven.");
        }
        catch (Exception exception) when (WindowsWorkspaceFileSystem.IsExpectedFileSystemFailure(exception))
        {
            throw new InfrastructureOperationException(
                "DF-FS-001",
                "The workspace root could not be prepared.");
        }
    }

    public Task<IWorkspaceFileSystem> OpenWorkspaceAsync(
        WorkspaceRoot allowedRoot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(allowedRoot);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var guard = WorkspacePathGuard.Open(allowedRoot);
            return Task.FromResult<IWorkspaceFileSystem>(
                new WindowsWorkspaceFileSystem(allowedRoot, guard));
        }
        catch (WorkspaceContainmentException)
        {
            throw new InfrastructureOperationException(
                "DF-FS-003",
                "Workspace containment could not be proven.");
        }
        catch (Exception exception) when (WindowsWorkspaceFileSystem.IsExpectedFileSystemFailure(exception))
        {
            throw new InfrastructureOperationException(
                "DF-FS-001",
                "The workspace root could not be opened.");
        }
    }
}
