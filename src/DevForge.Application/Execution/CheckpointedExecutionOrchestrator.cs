using System.Collections.Immutable;
using DevForge.Application.Contracts;
using DevForge.Domain.Diagnostics;
using DevForge.Domain.Execution;
using DevForge.Domain.Runs;
using DevForge.Domain.Validation;

namespace DevForge.Application.Execution;

public sealed class ExecutionOrchestratorBusyException : InvalidOperationException
{
    public ExecutionOrchestratorBusyException()
        : base("Another project execution is already active.")
    {
    }

    public string Code { get; } = "DF-EXEC-001";
}

public sealed class ExecutionCheckpointMismatchException : InvalidOperationException
{
    public ExecutionCheckpointMismatchException()
        : base("The execution request does not match its persisted checkpoint.")
    {
    }

    public string Code { get; } = "DF-EXEC-003";
}

public sealed class ExecutionStartException : InvalidOperationException
{
    public ExecutionStartException(string code)
        : base("The guarded execution workspace could not be started.")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
    }

    public string Code { get; }
}

public sealed class CheckpointedExecutionOrchestrator : IExecutionOrchestrator
{
    private readonly IRunCheckpointStore _checkpointStore;
    private readonly IStagingWorkspaceManager _stagingManager;
    private readonly IBlueprintExecutionSource _blueprintSource;
    private readonly IExecutionHandlerRegistryProvider _registryProvider;
    private readonly TimeProvider _timeProvider;
    private static int _isExecuting;

    public CheckpointedExecutionOrchestrator(
        IRunCheckpointStore checkpointStore,
        IStagingWorkspaceManager stagingManager,
        IBlueprintExecutionSource blueprintSource,
        IExecutionHandlerRegistryProvider registryProvider,
        TimeProvider timeProvider)
    {
        _checkpointStore = checkpointStore ?? throw new ArgumentNullException(nameof(checkpointStore));
        _stagingManager = stagingManager ?? throw new ArgumentNullException(nameof(stagingManager));
        _blueprintSource = blueprintSource ?? throw new ArgumentNullException(nameof(blueprintSource));
        _registryProvider = registryProvider ?? throw new ArgumentNullException(nameof(registryProvider));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<RunCheckpoint> ExecuteAsync(
        ExecutionRequest request,
        IProgress<ExecutionProgressLine>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _isExecuting, 1, 0) != 0)
        {
            throw new ExecutionOrchestratorBusyException();
        }

