using DevForge.Application.Contracts;

namespace DevForge.Desktop.RunHistory;

public sealed class RunHistoryActionCoordinator(IProjectRecoveryWorkflow workflow)
{
    private readonly IProjectRecoveryWorkflow _workflow =
        workflow ?? throw new ArgumentNullException(nameof(workflow));

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
}
