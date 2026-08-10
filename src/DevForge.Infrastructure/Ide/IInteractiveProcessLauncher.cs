using DevForge.Application.Contracts;

namespace DevForge.Infrastructure.Ide;

internal interface IInteractiveProcessLauncher
{
    Task LaunchAsync(
        ExecutableIdentity executable,
        IWorkspaceFileSystem workspace,
        CancellationToken cancellationToken);
}
