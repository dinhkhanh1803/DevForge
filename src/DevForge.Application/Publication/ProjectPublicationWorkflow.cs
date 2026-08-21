using DevForge.Application.Contracts;
using DevForge.Domain.Diagnostics;
using DevForge.Domain.Privacy;

namespace DevForge.Application.Publication;

public sealed class ProjectPublicationWorkflow(
    IProjectPublicationCoordinator coordinator,
    IRunCheckpointStore checkpointStore) : IProjectPublicationWorkflow
{
    private readonly IProjectPublicationCoordinator _coordinator = coordinator
        ?? throw new ArgumentNullException(nameof(coordinator));
    private readonly IRunCheckpointStore _checkpointStore = checkpointStore
        ?? throw new ArgumentNullException(nameof(checkpointStore));

    public async Task<ExecutionOperationResult<ProjectPublicationOutcome>> CompleteAsync(
        string runId,
        PublicationMutationMode mutationMode,
        CancellationToken cancellationToken)
    {
        var request = PublicationRequest.Create(runId, mutationMode);
        if (!request.IsValid)
        {
            return Failure("DF-PUB-REQUEST", "The publication request is invalid.");
        }

        var published = await _coordinator.PublishAsync(request.Value, cancellationToken)
            .ConfigureAwait(false);
        if (published.IsSuccessful)
        {
            return ExecutionOperationResult.Success(
                ProjectPublicationOutcome.Create(published.Value, error: null).Value);
        }

        var checkpoint = await _checkpointStore.FindAsync(runId, CancellationToken.None)
            .ConfigureAwait(false);
        return checkpoint is null
            ? ExecutionOperationResult.Failure<ProjectPublicationOutcome>(published.Error!)
            : ExecutionOperationResult.Success(
                ProjectPublicationOutcome.Create(checkpoint, published.Error).Value);
    }

    private static ExecutionOperationResult<ProjectPublicationOutcome> Failure(
        string code,
        string summary)
    {
        var error = DevForgeError.Create(
            code,
            summary,
            RedactedText.FromTrustedRedaction(summary).Value,
            "publication",
            stepId: null,
            isRetryable: false,
            suggestedActions: [],
            redactedContext: []).Value;
        return ExecutionOperationResult.Failure<ProjectPublicationOutcome>(error);
    }
}
