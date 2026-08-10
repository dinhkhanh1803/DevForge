using DevForge.Application.Contracts;
using DevForge.Infrastructure.Processes;

namespace DevForge.Infrastructure.Ide;

public sealed class WindowsIdeLauncher : IIdeLauncher
{
    private readonly IInteractiveProcessLauncher _interactiveLauncher;

    public WindowsIdeLauncher()
        : this(
            new WindowsInteractiveProcessLauncher(
                new TrustedExecutableResolver(),
                new SystemInteractiveProcessStarter()))
    {
    }

    internal WindowsIdeLauncher(IInteractiveProcessLauncher interactiveLauncher)
    {
        _interactiveLauncher = interactiveLauncher
            ?? throw new ArgumentNullException(nameof(interactiveLauncher));
    }

    public Task LaunchAsync(
        IdeLaunchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var executable = IdeCatalog.Resolve(request.IdeId);
        return _interactiveLauncher.LaunchAsync(
            executable,
            request.Workspace,
            cancellationToken);
    }
}
