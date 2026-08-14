using DevForge.Application.Contracts;

namespace DevForge.Infrastructure.Publication;

public sealed class ProjectPublicationWorkspaceFactory(
    IProjectRecoveryWorkspaceFactory recoveryWorkspaces) : IProjectPublicationWorkspaceFactory
{
    private readonly IProjectRecoveryWorkspaceFactory _recoveryWorkspaces = recoveryWorkspaces
        ?? throw new ArgumentNullException(nameof(recoveryWorkspaces));

    public async Task<ExecutionOperationResult<ProjectPublicationWorkspaces>> OpenAsync(
        RunCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        try
        {
            var recovered = await _recoveryWorkspaces.OpenAsync(checkpoint, cancellationToken)
                .ConfigureAwait(false);
            var finalProject = await _recoveryWorkspaces.OpenFinalProjectAsync(
                checkpoint,
                cancellationToken).ConfigureAwait(false);
            return ExecutionOperationResult.Success(
                new ProjectPublicationWorkspaces(finalProject, recovered.RunArtifacts));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InfrastructureOperationException
            or IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException)
        {
            var summary = "The finalized project workspace could not be opened safely.";
            return ExecutionOperationResult.Failure<ProjectPublicationWorkspaces>(
                DevForge.Domain.Diagnostics.DevForgeError.Create(
                    "DF-PUB-001",
                    summary,
                    DevForge.Domain.Privacy.RedactedText.FromTrustedRedaction(summary).Value,
                    "publication",
                    null,
                    true,
                    [],
                    []).Value);
        }
    }
}
