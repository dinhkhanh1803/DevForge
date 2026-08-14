using DevForge.Application.Contracts;
using DevForge.Application.Execution;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Diagnostics;
using DevForge.Domain.Execution;
using DevForge.Domain.Privacy;
using DevForge.Domain.Projects;
using DevForge.Domain.Reports;
using DevForge.Domain.Runs;

namespace DevForge.UnitTests.Application.Execution;

public sealed class ValidatedRunCompletionCoordinatorTests
{
    [Fact]
    public void ImplementsTheClosedCompletionPortWithAllRequiredDependencies()
    {
        Assert.Contains(
            typeof(IRunCompletionCoordinator),
            typeof(ValidatedRunCompletionCoordinator).GetInterfaces());
        var constructor = Assert.Single(typeof(ValidatedRunCompletionCoordinator).GetConstructors());
        Assert.Equal(
            [
                typeof(IRunCheckpointStore),
                typeof(ISecretScanner),
                typeof(IProjectFinalizer),
                typeof(IGenerationReportWriter),
                typeof(TimeProvider),
            ],
            constructor.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public async Task SuccessfulScanPersistsReportBeforeFinalizationAndEndsLocalReady()
    {
        var fixture = Fixture.Create(secretFound: false);

        var result = await fixture.Coordinator.CompleteAsync(
            fixture.Request,
            fixture.Checkpoint,
            fixture.Staging,
            fixture.Package,
            fixture.Registry,
            progress: null,
            CancellationToken.None);

        Assert.Equal(RunStatus.LocalReady, result.Run.Status);
        Assert.Equal(ReportPersistenceState.Succeeded, result.ReportState);
        Assert.Equal(FinalizationState.Succeeded, result.FinalizationState);
        Assert.Equal($"sha256:{new string('3', 64)}", result.Publication.FinalTreeDigest);
        Assert.Equal(GitPublicationState.NotRequested, result.Publication.GitState);
        var finalizationCheckpoint = Assert.Single(fixture.Store.SavedCheckpoints, saved =>
            saved.FinalizationState == FinalizationState.Succeeded
            && saved.ReportState == ReportPersistenceState.NotStarted);
        Assert.Equal(
            result.Publication.FinalTreeDigest,
            finalizationCheckpoint.Publication.FinalTreeDigest);
        Assert.Equal(RunStatus.Executing, finalizationCheckpoint.Run.Status);
        Assert.True(
            fixture.Events.IndexOf("finalizer.run")
                < fixture.Events.IndexOf("report.write"));
        Assert.Equal("checkpoint.save", fixture.Events[^1]);
    }

    [Fact]
    public async Task SecretFindingBlocksReportFinalizationTargetAndTransitionsValidationFailed()
    {
        var fixture = Fixture.Create(secretFound: true);

        var result = await fixture.Coordinator.CompleteAsync(
            fixture.Request,
            fixture.Checkpoint,
            fixture.Staging,
            fixture.Package,
            fixture.Registry,
            progress: null,
            CancellationToken.None);

        Assert.Equal(RunStatus.ValidationFailed, result.Run.Status);
        Assert.Equal("DF-SECRET-001", Assert.Single(result.Run.Errors).Code);
        Assert.DoesNotContain("report.write", fixture.Events);
        Assert.DoesNotContain("finalizer.run", fixture.Events);
        Assert.DoesNotContain("staging.cleanup-finalized", fixture.Events);
    }

    [Fact]
    public async Task SecretScannerOperationalFailureIsPersistedAndBlocksFinalization()
    {
        var fixture = Fixture.Create(secretFound: false, scannerThrows: true);

        var result = await fixture.Coordinator.CompleteAsync(
            fixture.Request,
            fixture.Checkpoint,
            fixture.Staging,
            fixture.Package,
            fixture.Registry,
            progress: null,
            CancellationToken.None);

        Assert.Equal(RunStatus.ValidationFailed, result.Run.Status);
        Assert.Equal("DF-SECRET-001", Assert.Single(result.Run.Errors).Code);
        Assert.DoesNotContain("finalizer.run", fixture.Events);
        Assert.DoesNotContain("report.write", fixture.Events);
    }

    [Fact]
    public async Task RequiredValidatorFailureStopsAfterTheFirstFailedPhase()
    {
        var handler = new ValidatorHandler(ExecutionPhase.Prepare, ExecutionHandlerOutcome.Failed);
        var fixture = Fixture.Create(secretFound: false, handler);

        var result = await fixture.Coordinator.CompleteAsync(
            fixture.Request,
            fixture.Checkpoint,
            fixture.Staging,
            fixture.Package,
            fixture.Registry,
            progress: null,
            CancellationToken.None);

        Assert.Equal(RunStatus.ValidationFailed, result.Run.Status);
        Assert.Equal([ExecutionPhase.Prepare], handler.Phases);
        Assert.DoesNotContain("secret.scan", fixture.Events);
    }

    [Fact]
    public async Task CancelledValidatorResultPersistsCancellationAndStopsLaterPhases()
    {
        var handler = new ValidatorHandler(ExecutionPhase.Prepare, ExecutionHandlerOutcome.Cancelled);
        var fixture = Fixture.Create(secretFound: false, handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Coordinator.CompleteAsync(
                fixture.Request,
                fixture.Checkpoint,
                fixture.Staging,
                fixture.Package,
                fixture.Registry,
                progress: null,
                CancellationToken.None));

        Assert.Equal(RunStatus.Cancelled, fixture.Store.LastCheckpoint?.Run.Status);
        Assert.Equal([ExecutionPhase.Prepare], handler.Phases);
        Assert.DoesNotContain("secret.scan", fixture.Events);
    }

    [Fact]
    public async Task OptionalValidatorFailureIsRecordedAsWarningAndDoesNotBlockFinalization()
    {
        var handler = new ValidatorHandler(ExecutionPhase.Execute, ExecutionHandlerOutcome.Failed);
        var fixture = Fixture.Create(secretFound: false, handler, validatorRequired: false);

        var result = await fixture.Coordinator.CompleteAsync(
            fixture.Request,
            fixture.Checkpoint,
            fixture.Staging,
            fixture.Package,
            fixture.Registry,
            progress: null,
            CancellationToken.None);

        Assert.Equal(RunStatus.LocalReady, result.Run.Status);
        var evidence = Assert.Single(result.Evidence, item => item.Kind == ExecutionEvidenceKind.Validator);
        Assert.Equal(ExecutionEvidenceStatus.Warning, evidence.Status);
        Assert.Contains("finalizer.run", fixture.Events);
    }

    [Fact]
    public async Task ReportFailureRetainsStagingAndBlocksFinalization()
    {
        var fixture = Fixture.Create(secretFound: false, reportFails: true);

        var result = await fixture.Coordinator.CompleteAsync(
            fixture.Request,
            fixture.Checkpoint,
            fixture.Staging,
            fixture.Package,
            fixture.Registry,
            progress: null,
            CancellationToken.None);

        Assert.Equal(RunStatus.Failed, result.Run.Status);
        Assert.Equal(ReportPersistenceState.Failed, result.ReportState);
        Assert.Contains("finalizer.run", fixture.Events);
        Assert.DoesNotContain("staging.cleanup-finalized", fixture.Events);
    }

    [Fact]
    public async Task FinalizerFailureIsDurableAndRetainsOwnedStaging()
    {
        var fixture = Fixture.Create(secretFound: false, finalizerFails: true);

        var result = await fixture.Coordinator.CompleteAsync(
            fixture.Request,
            fixture.Checkpoint,
            fixture.Staging,
            fixture.Package,
            fixture.Registry,
            progress: null,
            CancellationToken.None);

        Assert.Equal(RunStatus.Failed, result.Run.Status);
        Assert.Equal(FinalizationState.Failed, result.FinalizationState);
        Assert.Equal("DF-FINAL-001", Assert.Single(result.Run.Errors).Code);
        Assert.DoesNotContain("report.write", fixture.Events);
        Assert.DoesNotContain("staging.cleanup-finalized", fixture.Events);
    }

    [Fact]
    public async Task CancellationAfterFinalizerCommitCompletesDurableLocalReadyBoundary()
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = Fixture.Create(secretFound: false, cancelAfterFinalize: cancellation);

        var result = await fixture.Coordinator.CompleteAsync(
            fixture.Request,
            fixture.Checkpoint,
            fixture.Staging,
            fixture.Package,
            fixture.Registry,
            progress: null,
            cancellation.Token);

        Assert.Equal(RunStatus.LocalReady, result.Run.Status);
        Assert.Equal("checkpoint.save", fixture.Events[^1]);
    }

    private sealed class Fixture
    {
        private Fixture(
            ExecutionRequest request,
            RunCheckpoint checkpoint,
            StagingWorkspace staging,
            BlueprintExecutionPackage package,
            IExecutionHandlerRegistry registry,
            ValidatedRunCompletionCoordinator coordinator,
            Store store,
            List<string> events)
        {
            Request = request;
            Checkpoint = checkpoint;
            Staging = staging;
            Package = package;
            Registry = registry;
            Coordinator = coordinator;
            Store = store;
            Events = events;
        }

        public ExecutionRequest Request { get; }
        public RunCheckpoint Checkpoint { get; }
        public StagingWorkspace Staging { get; }
        public BlueprintExecutionPackage Package { get; }
        public IExecutionHandlerRegistry Registry { get; }
        public ValidatedRunCompletionCoordinator Coordinator { get; }
        public Store Store { get; }
        public List<string> Events { get; }

        public static Fixture Create(
            bool secretFound,
            ValidatorHandler? validatorHandler = null,
            bool validatorRequired = true,
            bool reportFails = false,
            bool finalizerFails = false,
            CancellationTokenSource? cancelAfterFinalize = null,
            bool scannerThrows = false)
        {
            var events = new List<string>();
            var target = new StubWorkspace("C:\\target");
            var artifacts = new StubWorkspace("C:\\artifacts");
            var payload = new StubWorkspace("C:\\target\\.devforge-staging\\run\\payload");
            var validators = validatorHandler is null
                ? []
                : new[]
                {
                    ExecutionValidator.Create(
                        "quality-gate",
                        validatorHandler.Id,
                        [],
                        TimeSpan.FromMinutes(1),
                        required: validatorRequired).Value,
                };
            var plan = ExecutionPlan.Create($"sha256:{new string('1', 64)}", [], validators).Value;
            var blueprint = BlueprintReference.Create("desktop.csharp-wpf-tool", "1.0.0").Value;
            var fingerprint = BlueprintFingerprint.Create(
                "built-in",
                Path("desktop.csharp-wpf-tool\\1.0.0"),
                BlueprintTrust.BuiltIn,
                $"sha256:{new string('2', 64)}").Value;
            var preview = PlanPreview.Create(
                blueprint, [], [], [], [], [], [], [], [], [],
                GitOptions.Create().Value,
                CompletionOptions.Create().Value,
                plan.Id).Value;
            var planned = PlannedProject.Create(plan, preview, fingerprint).Value;
            var run = ProjectRun.Create("run", "recipe").Value
                .TransitionTo(RunStatus.Planning).Value
                .TransitionTo(RunStatus.Executing).Value;
            var request = ExecutionRequest.Create(
                planned, run, target, Path("project"), artifacts, ExecutionMode.Resume).Value;
            var descriptor = StagingDescriptor.Create(
                Path(".devforge-staging\\run"),
                Path(".devforge-staging\\run\\payload"),
                Path(".devforge-staging\\run\\ownership.json"),
                "run").Value;
            var staging = StagingWorkspace.Create(descriptor, payload).Value;
            var checkpoint = RunCheckpoint.Create(
                run,
                plan,
                blueprint,
                fingerprint,
                descriptor,
                TargetDescriptor.Create(target.Root, Path("project"), null).Value,
                RunArtifactDescriptor.Create(artifacts.Root).Value,
                [],
                FinalizationState.NotStarted,
                ReportPersistenceState.NotStarted).Value;
            var manifest = BlueprintManifest.Create(
                new BlueprintManifestDraft(
                    blueprint.Id, blueprint.Version, ">=1.0.0 <2.0.0", [], [], [], [], []),
                new BlueprintTrustAssignment(BlueprintTrust.BuiltIn)).Value;
            var resolved = ResolvedBlueprint.Create(manifest, [], fingerprint).Value;
            var package = BlueprintExecutionPackage.Create(
                resolved,
                new StubWorkspace("C:\\blueprint")).Value;
            var store = new Store(events);
            var coordinator = new ValidatedRunCompletionCoordinator(
                store,
                new Scanner(events, secretFound, scannerThrows),
                new Finalizer(events, checkpoint.Target, finalizerFails, cancelAfterFinalize),
                new Writer(events, reportFails),
                TimeProvider.System);
            return new Fixture(
                request,
                checkpoint,
                staging,
                package,
                validatorHandler is null
                    ? new EmptyRegistry()
                    : new SingleHandlerRegistry(validatorHandler),
                coordinator,
                store,
                events);
        }
    }

    private sealed class Store(List<string> events) : IRunCheckpointStore
    {
        public RunCheckpoint? LastCheckpoint { get; private set; }

        public List<RunCheckpoint> SavedCheckpoints { get; } = [];

        public Task SaveAsync(RunCheckpoint checkpoint, CancellationToken cancellationToken)
        {
            events.Add("checkpoint.save");
            LastCheckpoint = checkpoint;
            SavedCheckpoints.Add(checkpoint);
            return Task.CompletedTask;
        }
        public Task<RunCheckpoint?> FindAsync(string runId, CancellationToken cancellationToken) =>
            Task.FromResult<RunCheckpoint?>(null);
        public Task<System.Collections.Immutable.ImmutableArray<RunCheckpoint>> ListAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(System.Collections.Immutable.ImmutableArray<RunCheckpoint>.Empty);
    }

    private sealed class Scanner(
        List<string> events,
        bool found,
        bool throws) : ISecretScanner
    {
        public Task<SecretScanResult> ScanAsync(
            SecretScanRequest request,
            CancellationToken cancellationToken)
        {
            events.Add("secret.scan");
            if (throws)
            {
                throw new IOException("Injected guarded scan failure.");
            }

            var findings = found
                ? new[]
                {
                    SecretFinding.Create(
                        Path("appsettings.json"),
                        1,
                        RedactedText.FromTrustedRedaction("Credential-shaped value.").Value).Value,
                }
                : [];
            return Task.FromResult(SecretScanResult.Create(findings).Value);
        }
    }

    private sealed class Finalizer(
        List<string> events,
        TargetDescriptor target,
        bool fails,
        CancellationTokenSource? cancelAfterFinalize) : IProjectFinalizer
    {
        public Task<ExecutionOperationResult<FinalizationReceipt>> FinalizeAsync(
            RunCheckpoint checkpoint,
            StagingWorkspace staging,
            IWorkspaceFileSystem targetParentWorkspace,
            CancellationToken cancellationToken)
        {
            events.Add("finalizer.run");
            if (fails)
            {
                return Task.FromResult(ExecutionOperationResult.Failure<FinalizationReceipt>(
                    DevForgeError.Create(
                        "DF-FINAL-001",
                        "Finalization failed.",
                        RedactedText.FromTrustedRedaction("Finalization failed safely.").Value,
                        "finalization",
                        null,
                        false,
                        [],
                        []).Value));
            }

            cancelAfterFinalize?.Cancel();
            return Task.FromResult(ExecutionOperationResult.Success(
                FinalizationReceipt.Create(target, $"sha256:{new string('3', 64)}").Value));
        }
    }

    private sealed class Writer(List<string> events, bool fails) : IGenerationReportWriter
    {
        public Task<ExecutionOperationResult<ReportWriteReceipt>> WriteAsync(
            RunCheckpoint checkpoint,
            GenerationReport report,
            IWorkspaceFileSystem runArtifactWorkspace,
            CancellationToken cancellationToken)
        {
            events.Add("report.write");
            if (fails)
            {
                return Task.FromResult(ExecutionOperationResult.Failure<ReportWriteReceipt>(
                    DevForgeError.Create(
                        "DF-FINAL-001",
                        "Report failed.",
                        RedactedText.FromTrustedRedaction("Report failed safely.").Value,
                        "report",
                        null,
                        true,
                        [],
                        []).Value));
            }

            return Task.FromResult(ExecutionOperationResult.Success(
                ReportWriteReceipt.Create(Path("run.json"), Path("run.md")).Value));
        }
    }

    private sealed class StagingManager(List<string> events) : IStagingWorkspaceManager
    {
        public Task<ExecutionOperationResult<StagingCleanupReceipt>> CleanupFinalizedAsync(
            RunCheckpoint checkpoint,
            IWorkspaceFileSystem targetParentWorkspace,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add("staging.cleanup-finalized");
            return Task.FromResult(ExecutionOperationResult.Success(
                StagingCleanupReceipt.Create(checkpoint.Run.Id, checkpoint.Staging.MarkerId).Value));
        }
        public Task<ExecutionOperationResult<IStagingWorkspaceLease>> CreateAsync(
            ExecutionRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ExecutionOperationResult<IStagingWorkspaceLease>> ValidateOwnershipAsync(
            RunCheckpoint checkpoint, IWorkspaceFileSystem targetParentWorkspace,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ExecutionOperationResult<IStagingWorkspaceLease>> RecreateForReplayAsync(
            RunCheckpoint checkpoint, ExecutionRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ExecutionOperationResult<StagingCleanupReceipt>> CleanupAsync(
            RunCheckpoint checkpoint, IWorkspaceFileSystem targetParentWorkspace,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class EmptyRegistry : IExecutionHandlerRegistry
    {
        public IExecutionHandler? Resolve(string handlerId) => null;
    }

    private sealed class SingleHandlerRegistry(IExecutionHandler handler) : IExecutionHandlerRegistry
    {
        public IExecutionHandler? Resolve(string handlerId) =>
            StringComparer.Ordinal.Equals(handler.Id, handlerId) ? handler : null;
    }

    private sealed class ValidatorHandler(
        ExecutionPhase terminalPhase,
        ExecutionHandlerOutcome terminalOutcome) : IExecutionHandler
    {
        public string Id => "validate-command";
        public ExecutionResumeBehavior ResumeBehavior => ExecutionResumeBehavior.RevalidatePostcondition;
        public List<ExecutionPhase> Phases { get; } = [];

        public Task<ExecutionHandlerResult> PrepareAsync(
            ExecutionHandlerRequest request,
            CancellationToken cancellationToken) => Result(ExecutionPhase.Prepare);

        public Task<ExecutionHandlerResult> CheckPreconditionsAsync(
            ExecutionHandlerRequest request,
            CancellationToken cancellationToken) => Result(ExecutionPhase.Precondition);

        public Task<ExecutionHandlerResult> ExecuteAsync(
            ExecutionHandlerRequest request,
            IProgress<ExecutionProgressLine>? progress,
            CancellationToken cancellationToken) => Result(ExecutionPhase.Execute);

        public Task<ExecutionHandlerResult> CheckPostconditionsAsync(
            ExecutionHandlerRequest request,
            CancellationToken cancellationToken) => Result(ExecutionPhase.Postcondition);

        public Task<ExecutionHandlerResult> CleanupForRetryAsync(
            ExecutionHandlerRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        private Task<ExecutionHandlerResult> Result(ExecutionPhase phase)
        {
            Phases.Add(phase);
            var outcome = phase == terminalPhase
                ? terminalOutcome
                : ExecutionHandlerOutcome.Succeeded;
            var error = outcome == ExecutionHandlerOutcome.Failed
                ? DevForgeError.Create(
                    "DF-VALID-001",
                    "Validation failed.",
                    RedactedText.FromTrustedRedaction("Validation failed safely.").Value,
                    phase.ToString().ToLowerInvariant(),
                    null,
                    false,
                    [],
                    []).Value
                : null;
            return Task.FromResult(ExecutionHandlerResult.Create(
                phase,
                outcome,
                null,
                outcome == ExecutionHandlerOutcome.Succeeded
                    ? $"sha256:{new string('4', 64)}"
                    : null,
                error,
                []).Value);
        }
    }

    private sealed class StubWorkspace(string root) : IWorkspaceFileSystem
    {
        public WorkspaceRoot Root { get; } = WorkspaceRoot.Create(root).Value;
        public Task<System.Collections.Immutable.ImmutableArray<WorkspaceRelativePath>> EnumerateAllFilesAsync(
            CancellationToken cancellationToken) => Task.FromResult(
                System.Collections.Immutable.ImmutableArray.Create(Path("src\\App.csproj")));
        public Task<bool> FileExistsAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DirectoryExistsAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CreateDirectoryAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Stream> OpenWriteAsync(WorkspaceRelativePath path, bool overwrite, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteFileAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<System.Collections.Immutable.ImmutableArray<WorkspaceRelativePath>> EnumerateRootDirectoriesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<System.Collections.Immutable.ImmutableArray<WorkspaceRelativePath>> EnumerateFilesAsync(WorkspaceRelativePath directory, bool recursive, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<System.Collections.Immutable.ImmutableArray<WorkspaceRelativePath>> EnumerateDirectoriesAsync(WorkspaceRelativePath directory, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteDirectoryAsync(WorkspaceRelativePath path, DirectoryCleanupIntent intent, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MoveDirectoryAsync(WorkspaceRelativePath source, WorkspaceRelativePath destination, WorkspaceMoveIntent intent, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private static WorkspaceRelativePath Path(string value) => WorkspaceRelativePath.Create(value).Value;
}
