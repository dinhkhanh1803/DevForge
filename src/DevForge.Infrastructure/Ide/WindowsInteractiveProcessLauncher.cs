using System.ComponentModel;
using System.Diagnostics;
using DevForge.Application.Contracts;
using DevForge.Infrastructure.FileSystem;
using DevForge.Infrastructure.Processes;

namespace DevForge.Infrastructure.Ide;

internal sealed class WindowsInteractiveProcessLauncher : IInteractiveProcessLauncher
{
    private readonly ITrustedExecutableResolver _executableResolver;
    private readonly IInteractiveProcessStarter _processStarter;

    public WindowsInteractiveProcessLauncher(
        ITrustedExecutableResolver executableResolver,
        IInteractiveProcessStarter processStarter)
    {
        _executableResolver = executableResolver
            ?? throw new ArgumentNullException(nameof(executableResolver));
        _processStarter = processStarter
            ?? throw new ArgumentNullException(nameof(processStarter));
    }

    public Task LaunchAsync(
        ExecutableIdentity executable,
        IWorkspaceFileSystem workspace,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentNullException.ThrowIfNull(workspace);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var resolved = _executableResolver.Resolve(executable);
            if (!resolved.PrefixArguments.IsEmpty)
            {
                throw new InfrastructureOperationException(
                    "DF-IDE-001",
                    "The trusted IDE executable could not be resolved directly.");
            }

            var guard = WorkspacePathGuard.Open(workspace.Root);
            var workspacePath = guard.RootPath;
            var startInfo = new ProcessStartInfo(resolved.ExecutablePath)
            {
                UseShellExecute = false,
                CreateNoWindow = false,
                WorkingDirectory = workspacePath,
            };
            startInfo.ArgumentList.Add(workspacePath);

            using var process = _processStarter.Start(startInfo);
            return Task.CompletedTask;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InfrastructureOperationException
            or Win32Exception
            or InvalidOperationException
            or IOException
            or UnauthorizedAccessException)
        {
            throw new InfrastructureOperationException(
                "DF-IDE-001",
                "The trusted IDE could not be launched.");
        }
    }
}

internal sealed class SystemInteractiveProcessStarter : IInteractiveProcessStarter
{
    public Process Start(ProcessStartInfo startInfo)
    {
        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("The interactive process did not start.");
    }
}
