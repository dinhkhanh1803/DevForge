using DevForge.Application.Contracts;
using DevForge.Domain.Runs;

namespace DevForge.Application.Creation;

public sealed class LocalReadyService(
    IProjectRecoveryWorkspaceFactory workspaces,
    IIdeLauncher ideLauncher) : ILocalReadyService
{
    public LocalReadyPresentation Describe(RunCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        return workspaces.DescribeLocalReady(checkpoint);
    }

    public async Task OpenIdeAsync(
        RunCheckpoint checkpoint,
        string ideId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (checkpoint.Run.Status != RunStatus.LocalReady)
        {
            throw new InvalidOperationException("Only a LocalReady project can be opened.");
        }

        var workspace = await workspaces.OpenFinalProjectAsync(
            checkpoint,
            cancellationToken).ConfigureAwait(false);
        var request = IdeLaunchRequest.Create(workspace, ideId);
        if (!request.IsValid)
        {
            throw new InvalidOperationException("The selected IDE request is invalid.");
        }

        await ideLauncher.LaunchAsync(request.Value, cancellationToken).ConfigureAwait(false);
    }
}
