using DevForge.Application.Contracts;
using DevForge.Domain.Runs;

namespace DevForge.Application.Creation;

public sealed class ProjectRecoveryWorkflow(
    IRunCheckpointStore store,
    IProjectRecoveryWorkspaceFactory workspaces,
    IStagingWorkspaceManager stagingManager,
    IRunRecoveryService recovery,
    IBlueprintRecoveryInspector blueprintInspector) : IProjectRecoveryWorkflow
{
    public async Task<ProjectRecoveryEligibility> InspectAsync(
        RunCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (checkpoint.Preview is null || checkpoint.FinalizationState == FinalizationState.Succeeded)
        {
            return ProjectRecoveryEligibility.None;
        }

        try
        {
            if (!await blueprintInspector.IsCurrentAsync(
                    checkpoint.Blueprint,
                    checkpoint.BlueprintFingerprint,
                    cancellationToken).ConfigureAwait(false))
            {
                return ProjectRecoveryEligibility.None;
            }

            var opened = await workspaces.OpenAsync(checkpoint, cancellationToken).ConfigureAwait(false);
            var ownership = await stagingManager.ValidateOwnershipAsync(
                checkpoint, opened.TargetParent, cancellationToken).ConfigureAwait(false);
            if (!ownership.IsSuccessful)
            {
                return ProjectRecoveryEligibility.None;
            }

            await using var lease = ownership.Value;
            var planned = RequirePlannedProject(checkpoint);
            bool Supports(ExecutionMode mode) => ExecutionRequest.Create(
                planned, checkpoint.Run, opened.TargetParent, checkpoint.Target.TargetDirectory,
                opened.RunArtifacts, mode).IsValid;
            return new ProjectRecoveryEligibility(
                Supports(ExecutionMode.Resume),
                Supports(ExecutionMode.ManualRetry),
                checkpoint.Run.Status is RunStatus.PreflightFailed
                    or RunStatus.ValidationFailed or RunStatus.Cancelled or RunStatus.Failed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return ProjectRecoveryEligibility.None;
        }
    }

    public async Task<ProjectRecoverySnapshot> ContinueAsync(
        string runId,
        ExecutionMode mode,
        CancellationToken cancellationToken)
    {
        var checkpoint = await RequireCheckpointAsync(runId, cancellationToken).ConfigureAwait(false);
        var planned = RequirePlannedProject(checkpoint);
        var opened = await workspaces.OpenAsync(checkpoint, cancellationToken).ConfigureAwait(false);
        var request = ExecutionRequest.Create(
            planned, checkpoint.Run, opened.TargetParent, checkpoint.Target.TargetDirectory,
            opened.RunArtifacts, mode);
        if (!request.IsValid)
        {
            throw new InvalidOperationException("The persisted run cannot perform the selected recovery action.");
        }

        var result = await recovery.ResumeAsync(request.Value, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccessful)
        {
            throw new InvalidOperationException("The selected recovery action could not be completed safely.");
        }

        return new ProjectRecoverySnapshot(planned, result.Value);
    }

    public async Task CleanupAsync(string runId, CancellationToken cancellationToken)
    {
        var checkpoint = await RequireCheckpointAsync(runId, cancellationToken).ConfigureAwait(false);
        var opened = await workspaces.OpenAsync(checkpoint, cancellationToken).ConfigureAwait(false);
        var result = await recovery.CleanupAsync(
            checkpoint, opened.TargetParent, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccessful)
        {
            throw new InvalidOperationException("The selected staging workspace could not be cleaned safely.");
        }
    }

    private async Task<RunCheckpoint> RequireCheckpointAsync(
        string runId,
        CancellationToken cancellationToken) =>
        await store.FindAsync(runId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The selected run no longer exists.");

    private static PlannedProject RequirePlannedProject(RunCheckpoint checkpoint)
    {
        var preview = checkpoint.Preview ?? throw new InvalidOperationException(
            "This legacy run does not contain an authoritative reviewed plan snapshot.");
        return PlannedProject.Create(checkpoint.Plan, preview, checkpoint.BlueprintFingerprint).Value;
    }
}
