using DevForge.Application.Contracts;

namespace DevForge.Desktop.RunHistory;

public sealed class RunHistoryActionCoordinator(
    IProjectRecoveryWorkflow workflow,
    IProjectPublicationWorkflow publicationWorkflow)
{
    private bool _isReadOnly;
    private readonly IProjectRecoveryWorkflow _workflow =
        workflow ?? throw new ArgumentNullException(nameof(workflow));

    internal IProjectPublicationWorkflow PublicationWorkflow { get; } =
        publicationWorkflow ?? throw new ArgumentNullException(nameof(publicationWorkflow));

    public Task<ProjectRecoveryEligibility> InspectAsync(
        RunCheckpoint checkpoint,
        CancellationToken cancellationToken) =>
        _workflow.InspectAsync(checkpoint, cancellationToken);

    public Task<ProjectRecoverySnapshot> ContinueAsync(
        string runId,
        ExecutionMode mode,
        CancellationToken cancellationToken) =>
        _workflow.ContinueAsync(runId, mode, cancellationToken);

    public Task CleanupAsync(string runId, CancellationToken cancellationToken) =>
        _workflow.CleanupAsync(runId, cancellationToken);

    public void EnterReadOnlyMode() => _isReadOnly = true;

    public Task<ExecutionOperationResult<ProjectPublicationOutcome>> RetryPublicationAsync(
        string runId,
        CancellationToken cancellationToken) =>
        PublicationWorkflow.CompleteAsync(
            runId,
            _isReadOnly
                ? PublicationMutationMode.SafeReadOnly
                : PublicationMutationMode.Normal,
            cancellationToken);
}
