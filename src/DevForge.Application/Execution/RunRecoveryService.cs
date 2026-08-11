using System.Collections.Immutable;
using DevForge.Application.Contracts;
using DevForge.Domain.Diagnostics;
using DevForge.Domain.Privacy;
using DevForge.Domain.Runs;

namespace DevForge.Application.Execution;

public sealed class RunRecoveryService : IRunRecoveryService
{
    private readonly IRunCheckpointStore _checkpointStore;
    private readonly IExecutionOrchestrator _orchestrator;
    private readonly IStagingWorkspaceManager _stagingManager;
    private readonly TimeProvider _timeProvider;

    public RunRecoveryService(
        IRunCheckpointStore checkpointStore,
        IExecutionOrchestrator orchestrator,
        IStagingWorkspaceManager stagingManager,
        TimeProvider timeProvider)
    {
        _checkpointStore = checkpointStore ?? throw new ArgumentNullException(nameof(checkpointStore));
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _stagingManager = stagingManager ?? throw new ArgumentNullException(nameof(stagingManager));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<ExecutionOperationResult<RunRecoveryBatch>> RecoverInterruptedAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ExecutionActivityGate.TryEnter())
        {
            return Failure<RunRecoveryBatch>();
        }

        try
        {
            var checkpoints = await _checkpointStore.ListAsync(cancellationToken).ConfigureAwait(false);
            var recovered = ImmutableArray.CreateBuilder<RunCheckpoint>();
            foreach (var checkpoint in checkpoints.Where(item => item.Run.Status == RunStatus.Executing))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await NormalizeAuthoritativeAsync(
                    checkpoint.Run.Id,
                    cancellationToken).ConfigureAwait(false);
                if (!result.IsSuccessful)
                {
                    return ExecutionOperationResult.Failure<RunRecoveryBatch>(result.Error!);
                }

                recovered.Add(result.Value);
            }

            return ExecutionOperationResult.Success(RunRecoveryBatch.Create(recovered).Value);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsRecoverableBoundaryFailure(exception))
        {
            return Failure<RunRecoveryBatch>();
        }
        finally
        {
            ExecutionActivityGate.Exit();
        }
    }

    public async Task<ExecutionOperationResult<RunCheckpoint>> NormalizeInterruptedAsync(
        RunCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ExecutionActivityGate.TryEnter())
        {
            return Failure<RunCheckpoint>();
        }

        try
        {
            return await NormalizeAuthoritativeAsync(
                checkpoint.Run.Id,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsRecoverableBoundaryFailure(exception))
        {
            return Failure<RunCheckpoint>();
        }
        finally
        {
            ExecutionActivityGate.Exit();
        }
    }

    public async Task<ExecutionOperationResult<RunCheckpoint>> ResumeAsync(
        ExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Mode is not (ExecutionMode.Resume or ExecutionMode.ManualRetry))
        {
            return Failure<RunCheckpoint>();
        }

        try
        {
            var checkpoint = await _orchestrator.ExecuteAsync(
                request,
                progress: null,
                cancellationToken).ConfigureAwait(false);
            return ExecutionOperationResult.Success(checkpoint);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FinalizedStagingCleanupException)
        {
            throw;
        }
        catch (Exception exception) when (IsRecoverableBoundaryFailure(exception))
        {
            return Failure<RunCheckpoint>();
        }
    }

    public Task<ExecutionOperationResult<StagingCleanupReceipt>> CleanupAsync(
        RunCheckpoint checkpoint,
        IWorkspaceFileSystem targetParentWorkspace,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(targetParentWorkspace);
        return _stagingManager.CleanupAsync(
            checkpoint,
            targetParentWorkspace,
            cancellationToken);
    }

    private static RunCheckpoint Recreate(RunCheckpoint checkpoint, ProjectRun run) =>
        RunCheckpoint.Create(
            run,
            checkpoint.Plan,
            checkpoint.Blueprint,
            checkpoint.BlueprintFingerprint,
            checkpoint.Staging,
            checkpoint.Target,
            checkpoint.RunArtifacts,
            checkpoint.Evidence,
            checkpoint.FinalizationState,
            checkpoint.ReportState).Value;

    private async Task<ExecutionOperationResult<RunCheckpoint>> NormalizeAuthoritativeAsync(
        string runId,
        CancellationToken cancellationToken)
    {
        var checkpoint = await _checkpointStore.FindAsync(
            runId,
            cancellationToken).ConfigureAwait(false);
        if (checkpoint is null)
        {
            return Failure<RunCheckpoint>();
        }

        if (checkpoint.Run.Status != RunStatus.Executing
            || checkpoint.Run.CurrentStepId is null)
        {
            return ExecutionOperationResult.Success(checkpoint);
        }

        var error = DevForgeError.Create(
            "DF-EXEC-003",
            "The previous execution was interrupted.",
            RedactedText.FromTrustedRedaction(
                "A persisted running attempt was closed without assuming that its process survived.").Value,
            "recovery",
            checkpoint.Run.CurrentStepId,
            true,
            ["Resume the run to revalidate or replay the interrupted step."],
            []).Value;
        var run = checkpoint.Run.InterruptCurrentAttempt(
            _timeProvider.GetUtcNow(),
            error,
            outputDigest: null);
        if (!run.IsValid)
        {
            return Failure<RunCheckpoint>();
        }

        var normalized = Recreate(checkpoint, run.Value);
        await _checkpointStore.SaveAsync(normalized, cancellationToken).ConfigureAwait(false);
        return ExecutionOperationResult.Success(normalized);
    }

    private static ExecutionOperationResult<T> Failure<T>()
        where T : class
    {
        var error = DevForgeError.Create(
            "DF-EXEC-003",
            "Interrupted execution recovery could not be completed safely.",
            RedactedText.FromTrustedRedaction(
                "The persisted checkpoint or recovery boundary could not be verified.").Value,
            "recovery",
            null,
            true,
            ["Verify the saved run, blueprint, and staging ownership before retrying."],
            []).Value;
        return ExecutionOperationResult.Failure<T>(error);
    }

    private static bool IsRecoverableBoundaryFailure(Exception exception) =>
        exception is not OutOfMemoryException
            and not StackOverflowException
            and not AccessViolationException;
}