        try
        {
            var safeProgress = progress is null ? null : new SafeProgress(progress);
            return request.Mode switch
            {
                ExecutionMode.Fresh => await ExecuteFreshAsync(
                    request,
                    safeProgress,
                    cancellationToken).ConfigureAwait(false),
                ExecutionMode.Resume => await ExecuteResumeAsync(
                    request,
                    safeProgress,
                    cancellationToken).ConfigureAwait(false),
                ExecutionMode.ManualRetry => await ExecuteResumeAsync(
                    request,
                    safeProgress,
                    cancellationToken).ConfigureAwait(false),
                _ => throw new ArgumentOutOfRangeException(nameof(request)),
            };
        }
        finally
        {
            Volatile.Write(ref _isExecuting, 0);
        }
    }

    private async Task<RunCheckpoint> ExecuteResumeAsync(
        ExecutionRequest request,
        IProgress<ExecutionProgressLine>? progress,
        CancellationToken cancellationToken)
    {
        var checkpoint = await _checkpointStore.FindAsync(
            request.Run.Id,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The persisted run checkpoint was not found.");
        EnsureRequestMatchesCheckpoint(request, checkpoint);

        var stagingResult = await _stagingManager.ValidateOwnershipAsync(
            checkpoint,
            request.TargetParentWorkspace,
            cancellationToken).ConfigureAwait(false);
        if (!stagingResult.IsSuccessful)
        {
            return await FailCheckpointAsync(
                checkpoint,
                stagingResult.Error!).ConfigureAwait(false);
        }

        var session = new ExecutionSession(checkpoint, stagingResult.Value);
        try
        {
            var packageResult = await _blueprintSource.OpenAsync(
                checkpoint.Blueprint,
                checkpoint.BlueprintFingerprint,
                cancellationToken).ConfigureAwait(false);
            if (!packageResult.IsSuccessful)
            {
                return await FailCheckpointAsync(
                    session.Checkpoint,
                    packageResult.Error!).ConfigureAwait(false);
            }

            EnsurePackageMatchesCheckpoint(packageResult.Value, checkpoint);
            var registryResult = _registryProvider.Create(
                packageResult.Value.Blueprint.Fingerprint.Trust);
            if (!registryResult.IsSuccessful)
            {
                return await FailCheckpointAsync(
                    session.Checkpoint,
                    registryResult.Error!).ConfigureAwait(false);
            }

            if (request.Mode == ExecutionMode.ManualRetry)
            {
                return await ExecuteManualRetryAsync(
                    request,
                    session,
                    packageResult.Value,
                    registryResult.Value,
                    progress,
                    cancellationToken).ConfigureAwait(false);
            }

            var resumedRun = Require(checkpoint.Run.ResumeExecution());
            session.Checkpoint = Recreate(checkpoint, resumedRun, checkpoint.Evidence);
            await _checkpointStore.SaveAsync(
                session.Checkpoint,
                cancellationToken).ConfigureAwait(false);
            for (var index = 0; index < session.Checkpoint.Plan.Steps.Length; index++)
            {
                var step = session.Checkpoint.Plan.Steps[index];
                var priorEvidence = session.Checkpoint.Evidence.FirstOrDefault(item =>
                    item.Kind == ExecutionEvidenceKind.Step
                    && StringComparer.Ordinal.Equals(item.Id, step.Id)
                    && item.Status == ExecutionEvidenceStatus.Passed);
                if (priorEvidence is null)
                {
                    var resumeHandler = registryResult.Value.Resolve(step.Handler)
                        ?? throw new InvalidOperationException(
                            "The immutable plan references an unavailable handler.");
                    var priorAttempt = session.Checkpoint.Run.Attempts.LastOrDefault(item =>
                        StringComparer.Ordinal.Equals(item.StepId, step.Id));
                    if (priorAttempt is not null)
                    {
                        if (priorAttempt.Outcome == StepAttemptOutcome.Running)
                        {
                            throw new ExecutionCheckpointMismatchException();
                        }

                        if (priorAttempt.Outcome == StepAttemptOutcome.Failed
                            && (priorAttempt.AttemptNumber >= step.RetryPolicy.MaxAttempts
                                || priorAttempt.Error?.IsRetryable != true))
                        {
                            var failedRun = Require(
                                session.Checkpoint.Run.TransitionTo(RunStatus.Failed));
                            session.Checkpoint = Recreate(
                                session.Checkpoint,
                                failedRun,
                                session.Checkpoint.Evidence);
                            await _checkpointStore.SaveAsync(
                                session.Checkpoint,
                                cancellationToken).ConfigureAwait(false);
                            return session.Checkpoint;
                        }

                        if (resumeHandler.ResumeBehavior == ExecutionResumeBehavior.ReplayFromFreshStaging)
                        {
                            if (!await ReplaceStagingForReplayAsync(
                                request,
                                session,
                                cancellationToken).ConfigureAwait(false))
                            {
                                return session.Checkpoint;
                            }

                            return await ExecuteStepsAsync(
                                request,
                                session,
                                packageResult.Value,
                                registryResult.Value,
                                progress,
                                startIndex: 0,
                                cancellationToken).ConfigureAwait(false);
                        }

                        var retryRequest = CreateHandlerRequest(
                            session.Checkpoint,
                            step,
                            session.Lease.Workspace,
                            packageResult.Value);
                        if (!await TryCleanupForRetryAsync(
                            session,
                            resumeHandler,
                            retryRequest,
                            cancellationToken).ConfigureAwait(false))
                        {
                            return session.Checkpoint;
                        }
                    }

                    return await ExecuteStepsAsync(
                        request,
                        session,
                        packageResult.Value,
                        registryResult.Value,
                        progress,
                        index,
                        cancellationToken).ConfigureAwait(false);
                }

                var handler = registryResult.Value.Resolve(step.Handler)
                    ?? throw new InvalidOperationException(
                        "The immutable plan references an unavailable handler.");
                if (handler.ResumeBehavior == ExecutionResumeBehavior.ReplayFromFreshStaging)
                {
                    if (!await ReplaceStagingForReplayAsync(
                        request,
                        session,
                        cancellationToken).ConfigureAwait(false))
                    {
                        return session.Checkpoint;
                    }

                    return await ExecuteStepsAsync(
                        request,
                        session,
                        packageResult.Value,
                        registryResult.Value,
                        progress,
                        startIndex: 0,
                        cancellationToken).ConfigureAwait(false);
                }

                var handlerRequest = CreateHandlerRequest(
                    session.Checkpoint,
                    step,
                    session.Lease.Workspace,
                    packageResult.Value);
                var postcondition = await handler.CheckPostconditionsAsync(
                    handlerRequest,
                    cancellationToken).ConfigureAwait(false);
                ValidatePhase(postcondition, ExecutionPhase.Postcondition);
                if (postcondition.Outcome == ExecutionHandlerOutcome.Cancelled)
                {
                    await PersistIdleCancellationAsync(session).ConfigureAwait(false);
                    throw new OperationCanceledException(cancellationToken);
                }

                if (postcondition.Outcome == ExecutionHandlerOutcome.Succeeded)
                {
                    continue;
                }

                var cleanupSucceeded = await TryCleanupForRetryAsync(
                    session,
                    handler,
                    handlerRequest,
                    cancellationToken).ConfigureAwait(false);
                if (!cleanupSucceeded)
                {
                    return session.Checkpoint;
                }

                return await ExecuteStepsAsync(
                    request,
                    session,
                    packageResult.Value,
                    registryResult.Value,
                    progress,
                    index,
                    cancellationToken).ConfigureAwait(false);
            }

            return session.Checkpoint;
        }
        catch (OperationCanceledException)
        {
            await PersistIdleCancellationAsync(session).ConfigureAwait(false);
            throw;
        }
        finally
        {
            await session.Lease.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<RunCheckpoint> ExecuteManualRetryAsync(
        ExecutionRequest request,
        ExecutionSession session,
        BlueprintExecutionPackage package,
        IExecutionHandlerRegistry registry,
        IProgress<ExecutionProgressLine>? progress,
        CancellationToken cancellationToken)
    {
        var failedAttempt = session.Checkpoint.Run.Attempts.LastOrDefault();
        if (failedAttempt is not
            {
                Outcome: StepAttemptOutcome.Failed,
                Error.IsRetryable: true,
            })
        {
            throw new InvalidOperationException("The checkpoint has no manually retryable attempt.");
        }

        var stepIndex = -1;
        for (var index = 0; index < session.Checkpoint.Plan.Steps.Length; index++)
        {
            if (StringComparer.Ordinal.Equals(
                    session.Checkpoint.Plan.Steps[index].Id,
                    failedAttempt.StepId))
            {
                stepIndex = index;
                break;
            }
        }

        if (stepIndex < 0)
        {
            throw new InvalidOperationException("The retry attempt does not belong to the checkpoint plan.");
        }

        var step = session.Checkpoint.Plan.Steps[stepIndex];
        var handler = registry.Resolve(step.Handler)
            ?? throw new InvalidOperationException("The immutable plan references an unavailable handler.");
        var decision = RetryDecisionEngine.Decide(
            step.RetryPolicy,
            failedAttempt.AttemptNumber,
            failedAttempt.Error,
            handler.ResumeBehavior);
        if (decision.Action != RetryAction.AwaitManualRetry)
        {
            var failedRun = Require(session.Checkpoint.Run.TransitionTo(RunStatus.Failed));
            session.Checkpoint = Recreate(
                session.Checkpoint,
                failedRun,
                session.Checkpoint.Evidence);
            await _checkpointStore.SaveAsync(
                session.Checkpoint,
                cancellationToken).ConfigureAwait(false);
            return session.Checkpoint;
        }

        if (handler.ResumeBehavior == ExecutionResumeBehavior.ReplayFromFreshStaging)
        {
            if (!await ReplaceStagingForReplayAsync(
                request,
                session,
                cancellationToken).ConfigureAwait(false))
            {
                return session.Checkpoint;
            }

            stepIndex = 0;
        }
        else
        {
            var handlerRequest = CreateHandlerRequest(
                session.Checkpoint,
                step,
                session.Lease.Workspace,
                package);
            var cleanupSucceeded = await TryCleanupForRetryAsync(
                session,
                handler,
                handlerRequest,
                cancellationToken).ConfigureAwait(false);
            if (!cleanupSucceeded)
            {
                return session.Checkpoint;
            }
        }

        return await ExecuteStepsAsync(
            request,
            session,
            package,
            registry,
            progress,
            stepIndex,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<RunCheckpoint> ExecuteFreshAsync(
        ExecutionRequest request,
        IProgress<ExecutionProgressLine>? progress,
        CancellationToken cancellationToken)
    {
        var stagingResult = await _stagingManager.CreateAsync(
            request,
            cancellationToken).ConfigureAwait(false);
        if (!stagingResult.IsSuccessful)
        {
            throw new ExecutionStartException(stagingResult.Error!.Code);
        }

        var lease = stagingResult.Value;
        ExecutionSession? session = null;
        var initialCheckpointSaved = false;
        try
        {
            var run = Require(request.Run.TransitionTo(RunStatus.Planning));
            var checkpoint = CreateInitialCheckpoint(request, run, lease.Workspace.Descriptor);
            session = new ExecutionSession(checkpoint, lease);
            await _checkpointStore.SaveAsync(
                session.Checkpoint,
                cancellationToken).ConfigureAwait(false);
            initialCheckpointSaved = true;
            run = Require(run.TransitionTo(RunStatus.Executing));
            session.Checkpoint = Recreate(
                session.Checkpoint,
                run,
                session.Checkpoint.Evidence);
            await _checkpointStore.SaveAsync(
                session.Checkpoint,
                cancellationToken).ConfigureAwait(false);

            var packageResult = await _blueprintSource.OpenAsync(
                request.PlannedProject.Preview.Blueprint,
                request.PlannedProject.BlueprintFingerprint,
                cancellationToken).ConfigureAwait(false);
            if (!packageResult.IsSuccessful)
            {
                return await FailCheckpointAsync(
                    session.Checkpoint,
                    packageResult.Error!).ConfigureAwait(false);
            }

            var registryResult = _registryProvider.Create(
                packageResult.Value.Blueprint.Fingerprint.Trust);
            if (!registryResult.IsSuccessful)
            {
                return await FailCheckpointAsync(
                    session.Checkpoint,
                    registryResult.Error!).ConfigureAwait(false);
            }

            return await ExecuteStepsAsync(
                request,
                session,
                packageResult.Value,
                registryResult.Value,
                progress,
                startIndex: 0,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (session is not null)
            {
                await PersistIdleCancellationAsync(session).ConfigureAwait(false);
            }

            throw;
        }
        catch (Exception) when (session is not null && !initialCheckpointSaved)
        {
            await session.Lease.DisposeAsync().ConfigureAwait(false);
            var cancelled = Require(session.Checkpoint.Run.TransitionTo(RunStatus.Cancelled));
            var cleanupCheckpoint = Recreate(
                session.Checkpoint,
                cancelled,
                session.Checkpoint.Evidence);
            _ = await _stagingManager.CleanupAsync(
                cleanupCheckpoint,
                request.TargetParentWorkspace,
                CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        finally
        {
            await (session?.Lease ?? lease).DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<RunCheckpoint> ExecuteStepsAsync(
        ExecutionRequest request,
        ExecutionSession session,
        BlueprintExecutionPackage package,
        IExecutionHandlerRegistry registry,
        IProgress<ExecutionProgressLine>? progress,
        int startIndex,
        CancellationToken cancellationToken)
    {
        for (var index = startIndex; index < session.Checkpoint.Plan.Steps.Length; index++)
        {
            var step = session.Checkpoint.Plan.Steps[index];
            var handler = registry.Resolve(step.Handler)
                ?? throw new InvalidOperationException(
                    "The immutable plan references an unavailable handler.");
            var replayPlan = false;
            while (true)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    await PersistIdleCancellationAsync(session).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var handlerRequest = CreateHandlerRequest(
                    session.Checkpoint,
                    step,
                    session.Lease.Workspace,
                    package);
                var run = Require(session.Checkpoint.Run.StartAttempt(
                    step.Id,
                    _timeProvider.GetUtcNow()));
                session.Checkpoint = Recreate(
                    session.Checkpoint,
                    run,
                    session.Checkpoint.Evidence);
                await _checkpointStore.SaveAsync(
                    session.Checkpoint,
                    cancellationToken).ConfigureAwait(false);

                AttemptPhaseResult phaseResult;
                try
                {
                    phaseResult = await ExecuteAttemptAsync(
                        handler,
                        handlerRequest,
                        progress,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    var cancelledAttempt = session.Checkpoint.Run.Attempts[^1];
                    run = Require(session.Checkpoint.Run.CompleteAttempt(
                        step.Id,
                        cancelledAttempt.AttemptNumber,
                        StepAttemptOutcome.Cancelled,
                        _timeProvider.GetUtcNow(),
                        null,
                        null,
                        null));
                    session.Checkpoint = Recreate(
                        session.Checkpoint,
                        run,
                        session.Checkpoint.Evidence);
                    await _checkpointStore.SaveAsync(
                        session.Checkpoint,
                        CancellationToken.None).ConfigureAwait(false);
                    run = Require(session.Checkpoint.Run.TransitionTo(RunStatus.Cancelled));
                    session.Checkpoint = Recreate(
                        session.Checkpoint,
                        run,
                        session.Checkpoint.Evidence);
                    await _checkpointStore.SaveAsync(
                        session.Checkpoint,
                        CancellationToken.None).ConfigureAwait(false);
                    throw;
                }

                var runningAttempt = session.Checkpoint.Run.Attempts[^1];
                if (phaseResult.Failure is null)
                {
                    run = Require(session.Checkpoint.Run.CompleteAttempt(
                        step.Id,
                        runningAttempt.AttemptNumber,
                        StepAttemptOutcome.Succeeded,
                        _timeProvider.GetUtcNow(),
                        phaseResult.Execute.ExitCode,
                        null,
                        phaseResult.Execute.OutputDigest));
                    var evidence = ExecutionEvidence.Create(
                        ExecutionEvidenceKind.Step,
                        step.Id,
                        ExecutionEvidenceStatus.Passed,
                        phaseResult.Execute.OutputDigest);
                    if (!evidence.IsValid)
                    {
                        throw new InvalidOperationException(
                            "Successful execution did not produce canonical evidence.");
                    }

                    session.Checkpoint = Recreate(
                        session.Checkpoint,
                        run,
                        UpsertEvidence(session.Checkpoint.Evidence, evidence.Value));
                    await _checkpointStore.SaveAsync(
                        session.Checkpoint,
                        cancellationToken).ConfigureAwait(false);
                    break;
                }

                var failure = phaseResult.Failure;
                run = Require(session.Checkpoint.Run.CompleteAttempt(
                    step.Id,
                    runningAttempt.AttemptNumber,
                    StepAttemptOutcome.Failed,
                    _timeProvider.GetUtcNow(),
                    phaseResult.Execute.ExitCode,
                    failure.Error,
                    phaseResult.Execute.OutputDigest));
                session.Checkpoint = Recreate(
                    session.Checkpoint,
                    run,
                    session.Checkpoint.Evidence);
                await _checkpointStore.SaveAsync(
                    session.Checkpoint,
                    cancellationToken).ConfigureAwait(false);
                run = Require(session.Checkpoint.Run.AppendError(failure.Error));
                session.Checkpoint = Recreate(
                    session.Checkpoint,
                    run,
                    session.Checkpoint.Evidence);
                await _checkpointStore.SaveAsync(
                    session.Checkpoint,
                    cancellationToken).ConfigureAwait(false);

                var decision = RetryDecisionEngine.Decide(
                    step.RetryPolicy,
                    runningAttempt.AttemptNumber,
                    failure.Error!,
                    handler.ResumeBehavior);
                if (decision.Action == RetryAction.Stop)
                {
                    run = Require(session.Checkpoint.Run.TransitionTo(RunStatus.Failed));
                    session.Checkpoint = Recreate(
                        session.Checkpoint,
                        run,
                        session.Checkpoint.Evidence);
                    await _checkpointStore.SaveAsync(
                        session.Checkpoint,
                        cancellationToken).ConfigureAwait(false);
                    return session.Checkpoint;
                }

                if (decision.Action == RetryAction.AwaitManualRetry)
                {
                    return session.Checkpoint;
                }

                try
                {
                    await Task.Delay(
                        decision.Delay,
                        _timeProvider,
                        cancellationToken).ConfigureAwait(false);
                    if (decision.Action == RetryAction.ReplayFromFreshStaging)
                    {
                        if (!await ReplaceStagingForReplayAsync(
                            request,
                            session,
                            cancellationToken).ConfigureAwait(false))
                        {
                            return session.Checkpoint;
                        }

                        replayPlan = true;
                        break;
                    }

                    var cleanupSucceeded = await TryCleanupForRetryAsync(
                        session,
                        handler,
                        handlerRequest,
                        cancellationToken).ConfigureAwait(false);
                    if (!cleanupSucceeded)
                    {
                        return session.Checkpoint;
                    }
                }
                catch (OperationCanceledException)
                {
                    await PersistIdleCancellationAsync(session).ConfigureAwait(false);
                    throw;
                }
            }

            if (replayPlan)
            {
                index = -1;
            }
        }

        return session.Checkpoint;
    }

    private async Task<bool> ReplaceStagingForReplayAsync(
        ExecutionRequest request,
        ExecutionSession session,
        CancellationToken cancellationToken)
    {
        await session.Lease.DisposeAsync().ConfigureAwait(false);
        var replay = await _stagingManager.RecreateForReplayAsync(
            session.Checkpoint,
            request,
            cancellationToken).ConfigureAwait(false);
        if (!replay.IsSuccessful)
        {
            session.Checkpoint = await FailCheckpointAsync(
                session.Checkpoint,
                replay.Error!).ConfigureAwait(false);
            return false;
        }

        session.Lease = replay.Value;
        session.Checkpoint = Recreate(
            session.Checkpoint,
            session.Checkpoint.Run,
            []);
        await _checkpointStore.SaveAsync(
            session.Checkpoint,
            cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task PersistIdleCancellationAsync(ExecutionSession session)
    {
        if (session.Checkpoint.Run.Status is not (RunStatus.Planning or RunStatus.Executing))
        {
            return;
        }

        var run = session.Checkpoint.Run;
        if (run.CurrentStepId is not null)
        {
            var runningAttempt = run.Attempts[^1];
            var completed = run.CompleteAttempt(
                runningAttempt.StepId,
                runningAttempt.AttemptNumber,
                StepAttemptOutcome.Cancelled,
                _timeProvider.GetUtcNow(),
                null,
                null,
                null);
            if (!completed.IsValid)
            {
                return;
            }

            run = completed.Value;
            session.Checkpoint = Recreate(
                session.Checkpoint,
                run,
                session.Checkpoint.Evidence);
            await _checkpointStore.SaveAsync(
                session.Checkpoint,
                CancellationToken.None).ConfigureAwait(false);
        }

        var cancelled = run.TransitionTo(RunStatus.Cancelled);
        if (!cancelled.IsValid)
        {
            return;
        }

        session.Checkpoint = Recreate(
            session.Checkpoint,
            cancelled.Value,
            session.Checkpoint.Evidence);
        await _checkpointStore.SaveAsync(
            session.Checkpoint,
            CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<bool> TryCleanupForRetryAsync(
        ExecutionSession session,
        IExecutionHandler handler,
        ExecutionHandlerRequest request,
        CancellationToken cancellationToken)
    {
        var cleanup = await handler.CleanupForRetryAsync(
            request,
            cancellationToken).ConfigureAwait(false);
        ValidatePhase(cleanup, ExecutionPhase.Prepare);
        if (cleanup.Outcome == ExecutionHandlerOutcome.Cancelled)
        {
            await PersistIdleCancellationAsync(session).ConfigureAwait(false);
            throw new OperationCanceledException(cancellationToken);
        }

        if (cleanup.Outcome == ExecutionHandlerOutcome.Succeeded)
        {
            return true;
        }

        session.Checkpoint = await FailCheckpointAsync(
            session.Checkpoint,
            cleanup.Error!).ConfigureAwait(false);
        return false;
    }

    private static ExecutionHandlerRequest CreateHandlerRequest(
        RunCheckpoint checkpoint,
        ExecutionStep step,
        StagingWorkspace staging,
        BlueprintExecutionPackage package)
    {
        var request = ExecutionHandlerRequest.Create(
            checkpoint.Run.Id,
            step,
            staging,
            package,
            checkpoint.Plan);
        return request.IsValid
            ? request.Value
            : throw new InvalidOperationException("The immutable handler request was invalid.");
    }

    private static RunCheckpoint CreateInitialCheckpoint(
        ExecutionRequest request,
        ProjectRun run,
        StagingDescriptor staging)
    {
        var target = TargetDescriptor.Create(
            request.TargetParentWorkspace.Root,
            request.TargetDirectory,
            null);
        var artifacts = RunArtifactDescriptor.Create(request.RunArtifactWorkspace.Root);
        if (!target.IsValid || !artifacts.IsValid)
        {
            throw new InvalidOperationException("The guarded execution descriptors were invalid.");
        }

        var checkpoint = RunCheckpoint.Create(
            run,
            request.PlannedProject.Plan,
            request.PlannedProject.Preview.Blueprint,
            request.PlannedProject.BlueprintFingerprint,
            staging,
            target.Value,
            artifacts.Value,
            [],
            FinalizationState.NotStarted,
            ReportPersistenceState.NotStarted);
        return checkpoint.IsValid
            ? checkpoint.Value
            : throw new InvalidOperationException("The initial run checkpoint was invalid.");
    }

    private static RunCheckpoint Recreate(
        RunCheckpoint checkpoint,
        ProjectRun run,
        ImmutableArray<ExecutionEvidence> evidence)
    {
        var result = RunCheckpoint.Create(
            run,
            checkpoint.Plan,
            checkpoint.Blueprint,
            checkpoint.BlueprintFingerprint,
            checkpoint.Staging,
            checkpoint.Target,
            checkpoint.RunArtifacts,
            evidence,
            checkpoint.FinalizationState,
            checkpoint.ReportState);
        return result.IsValid
            ? result.Value
            : throw new InvalidOperationException("The evolved run checkpoint was invalid.");
    }

    private async Task<RunCheckpoint> FailCheckpointAsync(
        RunCheckpoint checkpoint,
        DevForgeError error)
    {
        var run = checkpoint.Run;
        if (run.Status is RunStatus.Cancelled or RunStatus.ValidationFailed)
        {
            run = Require(run.ResumeExecution());
            checkpoint = Recreate(checkpoint, run, checkpoint.Evidence);
            await _checkpointStore.SaveAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);
        }

        run = Require(run.AppendError(error));
        checkpoint = Recreate(checkpoint, run, checkpoint.Evidence);
        await _checkpointStore.SaveAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);
        run = Require(run.TransitionTo(RunStatus.Failed));
        checkpoint = Recreate(checkpoint, run, checkpoint.Evidence);
        await _checkpointStore.SaveAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);
        return checkpoint;
    }

    private static void EnsureRequestMatchesCheckpoint(
        ExecutionRequest request,
        RunCheckpoint checkpoint)
    {
        var requested = request.PlannedProject.BlueprintFingerprint;
        var persisted = checkpoint.BlueprintFingerprint;
        if (!StringComparer.Ordinal.Equals(request.Run.Id, checkpoint.Run.Id)
            || !StringComparer.Ordinal.Equals(request.Run.RecipeId, checkpoint.Run.RecipeId)
            || !StringComparer.Ordinal.Equals(request.PlannedProject.Plan.Id, checkpoint.PlanHash)
            || !request.PlannedProject.Preview.Blueprint.Equals(checkpoint.Blueprint)
            || !request.TargetParentWorkspace.Root.Equals(checkpoint.Target.ParentRoot)
            || !request.TargetDirectory.Equals(checkpoint.Target.TargetDirectory)
            || !request.RunArtifactWorkspace.Root.Equals(checkpoint.RunArtifacts.Root)
            || !StringComparer.Ordinal.Equals(requested.SourceId, persisted.SourceId)
            || requested.Trust != persisted.Trust
            || !requested.PackageDirectory.Equals(persisted.PackageDirectory)
            || !StringComparer.Ordinal.Equals(
                requested.AggregateChecksum,
                persisted.AggregateChecksum)
            || !IsPersistedModeCompatible(request.Mode, checkpoint.Run))
        {
            throw new ExecutionCheckpointMismatchException();
        }
    }

    private static bool IsPersistedModeCompatible(ExecutionMode mode, ProjectRun run)
    {
        return mode switch
        {
            ExecutionMode.Resume => run.ResumeExecution().IsValid,
            ExecutionMode.ManualRetry => run.Status == RunStatus.Executing
                && run.CurrentStepId is null
                && run.Attempts.LastOrDefault() is
                {
                    Outcome: StepAttemptOutcome.Failed,
                    Error.IsRetryable: true,
                },
            _ => false,
        };
    }

    private static void EnsurePackageMatchesCheckpoint(
        BlueprintExecutionPackage package,
        RunCheckpoint checkpoint)
    {
        var actual = package.Blueprint.Fingerprint;
        var expected = checkpoint.BlueprintFingerprint;
        if (!package.Blueprint.Manifest.Id.Equals(checkpoint.Blueprint.Id, StringComparison.Ordinal)
            || !package.Blueprint.Manifest.Version.Equals(
                checkpoint.Blueprint.Version,
                StringComparison.Ordinal)
            || !StringComparer.Ordinal.Equals(actual.SourceId, expected.SourceId)
            || actual.Trust != expected.Trust
            || !actual.PackageDirectory.Equals(expected.PackageDirectory)
            || !StringComparer.Ordinal.Equals(
                actual.AggregateChecksum,
                expected.AggregateChecksum))
        {
            throw new InvalidOperationException("The reopened blueprint did not match the checkpoint.");
        }
    }

    private static ProjectRun Require(ValidationResult<ProjectRun> result)
    {
        return result.IsValid
            ? result.Value
            : throw new InvalidOperationException("The run lifecycle transition was invalid.");
    }

    private static async Task<AttemptPhaseResult> ExecuteAttemptAsync(
        IExecutionHandler handler,
        ExecutionHandlerRequest request,
        IProgress<ExecutionProgressLine>? progress,
        CancellationToken cancellationToken)
    {
        var prepare = await handler.PrepareAsync(request, cancellationToken).ConfigureAwait(false);
        ValidatePhase(prepare, ExecutionPhase.Prepare);
        ThrowIfCancelled(prepare, cancellationToken);
        if (prepare.Outcome == ExecutionHandlerOutcome.Failed)
        {
            return new AttemptPhaseResult(prepare, prepare);
        }

        var precondition = await handler.CheckPreconditionsAsync(
            request,
            cancellationToken).ConfigureAwait(false);
        ValidatePhase(precondition, ExecutionPhase.Precondition);
        ThrowIfCancelled(precondition, cancellationToken);
        if (precondition.Outcome == ExecutionHandlerOutcome.Failed)
        {
            return new AttemptPhaseResult(precondition, precondition);
        }

        var execute = await handler.ExecuteAsync(
            request,
            progress,
            cancellationToken).ConfigureAwait(false);
        ValidatePhase(execute, ExecutionPhase.Execute);
        ThrowIfCancelled(execute, cancellationToken);
        if (execute.Outcome == ExecutionHandlerOutcome.Failed)
        {
            return new AttemptPhaseResult(execute, execute);
        }

        var postcondition = await handler.CheckPostconditionsAsync(
            request,
            cancellationToken).ConfigureAwait(false);
        ValidatePhase(postcondition, ExecutionPhase.Postcondition);
        ThrowIfCancelled(postcondition, cancellationToken);
        return postcondition.Outcome == ExecutionHandlerOutcome.Failed
            ? new AttemptPhaseResult(execute, postcondition)
            : new AttemptPhaseResult(execute, null);
    }

    private static void ValidatePhase(ExecutionHandlerResult result, ExecutionPhase expectedPhase)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Phase != expectedPhase || !Enum.IsDefined(result.Outcome))
        {
            throw new InvalidOperationException("An execution handler returned an invalid phase result.");
        }
    }

    private static void ThrowIfCancelled(
        ExecutionHandlerResult result,
        CancellationToken cancellationToken)
    {
        if (result.Outcome == ExecutionHandlerOutcome.Cancelled)
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private static ImmutableArray<ExecutionEvidence> UpsertEvidence(
        ImmutableArray<ExecutionEvidence> evidence,
        ExecutionEvidence item)
    {
        var index = -1;
        for (var candidateIndex = 0; candidateIndex < evidence.Length; candidateIndex++)
        {
            var candidate = evidence[candidateIndex];
            if (candidate.Kind == item.Kind
                && StringComparer.Ordinal.Equals(candidate.Id, item.Id))
            {
                index = candidateIndex;
                break;
            }
        }

        return index < 0 ? evidence.Add(item) : evidence.SetItem(index, item);
    }

    private sealed record AttemptPhaseResult(
        ExecutionHandlerResult Execute,
        ExecutionHandlerResult? Failure);

    private sealed class ExecutionSession(
        RunCheckpoint checkpoint,
        IStagingWorkspaceLease lease)
    {
        public RunCheckpoint Checkpoint { get; set; } = checkpoint;

        public IStagingWorkspaceLease Lease { get; set; } = lease;
    }

    private sealed class SafeProgress(IProgress<ExecutionProgressLine> inner) :
        IProgress<ExecutionProgressLine>
    {
        public void Report(ExecutionProgressLine value)
        {
            try
            {
                inner.Report(value);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException
                and not StackOverflowException
                and not AccessViolationException)
            {
            }
        }
    }
}
