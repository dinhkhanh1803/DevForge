using DevForge.Application.Contracts;
using DevForge.Domain.Diagnostics;
using DevForge.Domain.Privacy;
using DevForge.Domain.Runs;

namespace DevForge.Desktop.Bootstrap;

public sealed class StartupRecoveryService : IStartupRecoveryService
{
    private readonly IRunCheckpointStore _checkpointStore;
    private readonly TimeProvider _timeProvider;

    public StartupRecoveryService(IRunCheckpointStore checkpointStore, TimeProvider timeProvider)
    {
        _checkpointStore = checkpointStore ?? throw new ArgumentNullException(nameof(checkpointStore));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<bool> RecoverAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var checkpoints = await _checkpointStore.ListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var checkpoint in checkpoints.Where(item =>
                         item.Run.Status == RunStatus.Executing
                         && item.Run.CurrentStepId is not null))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var normalized = Normalize(checkpoint);
                if (normalized is null)
                {
                    return false;
                }

                await _checkpointStore.SaveAsync(normalized, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsRecoverableBoundaryFailure(exception))
        {
            return false;
        }
    }

    private RunCheckpoint? Normalize(RunCheckpoint checkpoint)
    {
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
            return null;
        }

        var recreated = RunCheckpoint.Create(
            run.Value,
            checkpoint.Plan,
            checkpoint.Preview,
            checkpoint.Blueprint,
            checkpoint.BlueprintFingerprint,
            checkpoint.Staging,
            checkpoint.Target,
            checkpoint.RunArtifacts,
            checkpoint.Evidence,
            checkpoint.FinalizationState,
            checkpoint.ReportState,
            checkpoint.Publication);
        return recreated.IsValid ? recreated.Value : null;
    }

    private static bool IsRecoverableBoundaryFailure(Exception exception) =>
        exception is not OutOfMemoryException
            and not StackOverflowException
            and not AccessViolationException;
}
