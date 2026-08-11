using System.Collections.Immutable;
using DevForge.Application.Contracts;
using DevForge.Application.Execution;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Diagnostics;
using DevForge.Domain.Execution;
using DevForge.Domain.Privacy;
using DevForge.Domain.Projects;
using DevForge.Domain.Runs;

namespace DevForge.UnitTests.Application.Execution;

[Collection(ExecutionActivityTestGroup.Name)]
public sealed class RunRecoveryServiceTests
{
    [Fact]
    public void ConstructorUsesOnlyCheckpointOrchestratorStagingAndTimePorts()
    {
        var constructor = Assert.Single(typeof(RunRecoveryService).GetConstructors());
        Assert.Equal(
            [
                typeof(IRunCheckpointStore),
                typeof(IExecutionOrchestrator),
                typeof(IStagingWorkspaceManager),
                typeof(TimeProvider),
            ],
            constructor.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public async Task StartupRecoveryClosesOnlyRunningExecutingAttemptsAndIsIdempotent()
    {
        var running = Checkpoint(runningAttempt: true);
        var terminal = Checkpoint(runningAttempt: false, RunStatus.Failed);
        var store = new Store([running, terminal]);
        var service = Service(store);

        var first = await service.RecoverInterruptedAsync(CancellationToken.None);
        var second = await service.RecoverInterruptedAsync(CancellationToken.None);

        Assert.True(first.IsSuccessful);
        var normalized = Assert.Single(first.Value.Checkpoints);
        var attempt = Assert.Single(normalized.Run.Attempts);
        Assert.Equal(StepAttemptOutcome.Failed, attempt.Outcome);
        Assert.Equal("DF-EXEC-003", attempt.Error?.Code);
        Assert.True(attempt.Error?.IsRetryable);
        Assert.Null(normalized.Run.CurrentStepId);
        Assert.Single(store.Saves);
        Assert.True(second.IsSuccessful);
        Assert.Single(second.Value.Checkpoints);
    }

    [Fact]
    public async Task PreCancelledStartupRecoveryDoesNotReadOrMutateTheStore()
    {
        var store = new Store([Checkpoint(runningAttempt: true)]);
        var service = Service(store);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.RecoverInterruptedAsync(cancellation.Token));

        Assert.Equal(0, store.ListCalls);
        Assert.Empty(store.Saves);
    }

    [Fact]
    public async Task DirectNormalizationUsesThePersistedCheckpointInsteadOfAStaleCallerSnapshot()
    {
        var staleRunning = Checkpoint(runningAttempt: true);
        var persistedTerminal = Checkpoint(runningAttempt: false, RunStatus.Failed);
        var store = new Store([persistedTerminal]);
        var service = Service(store);

        var result = await service.NormalizeInterruptedAsync(
            staleRunning,
            CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.Same(persistedTerminal, result.Value);
        Assert.Empty(store.Saves);
    }

    [Fact]
    public async Task ResumeRejectsFreshModeBeforeCallingTheOrchestrator()
    {
        var checkpoint = Checkpoint(runningAttempt: false);
        var orchestrator = new Orchestrator();
        var service = new RunRecoveryService(
            new Store([checkpoint]),
            orchestrator,
            new StagingManager(),
            TimeProvider.System);
        var preview = PlanPreview.Create(
            checkpoint.Blueprint,
            [new PlanPreviewStep("build", "run-process", TimeSpan.FromMinutes(1))],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            GitOptions.Create().Value,
            CompletionOptions.Create().Value,
            checkpoint.PlanHash).Value;
        var planned = PlannedProject.Create(
            checkpoint.Plan,
            preview,
            checkpoint.BlueprintFingerprint).Value;
        var request = ExecutionRequest.Create(
            planned,
            ProjectRun.Create("fresh-run", "recipe").Value,
            new StubWorkspace("C:\\target"),
            Path("project"),
            new StubWorkspace("C:\\artifacts"),
            ExecutionMode.Fresh).Value;

        var result = await service.ResumeAsync(request, CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Equal("DF-EXEC-003", result.Error?.Code);
        Assert.Equal(0, orchestrator.Calls);
    }

    [Fact]
    public async Task ResumeDelegatesAnEligibleCancelledRunToTheOrchestrator()
    {
        var checkpoint = Checkpoint(runningAttempt: false, RunStatus.Cancelled);
        var request = Request(checkpoint, ExecutionMode.Resume);
        var orchestrator = new Orchestrator(checkpoint);
        var service = new RunRecoveryService(
            new Store([checkpoint]),
            orchestrator,
            new StagingManager(),
            TimeProvider.System);

        var result = await service.ResumeAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.Same(checkpoint, result.Value);
        Assert.Equal(1, orchestrator.Calls);
    }

    [Fact]
    public async Task CleanupDelegatesTheExactCheckpointAndWorkspace()
    {
        var checkpoint = Checkpoint(runningAttempt: false, RunStatus.Cancelled);
        var workspace = new StubWorkspace("C:\\target");
        var staging = new StagingManager();
        var service = new RunRecoveryService(
            new Store([checkpoint]),
            new Orchestrator(),
            staging,
            TimeProvider.System);

        var result = await service.CleanupAsync(
            checkpoint,
            workspace,
            CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.Same(checkpoint, staging.CleanupCheckpoint);
        Assert.Same(workspace, staging.CleanupWorkspace);
    }

    [Fact]
    public async Task ValidationFailedResumePreservesOwnershipRefusalBeforeBlueprintOpen()
    {
        var checkpoint = Checkpoint(runningAttempt: false, RunStatus.ValidationFailed);
        var store = new Store([checkpoint]);
        var ownershipError = Error(
            "DF-EXEC-003",
            "Restore the exact owned staging workspace before resuming.");
        var staging = new StagingManager
        {
            ValidationResult = ExecutionOperationResult.Failure<IStagingWorkspaceLease>(
                ownershipError),
        };
        var blueprint = new BlueprintSource();
        var service = ServiceWithActualOrchestrator(store, staging, blueprint);

        var result = await service.ResumeAsync(
            Request(checkpoint, ExecutionMode.Resume),
            CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.Equal(RunStatus.Failed, result.Value.Run.Status);
        Assert.Equal(ownershipError.Code, result.Value.Run.Errors[^1].Code);
        Assert.Equal(0, blueprint.Calls);
        Assert.Equal(1, staging.ValidationCalls);
    }

    [Fact]
    public async Task ResumePreservesUnavailableBlueprintRemediationAfterOwnershipValidation()
    {
        var checkpoint = Checkpoint(runningAttempt: false, RunStatus.Cancelled);
        var store = new Store([checkpoint]);
        var blueprintError = Error(
            "DF-BP-004",
            "Restore the exact blueprint package version and retry.");
        var staging = new StagingManager
        {
            ValidationResult = ExecutionOperationResult.Success<IStagingWorkspaceLease>(
                new Lease(checkpoint.Staging)),
        };
        var blueprint = new BlueprintSource(
            ExecutionOperationResult.Failure<BlueprintExecutionPackage>(blueprintError));
        var service = ServiceWithActualOrchestrator(store, staging, blueprint);

        var result = await service.ResumeAsync(
            Request(checkpoint, ExecutionMode.Resume),
            CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.Equal(RunStatus.Failed, result.Value.Run.Status);
        var persistedError = result.Value.Run.Errors[^1];
        Assert.Equal(blueprintError.Code, persistedError.Code);
        Assert.Equal(
            blueprintError.SuggestedActions.ToArray(),
            persistedError.SuggestedActions.ToArray());
        Assert.Equal(1, blueprint.Calls);
        Assert.Equal(1, staging.ValidationCalls);
    }

    [Fact]
    public async Task CleanupPreservesMarkerOwnershipRefusal()
    {
        var checkpoint = Checkpoint(runningAttempt: false, RunStatus.Cancelled);
        var cleanupError = Error(
            "DF-EXEC-003",
            "Do not remove staging without the exact ownership marker.");
        var staging = new StagingManager
        {
            CleanupResult = ExecutionOperationResult.Failure<StagingCleanupReceipt>(cleanupError),
        };
        var service = new RunRecoveryService(
            new Store([checkpoint]),
            new Orchestrator(),
            staging,
            TimeProvider.System);

        var result = await service.CleanupAsync(
            checkpoint,
            new StubWorkspace("C:\\target"),
            CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Same(cleanupError, result.Error);
    }

    [Fact]
    public async Task ResumePreservesFinalizedCheckpointWhenOnlyStagingCleanupFailed()
    {
        var resumable = Checkpoint(runningAttempt: false, RunStatus.Cancelled);
        var carriedCheckpoint = Checkpoint(runningAttempt: false, RunStatus.Cancelled);
        var cleanupError = Error(
            "DF-EXEC-003",
            "Retry exact marker-owned staging cleanup.");
        var expected = new FinalizedStagingCleanupException(carriedCheckpoint, cleanupError);
        var service = new RunRecoveryService(
            new Store([resumable]),
            new Orchestrator(exception: expected),
            new StagingManager(),
            TimeProvider.System);

        var actual = await Assert.ThrowsAsync<FinalizedStagingCleanupException>(() =>
            service.ResumeAsync(Request(resumable, ExecutionMode.Resume), CancellationToken.None));

        Assert.Same(expected, actual);
        Assert.Same(carriedCheckpoint, actual.Checkpoint);
        Assert.Same(cleanupError, actual.Error);
    }

    private static RunRecoveryService Service(Store store) => new(
        store,
        new Orchestrator(),
        new StagingManager(),
        new FixedTimeProvider(DateTimeOffset.UnixEpoch.AddMinutes(1)));

    private static RunRecoveryService ServiceWithActualOrchestrator(
        Store store,
        StagingManager staging,
        BlueprintSource blueprint)
    {
        var orchestrator = new CheckpointedExecutionOrchestrator(
            store,
            staging,
            blueprint,
            new UnusedRegistryProvider(),
            new UnusedCompletionCoordinator(),
            TimeProvider.System);
        return new RunRecoveryService(store, orchestrator, staging, TimeProvider.System);
    }

    private static ExecutionRequest Request(RunCheckpoint checkpoint, ExecutionMode mode)
    {
        var preview = PlanPreview.Create(
            checkpoint.Blueprint,
            [new PlanPreviewStep("build", "run-process", TimeSpan.FromMinutes(1))],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            GitOptions.Create().Value,
            CompletionOptions.Create().Value,
            checkpoint.PlanHash).Value;
        var planned = PlannedProject.Create(
            checkpoint.Plan,
            preview,
            checkpoint.BlueprintFingerprint).Value;
        return ExecutionRequest.Create(
            planned,
            checkpoint.Run,
            new StubWorkspace("C:\\target"),
            Path("project"),
            new StubWorkspace("C:\\artifacts"),
            mode).Value;
    }

    private static RunCheckpoint Checkpoint(
        bool runningAttempt,
        RunStatus status = RunStatus.Executing)
    {
        var step = ExecutionStep.Create(
            "build",
            "Build",
            "run-process",
            [],
            TimeSpan.FromMinutes(1),
            RetryPolicy.Manual(2).Value).Value;
        var plan = ExecutionPlan.Create($"sha256:{new string('1', 64)}", [step], []).Value;
        var run = ProjectRun.Create("run-recovery", "recipe").Value
            .TransitionTo(RunStatus.Planning).Value
            .TransitionTo(RunStatus.Executing).Value;
        if (runningAttempt)
        {
            run = run.StartAttempt(step.Id, DateTimeOffset.UnixEpoch).Value;
        }
        else if (status != RunStatus.Executing)
        {
            run = run.TransitionTo(status).Value;
        }

        var blueprint = BlueprintReference.Create("desktop.csharp-wpf-tool", "1.0.0").Value;
        var fingerprint = BlueprintFingerprint.Create(
            "built-in",
            Path("desktop.csharp-wpf-tool\\1.0.0"),
            BlueprintTrust.BuiltIn,
            $"sha256:{new string('2', 64)}").Value;
        return RunCheckpoint.Create(
            run,
            plan,
            blueprint,
            fingerprint,
            StagingDescriptor.Create(
                Path(".devforge-staging\\run-recovery"),
                Path(".devforge-staging\\run-recovery\\payload"),
                Path(".devforge-staging\\run-recovery\\ownership.json"),
                "run-recovery").Value,
            TargetDescriptor.Create(
                WorkspaceRoot.Create("C:\\target").Value,
                Path("project"),
                null).Value,
            RunArtifactDescriptor.Create(WorkspaceRoot.Create("C:\\artifacts").Value).Value,
            [],
            FinalizationState.NotStarted,
            ReportPersistenceState.NotStarted).Value;
    }

    private sealed class Store(IEnumerable<RunCheckpoint> checkpoints) : IRunCheckpointStore
    {
        private ImmutableArray<RunCheckpoint> _checkpoints = [.. checkpoints];
        public List<RunCheckpoint> Saves { get; } = [];
        public int ListCalls { get; private set; }

        public Task SaveAsync(RunCheckpoint checkpoint, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Saves.Add(checkpoint);
            var index = -1;
            for (var candidate = 0; candidate < _checkpoints.Length; candidate++)
            {
                if (StringComparer.Ordinal.Equals(
                        _checkpoints[candidate].Run.Id,
                        checkpoint.Run.Id))
                {
                    index = candidate;
                    break;
                }
            }
            _checkpoints = index < 0 ? _checkpoints.Add(checkpoint) : _checkpoints.SetItem(index, checkpoint);
            return Task.CompletedTask;
        }

        public Task<RunCheckpoint?> FindAsync(string runId, CancellationToken cancellationToken) =>
            Task.FromResult<RunCheckpoint?>(_checkpoints.FirstOrDefault(item => item.Run.Id == runId));

        public Task<ImmutableArray<RunCheckpoint>> ListAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ListCalls++;
            return Task.FromResult(_checkpoints);
        }
    }

    private sealed class Orchestrator(
        RunCheckpoint? result = null,
        Exception? exception = null) : IExecutionOrchestrator
    {
        public int Calls { get; private set; }

        public Task<RunCheckpoint> ExecuteAsync(
            ExecutionRequest request,
            IProgress<ExecutionProgressLine>? progress,
            CancellationToken cancellationToken)
        {
            Calls++;
            if (exception is not null)
            {
                throw exception;
            }

            return result is null
                ? throw new NotSupportedException()
                : Task.FromResult(result);
        }
    }

    private sealed class StagingManager : IStagingWorkspaceManager
    {
        public RunCheckpoint? CleanupCheckpoint { get; private set; }
        public IWorkspaceFileSystem? CleanupWorkspace { get; private set; }
        public int ValidationCalls { get; private set; }
        public ExecutionOperationResult<IStagingWorkspaceLease>? ValidationResult { get; init; }
        public ExecutionOperationResult<StagingCleanupReceipt>? CleanupResult { get; init; }

        public Task<ExecutionOperationResult<IStagingWorkspaceLease>> CreateAsync(ExecutionRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ExecutionOperationResult<IStagingWorkspaceLease>> ValidateOwnershipAsync(RunCheckpoint checkpoint, IWorkspaceFileSystem targetParentWorkspace, CancellationToken cancellationToken)
        {
            ValidationCalls++;
            return Task.FromResult(ValidationResult ?? throw new NotSupportedException());
        }
        public Task<ExecutionOperationResult<IStagingWorkspaceLease>> RecreateForReplayAsync(RunCheckpoint checkpoint, ExecutionRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ExecutionOperationResult<StagingCleanupReceipt>> CleanupAsync(RunCheckpoint checkpoint, IWorkspaceFileSystem targetParentWorkspace, CancellationToken cancellationToken)
        {
            CleanupCheckpoint = checkpoint;
            CleanupWorkspace = targetParentWorkspace;
            return Task.FromResult(CleanupResult ?? ExecutionOperationResult.Success(
                StagingCleanupReceipt.Create(checkpoint.Run.Id, checkpoint.Staging.MarkerId).Value));
        }
        public Task<ExecutionOperationResult<StagingCleanupReceipt>> CleanupFinalizedAsync(RunCheckpoint checkpoint, IWorkspaceFileSystem targetParentWorkspace, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Lease(StagingDescriptor descriptor) : IStagingWorkspaceLease
    {
        public StagingWorkspace Workspace { get; } = StagingWorkspace.Create(
            descriptor,
            new StubWorkspace("C:\\target\\.devforge-staging\\run-recovery\\payload")).Value;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class BlueprintSource(
        ExecutionOperationResult<BlueprintExecutionPackage>? result = null) : IBlueprintExecutionSource
    {
        public int Calls { get; private set; }

        public Task<ExecutionOperationResult<BlueprintExecutionPackage>> OpenAsync(
            BlueprintReference blueprint,
            BlueprintFingerprint fingerprint,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(result ?? throw new NotSupportedException());
        }
    }

    private sealed class UnusedRegistryProvider : IExecutionHandlerRegistryProvider
    {
        public ExecutionOperationResult<IExecutionHandlerRegistry> Create(BlueprintTrust trust) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedCompletionCoordinator : IRunCompletionCoordinator
    {
        public Task<RunCheckpoint> CompleteAsync(
            ExecutionRequest request,
            RunCheckpoint checkpoint,
            StagingWorkspace staging,
            BlueprintExecutionPackage blueprintPackage,
            IExecutionHandlerRegistry registry,
            IProgress<ExecutionProgressLine>? progress,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StubWorkspace(string root) : IWorkspaceFileSystem
    {
        public WorkspaceRoot Root { get; } = WorkspaceRoot.Create(root).Value;
        public Task<bool> FileExistsAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DirectoryExistsAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CreateDirectoryAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Stream> OpenWriteAsync(WorkspaceRelativePath path, bool overwrite, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteFileAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateAllFilesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateRootDirectoriesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateFilesAsync(WorkspaceRelativePath directory, bool recursive, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateDirectoriesAsync(WorkspaceRelativePath directory, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteDirectoryAsync(WorkspaceRelativePath path, DirectoryCleanupIntent intent, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MoveDirectoryAsync(WorkspaceRelativePath source, WorkspaceRelativePath destination, WorkspaceMoveIntent intent, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private static WorkspaceRelativePath Path(string value) => WorkspaceRelativePath.Create(value).Value;

    private static DevForgeError Error(string code, string remediation) => DevForgeError.Create(
        code,
        "Recovery could not continue safely.",
        RedactedText.FromTrustedRedaction("A guarded recovery prerequisite failed.").Value,
        "recovery",
        null,
        true,
        [remediation],
        []).Value;
}
