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

public sealed class CheckpointedExecutionOrchestratorTests
{
    [Fact]
    public async Task FinalizationBoundaryIsNotDispatchedAndCleanupRunsAfterLeaseRelease()
    {
        var fixture = Fixture.Create(
            completionLocalReady: true,
            includeFinalizationBoundary: true);

        var checkpoint = await fixture.Orchestrator.ExecuteAsync(
            fixture.Request,
            progress: null,
            CancellationToken.None);

        Assert.Equal(RunStatus.LocalReady, checkpoint.Run.Status);
        Assert.DoesNotContain("handler.finalize-workspace", fixture.Events);
        Assert.True(
            fixture.Events.IndexOf("lease.dispose")
                < fixture.Events.IndexOf("staging.cleanup-finalized"));
        Assert.Equal("staging.cleanup-finalized", fixture.Events[^1]);
    }

    [Fact]
    public async Task FinalizedCleanupFailureIsSurfacedWithTheDurableLocalReadyCheckpoint()
    {
        var cleanupError = FailureError("finalized-cleanup");
        var fixture = Fixture.Create(
            completionLocalReady: true,
            cleanupFinalizedError: cleanupError);

        var exception = await Assert.ThrowsAsync<FinalizedStagingCleanupException>(() =>
            fixture.Orchestrator.ExecuteAsync(
                fixture.Request,
                progress: null,
                CancellationToken.None));

        Assert.Equal(RunStatus.LocalReady, exception.Checkpoint.Run.Status);
        Assert.Same(cleanupError, exception.Error);
        Assert.Equal(1, fixture.Events.Count(item => item == "lease.dispose"));
    }

    [Fact]
    public async Task FreshExecutionPersistsPlanBeforeOrderedSixPhaseStepLifecycle()
    {
        var fixture = Fixture.Create();

        var checkpoint = await fixture.Orchestrator.ExecuteAsync(
            fixture.Request,
            progress: null,
            CancellationToken.None);

        Assert.Equal(
            [
                "staging.create",
                "checkpoint.save.initial",
                "checkpoint.save.initial",
                "blueprint.open",
                "registry.create",
                "checkpoint.save.running",
                "handler.prepare",
                "handler.precondition",
                "handler.execute",
                "handler.postcondition",
                "checkpoint.save.succeeded",
                "completion.run",
                "lease.dispose",
            ],
            fixture.Events);
        Assert.Equal(RunStatus.Executing, checkpoint.Run.Status);
        var attempt = Assert.Single(checkpoint.Run.Attempts);
        Assert.Equal(StepAttemptOutcome.Succeeded, attempt.Outcome);
        Assert.Equal(1, attempt.AttemptNumber);
        Assert.Equal(Fixture.Digest, attempt.OutputDigest);
        var evidence = Assert.Single(checkpoint.Evidence);
        Assert.Equal(ExecutionEvidenceKind.Step, evidence.Kind);
        Assert.Equal(ExecutionEvidenceStatus.Passed, evidence.Status);
        Assert.Equal(Fixture.Digest, evidence.OutputDigest);
        Assert.Equal(4, fixture.Store.Snapshots.Count);
        Assert.Equal(RunStatus.Planning, fixture.Store.Snapshots[0].Run.Status);
        Assert.Equal(RunStatus.Executing, fixture.Store.Snapshots[1].Run.Status);
        Assert.Empty(fixture.Store.Snapshots[0].Run.Attempts);
        Assert.Equal(
            StepAttemptOutcome.Running,
            Assert.Single(fixture.Store.Snapshots[2].Run.Attempts).Outcome);
    }

    [Fact]
    public async Task AutomaticRetryPersistsFailureCleansThenRunsABoundedSecondAttempt()
    {
        var fixture = Fixture.Create(
            RetryPolicy.AutomaticLimited(2, TimeSpan.FromTicks(1)).Value,
            events => new FailOnceHandler(events));

        var checkpoint = await fixture.Orchestrator.ExecuteAsync(
            fixture.Request,
            progress: null,
            CancellationToken.None);

        Assert.Equal(2, checkpoint.Run.Attempts.Length);
        Assert.Equal(StepAttemptOutcome.Failed, checkpoint.Run.Attempts[0].Outcome);
        Assert.Equal(StepAttemptOutcome.Succeeded, checkpoint.Run.Attempts[1].Outcome);
        Assert.Single(checkpoint.Run.Errors);
        Assert.True(
            fixture.Events.IndexOf("handler.cleanup")
                < fixture.Events.LastIndexOf("handler.prepare"));
        Assert.Equal(1, fixture.Events.Count(item => item == "handler.cleanup"));
    }

    [Fact]
    public async Task ManualRetryRecordsFailureAndWaitsWithoutImplicitCleanupOrRerun()
    {
        var fixture = Fixture.Create(
            RetryPolicy.Manual(2).Value,
            events => new FailOnceHandler(events));

        var checkpoint = await fixture.Orchestrator.ExecuteAsync(
            fixture.Request,
            progress: null,
            CancellationToken.None);

        Assert.Equal(RunStatus.Executing, checkpoint.Run.Status);
        Assert.Equal(StepAttemptOutcome.Failed, Assert.Single(checkpoint.Run.Attempts).Outcome);
        Assert.Single(checkpoint.Run.Errors);
        Assert.DoesNotContain("handler.cleanup", fixture.Events);
        Assert.Equal(1, fixture.Events.Count(item => item == "handler.execute"));
    }

    [Fact]
    public async Task CancellationIsDurablyRecordedBeforeItPropagates()
    {
        var fixture = Fixture.Create(
            handlerFactory: events => new CancellingHandler(events));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Orchestrator.ExecuteAsync(
                fixture.Request,
                progress: null,
                CancellationToken.None));

        var checkpoint = fixture.Store.Snapshots[^1];
        Assert.Equal(RunStatus.Cancelled, checkpoint.Run.Status);
        Assert.Equal(StepAttemptOutcome.Cancelled, Assert.Single(checkpoint.Run.Attempts).Outcome);
        Assert.Equal("lease.dispose", fixture.Events[^1]);
    }

    [Fact]
    public async Task CancelledHandlerResultUsesTheSameDurableCancellationPath()
    {
        var fixture = Fixture.Create(
            handlerFactory: events => new CancellingHandler(events, returnsResult: true));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Orchestrator.ExecuteAsync(
                fixture.Request,
                progress: null,
                CancellationToken.None));

        var checkpoint = fixture.Store.Snapshots[^1];
        Assert.Equal(RunStatus.Cancelled, checkpoint.Run.Status);
        Assert.Equal(StepAttemptOutcome.Cancelled, Assert.Single(checkpoint.Run.Attempts).Outcome);
    }

    [Fact]
    public async Task CancellationDuringRetryDelayPersistsCancelledRunBeforePropagation()
    {
        var fixture = Fixture.Create(
            RetryPolicy.AutomaticLimited(2, TimeSpan.FromMinutes(5)).Value,
            events => new FailOnceHandler(events));
        using var cancellation = new CancellationTokenSource();
        var execution = fixture.Orchestrator.ExecuteAsync(
            fixture.Request,
            progress: null,
            cancellation.Token);
        await fixture.Store.FailedAttemptSaved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);

        Assert.Equal(RunStatus.Cancelled, fixture.Store.Snapshots[^1].Run.Status);
    }

    [Fact]
    public async Task RetryCleanupFailureIsPersistedAndStopsTheRun()
    {
        var fixture = Fixture.Create(
            RetryPolicy.AutomaticLimited(2, TimeSpan.FromTicks(1)).Value,
            events => new FailOnceHandler(events, cleanupFails: true));

        var checkpoint = await fixture.Orchestrator.ExecuteAsync(
            fixture.Request,
            progress: null,
            CancellationToken.None);

        Assert.Equal(RunStatus.Failed, checkpoint.Run.Status);
        Assert.Equal(2, checkpoint.Run.Errors.Length);
        Assert.Equal("DF-FS-002", checkpoint.Run.Errors[^1].Code);
        Assert.Single(checkpoint.Run.Attempts);
    }

    [Fact]
    public async Task PostconditionFailureRetainsExecuteExitCodeAndDigestEvidence()
    {
        var fixture = Fixture.Create(
            handlerFactory: events => new PostconditionFailureHandler(events));

        var checkpoint = await fixture.Orchestrator.ExecuteAsync(
            fixture.Request,
            progress: null,
            CancellationToken.None);

        var attempt = Assert.Single(checkpoint.Run.Attempts);
        Assert.Equal(StepAttemptOutcome.Failed, attempt.Outcome);
        Assert.Equal(0, attempt.ExitCode);
        Assert.Equal(Fixture.Digest, attempt.OutputDigest);
        Assert.Equal("DF-EXEC-001", attempt.Error?.Code);
    }

    [Fact]
    public async Task ResumeSkipsSuccessfulStepOnlyAfterItsPostconditionPasses()
    {
        var fixture = Fixture.CreateResume(
            events => new ResumeHandler(events, postconditionPasses: true));

        var checkpoint = await fixture.Orchestrator.ExecuteAsync(
            fixture.Request,
            progress: null,
            CancellationToken.None);

        Assert.Single(checkpoint.Run.Attempts);
        Assert.Equal(RunStatus.Executing, checkpoint.Run.Status);
        Assert.Contains("handler.postcondition", fixture.Events);
        Assert.DoesNotContain("handler.execute", fixture.Events);
        Assert.Equal(
            [
                "checkpoint.find",
                "staging.validate",
                "blueprint.open",
                "registry.create",
                "checkpoint.save.succeeded",
                "handler.postcondition",
                "completion.run",
                "lease.dispose",
            ],
            fixture.Events);
    }

    [Fact]
    public async Task ResumeCleansAndRerunsFromTheFirstDriftedStep()
    {
        var fixture = Fixture.CreateResume(
            events => new ResumeHandler(events, postconditionPasses: false));

        var checkpoint = await fixture.Orchestrator.ExecuteAsync(
            fixture.Request,
            progress: null,
            CancellationToken.None);

        Assert.Equal(2, checkpoint.Run.Attempts.Length);
        Assert.Equal(StepAttemptOutcome.Succeeded, checkpoint.Run.Attempts[^1].Outcome);
        Assert.Equal(1, fixture.Events.Count(item => item == "handler.cleanup"));
        Assert.Equal(1, fixture.Events.Count(item => item == "handler.execute"));
        Assert.True(
            fixture.Events.IndexOf("handler.postcondition")
                < fixture.Events.IndexOf("handler.cleanup"));
    }

    [Fact]
    public async Task ResumeReplacesStagingAndReplaysPlanForOpaqueProcessMutations()
    {
        var fixture = Fixture.CreateResume(
            events => new ResumeHandler(
                events,
                postconditionPasses: true,
                ExecutionResumeBehavior.ReplayFromFreshStaging));

        var checkpoint = await fixture.Orchestrator.ExecuteAsync(
            fixture.Request,
            progress: null,
            CancellationToken.None);

        Assert.Equal(2, checkpoint.Run.Attempts.Length);
        Assert.Contains("staging.recreate", fixture.Events);
        Assert.True(
            fixture.Events.IndexOf("lease.dispose")
                < fixture.Events.IndexOf("staging.recreate"));
        Assert.Equal(1, fixture.Events.Count(item => item == "handler.postcondition"));
        Assert.Equal(1, fixture.Events.Count(item => item == "handler.execute"));
    }

    [Fact]
    public async Task ResumeOfCancelledOpaqueStepReplacesStagingBeforeReplayingPlan()
    {
        var fixture = Fixture.CreateResume(
            events => new ResumeHandler(
                events,
                postconditionPasses: true,
                ExecutionResumeBehavior.ReplayFromFreshStaging),
            retryPolicy: RetryPolicy.Manual(2).Value,
            resumeAttemptOutcome: StepAttemptOutcome.Cancelled);

        var checkpoint = await fixture.Orchestrator.ExecuteAsync(
            fixture.Request,
            progress: null,
            CancellationToken.None);

        Assert.Contains("staging.recreate", fixture.Events);
        Assert.Equal(1, fixture.Events.Count(item => item == "handler.execute"));
        Assert.Equal(StepAttemptOutcome.Cancelled, checkpoint.Run.Attempts[0].Outcome);
        Assert.Equal(StepAttemptOutcome.Succeeded, checkpoint.Run.Attempts[1].Outcome);
    }

    [Fact]
    public async Task ResumeOfCancelledFileStepCleansDeclaredOutputsBeforeRerun()
    {
        var fixture = Fixture.CreateResume(
            events => new ResumeHandler(events, postconditionPasses: true),
            retryPolicy: RetryPolicy.Manual(2).Value,
            resumeAttemptOutcome: StepAttemptOutcome.Cancelled);

        var checkpoint = await fixture.Orchestrator.ExecuteAsync(
            fixture.Request,
            progress: null,
            CancellationToken.None);

        Assert.Contains("handler.cleanup", fixture.Events);
        Assert.True(
            fixture.Events.IndexOf("handler.cleanup")
                < fixture.Events.IndexOf("handler.execute"));
        Assert.Equal(StepAttemptOutcome.Succeeded, checkpoint.Run.Attempts[^1].Outcome);
    }

    [Fact]
    public async Task CancelledResumePostconditionIsPersistedBeforeCancellationPropagates()
    {
        var fixture = Fixture.CreateResume(
            events => new ResumeHandler(
                events,
                postconditionPasses: false,
                postconditionCancelled: true));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Orchestrator.ExecuteAsync(
                fixture.Request,
                progress: null,
                CancellationToken.None));

        Assert.Equal(RunStatus.Cancelled, fixture.Store.Snapshots[^1].Run.Status);
        Assert.DoesNotContain("handler.cleanup", fixture.Events);
        Assert.DoesNotContain("handler.execute", fixture.Events);
    }

    [Fact]
    public async Task FailedFreshStagingReplayPersistsStableFailureAndStopsExecution()
    {
        var failure = FailureError("replay");
        var fixture = Fixture.CreateResume(
            events => new ResumeHandler(
                events,
                postconditionPasses: true,
                ExecutionResumeBehavior.ReplayFromFreshStaging),
            replayError: failure);

        var checkpoint = await fixture.Orchestrator.ExecuteAsync(
            fixture.Request,
            progress: null,
            CancellationToken.None);

        Assert.Equal(RunStatus.Failed, checkpoint.Run.Status);
        Assert.Equal("DF-EXEC-003", checkpoint.Run.Errors[^1].Code);
        Assert.DoesNotContain("handler.execute", fixture.Events);
    }

    [Fact]
    public async Task ExplicitManualRetryCleansAndContinuesThePersistedFailedStep()
    {
        var fixture = Fixture.CreateManualRetry(
            events => new ResumeHandler(events, postconditionPasses: true));

        var checkpoint = await fixture.Orchestrator.ExecuteAsync(
            fixture.Request,
            progress: null,
            CancellationToken.None);

        Assert.Equal(2, checkpoint.Run.Attempts.Length);
        Assert.Equal(StepAttemptOutcome.Failed, checkpoint.Run.Attempts[0].Outcome);
        Assert.Equal(StepAttemptOutcome.Succeeded, checkpoint.Run.Attempts[1].Outcome);
        Assert.Equal(1, fixture.Events.Count(item => item == "handler.cleanup"));
        Assert.Equal(1, fixture.Events.Count(item => item == "handler.execute"));
    }

    [Fact]
    public async Task MissingBlueprintPersistsStableFailureAndDoesNotDispatchAHandler()
    {
        var failure = FailureError("blueprint");
        var fixture = Fixture.CreateResume(
            events => new ResumeHandler(events, postconditionPasses: true),
            blueprintError: failure);

        var checkpoint = await fixture.Orchestrator.ExecuteAsync(
            fixture.Request,
            progress: null,
            CancellationToken.None);

        Assert.Equal(RunStatus.Failed, checkpoint.Run.Status);
        Assert.Equal("DF-EXEC-003", checkpoint.Run.Errors[^1].Code);
        Assert.DoesNotContain(fixture.Events, item => item.StartsWith("handler.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MarkerMismatchFailsCheckpointBeforeBlueprintOrHandlerAccess()
    {
        var failure = FailureError("staging");
        var fixture = Fixture.CreateResume(
            events => new ResumeHandler(events, postconditionPasses: true),
            ownershipError: failure);

        var checkpoint = await fixture.Orchestrator.ExecuteAsync(
            fixture.Request,
            progress: null,
            CancellationToken.None);

        Assert.Equal(RunStatus.Failed, checkpoint.Run.Status);
        Assert.Equal("DF-EXEC-003", checkpoint.Run.Errors[^1].Code);
        Assert.DoesNotContain("blueprint.open", fixture.Events);
        Assert.DoesNotContain("registry.create", fixture.Events);
    }

    [Fact]
    public async Task ThrowingProgressObserverCannotBreakCheckpointedExecution()
    {
        var fixture = Fixture.Create(
            handlerFactory: events => new ProgressReportingHandler(events));

        var checkpoint = await fixture.Orchestrator.ExecuteAsync(
            fixture.Request,
            new ThrowingProgress(),
            CancellationToken.None);

        Assert.Equal(StepAttemptOutcome.Succeeded, Assert.Single(checkpoint.Run.Attempts).Outcome);
    }

    [Fact]
    public async Task ConcurrentExecutionFailsWithStableErrorBeforeSecondMutation()
    {
        var handler = new BlockingHandler();
        var fixture = Fixture.Create(handlerFactory: _ => handler);
        var first = fixture.Orchestrator.ExecuteAsync(
            fixture.Request,
            progress: null,
            CancellationToken.None);
        await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var error = await Assert.ThrowsAsync<ExecutionOrchestratorBusyException>(() =>
            fixture.Orchestrator.ExecuteAsync(
                fixture.Request,
                progress: null,
                CancellationToken.None));

        Assert.Equal("DF-EXEC-001", error.Code);
        Assert.Equal(1, fixture.Events.Count(item => item == "staging.create"));
        handler.Release.TrySetResult();
        await first;
    }

    [Fact]
    public async Task ProcessWideLeaseRejectsASecondOrchestratorInstanceBeforeMutation()
    {
        var firstHandler = new BlockingHandler();
        var firstFixture = Fixture.Create(handlerFactory: _ => firstHandler);
        var secondFixture = Fixture.Create();
        var first = firstFixture.Orchestrator.ExecuteAsync(
            firstFixture.Request,
            progress: null,
            CancellationToken.None);
        await firstHandler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.ThrowsAsync<ExecutionOrchestratorBusyException>(() =>
            secondFixture.Orchestrator.ExecuteAsync(
                secondFixture.Request,
                progress: null,
                CancellationToken.None));

        Assert.DoesNotContain("staging.create", secondFixture.Events);
        firstHandler.Release.TrySetResult();
        await first;
    }

    [Fact]
    public async Task PlanMismatchFailsClosedBeforeStagingValidation()
    {
        var fixture = Fixture.CreateResume(
            events => new ResumeHandler(events, postconditionPasses: true));
        var persisted = fixture.Store.Snapshots[0];
        var mismatchedPlan = ExecutionPlan.Create(
            $"sha256:{new string('9', 64)}",
            persisted.Plan.Steps,
            persisted.Plan.Validators,
            persisted.Plan.TemplateContext.Select(item =>
                KeyValuePair.Create<string, string?>(item.Key, item.Value))).Value;
        fixture.Store.ReplacePreload(RunCheckpoint.Create(
            persisted.Run,
            mismatchedPlan,
            persisted.Blueprint,
            persisted.BlueprintFingerprint,
            persisted.Staging,
            persisted.Target,
            persisted.RunArtifacts,
            persisted.Evidence,
            persisted.FinalizationState,
            persisted.ReportState).Value);

        var error = await Assert.ThrowsAsync<ExecutionCheckpointMismatchException>(() =>
            fixture.Orchestrator.ExecuteAsync(
                fixture.Request,
                progress: null,
                CancellationToken.None));

        Assert.Equal("DF-EXEC-003", error.Code);
        Assert.DoesNotContain("staging.validate", fixture.Events);
    }

    [Fact]
    public async Task TerminalPersistedRunRejectsAStaleResumeRequestBeforeStagingMutation()
    {
        var fixture = Fixture.CreateResume(
            events => new ResumeHandler(events, postconditionPasses: true));
        var persisted = fixture.Store.Snapshots[0];
        var failed = persisted.Run.ResumeExecution().Value
            .TransitionTo(RunStatus.Failed).Value;
        fixture.ReplacePersistedRun(failed);

        await Assert.ThrowsAsync<ExecutionCheckpointMismatchException>(() =>
            fixture.Orchestrator.ExecuteAsync(
                fixture.Request,
                progress: null,
                CancellationToken.None));

        Assert.DoesNotContain("staging.validate", fixture.Events);
    }

    [Fact]
    public async Task CancelledPersistedRunRejectsAStaleManualRetryRequestBeforeMutation()
    {
        var fixture = Fixture.CreateManualRetry(
            events => new ResumeHandler(events, postconditionPasses: true));
        var persisted = fixture.Store.Snapshots[0];
        fixture.ReplacePersistedRun(
            persisted.Run.TransitionTo(RunStatus.Cancelled).Value);

        await Assert.ThrowsAsync<ExecutionCheckpointMismatchException>(() =>
            fixture.Orchestrator.ExecuteAsync(
                fixture.Request,
                progress: null,
                CancellationToken.None));

        Assert.DoesNotContain("staging.validate", fixture.Events);
    }

    [Fact]
    public async Task FreshStagingFailureReturnsStableStartErrorBeforeCheckpointMutation()
    {
        var fixture = Fixture.Create(createError: FailureError("create"));

        var error = await Assert.ThrowsAsync<ExecutionStartException>(() =>
            fixture.Orchestrator.ExecuteAsync(
                fixture.Request,
                progress: null,
                CancellationToken.None));

        Assert.Equal("DF-EXEC-003", error.Code);
        Assert.Empty(fixture.Store.Snapshots);
        Assert.DoesNotContain("blueprint.open", fixture.Events);
    }

    [Fact]
    public async Task InitialCheckpointPersistenceFailureCleansTheUntrackedOwnedStaging()
    {
        var fixture = Fixture.Create(initialSaveFails: true);

        await Assert.ThrowsAsync<IOException>(() =>
            fixture.Orchestrator.ExecuteAsync(
                fixture.Request,
                progress: null,
                CancellationToken.None));

        Assert.Empty(fixture.Store.Snapshots);
        Assert.Contains("staging.cleanup", fixture.Events);
        Assert.DoesNotContain("blueprint.open", fixture.Events);
    }

    [Fact]
    public async Task CancellationDuringBlueprintOpenPersistsCancelledCheckpointBeforePropagation()
    {
        var fixture = Fixture.Create(blueprintCancels: true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Orchestrator.ExecuteAsync(
                fixture.Request,
                progress: null,
                CancellationToken.None));

        Assert.Equal(RunStatus.Cancelled, fixture.Store.Snapshots[^1].Run.Status);
        Assert.Empty(fixture.Store.Snapshots[^1].Run.Attempts);
    }

    private sealed class Fixture
    {
        public const string Digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        private Fixture(
            ExecutionRequest request,
            List<string> events,
            RecordingCheckpointStore store,
            CheckpointedExecutionOrchestrator orchestrator)
        {
            Request = request;
            Events = events;
            Store = store;
            Orchestrator = orchestrator;
        }

        public ExecutionRequest Request { get; }

        public List<string> Events { get; }

        public RecordingCheckpointStore Store { get; }

        public CheckpointedExecutionOrchestrator Orchestrator { get; }

        public void ReplacePersistedRun(ProjectRun run)
        {
            var checkpoint = Store.Snapshots[0];
            Store.ReplacePreload(RunCheckpoint.Create(
                run,
                checkpoint.Plan,
                checkpoint.Blueprint,
                checkpoint.BlueprintFingerprint,
                checkpoint.Staging,
                checkpoint.Target,
                checkpoint.RunArtifacts,
                checkpoint.Evidence,
                checkpoint.FinalizationState,
                checkpoint.ReportState).Value);
        }

        public static Fixture Create(
            RetryPolicy? retryPolicy = null,
            Func<List<string>, IExecutionHandler>? handlerFactory = null,
            ExecutionMode mode = ExecutionMode.Fresh,
            DevForgeError? blueprintError = null,
            DevForgeError? ownershipError = null,
            DevForgeError? replayError = null,
            DevForgeError? createError = null,
            bool initialSaveFails = false,
            bool blueprintCancels = false,
            StepAttemptOutcome resumeAttemptOutcome = StepAttemptOutcome.Succeeded,
            bool completionLocalReady = false,
            bool includeFinalizationBoundary = false,
            DevForgeError? cleanupFinalizedError = null)
        {
            var events = new List<string>();
            var target = new StubWorkspace("C:\\target-parent");
            var artifacts = new StubWorkspace("C:\\run-artifacts");
            var payload = new StubWorkspace("C:\\target-parent\\.devforge-staging\\run-1\\payload");
            var step = ExecutionStep.Create(
                "create",
                "Create",
                "create-directory",
                [],
                TimeSpan.FromSeconds(30),
                retryPolicy ?? RetryPolicy.None).Value;
            var steps = new List<ExecutionStep> { step };
            if (includeFinalizationBoundary)
            {
                steps.Add(ExecutionStep.Create(
                    "finalize",
                    "Finalize",
                    "finalize-workspace",
                    [],
                    TimeSpan.FromSeconds(30),
                    RetryPolicy.None).Value);
            }

            var plan = ExecutionPlan.Create(
                $"sha256:{new string('1', 64)}",
                steps,
                [],
                []).Value;
            var blueprint = BlueprintReference.Create("desktop.csharp-wpf-tool", "1.0.0").Value;
            var fingerprint = BlueprintFingerprint.Create(
                "built-in",
                Path("desktop.csharp-wpf-tool\\1.0.0"),
                BlueprintTrust.BuiltIn,
                $"sha256:{new string('2', 64)}").Value;
            var preview = PlanPreview.Create(
                blueprint,
                [new PlanPreviewStep(step.Id, step.Handler, step.Timeout)],
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
                plan.Id).Value;
            var planned = PlannedProject.Create(plan, preview, fingerprint).Value;
            var run = ProjectRun.Create("run-1", "recipe-1").Value;
            ExecutionEvidence? priorEvidence = null;
            if (mode == ExecutionMode.Resume)
            {
                run = run.TransitionTo(RunStatus.Planning).Value
                    .TransitionTo(RunStatus.Executing).Value;
                run = run.StartAttempt(step.Id, DateTimeOffset.UnixEpoch).Value;
                run = run.CompleteAttempt(
                    step.Id,
                    1,
                    resumeAttemptOutcome,
                    DateTimeOffset.UnixEpoch.AddSeconds(1),
                    null,
                    null,
                    resumeAttemptOutcome == StepAttemptOutcome.Succeeded ? Digest : null).Value;
                run = run.TransitionTo(RunStatus.Cancelled).Value;
                if (resumeAttemptOutcome == StepAttemptOutcome.Succeeded)
                {
                    priorEvidence = ExecutionEvidence.Create(
                        ExecutionEvidenceKind.Step,
                        step.Id,
                        ExecutionEvidenceStatus.Passed,
                        Digest).Value;
                }
            }
            else if (mode == ExecutionMode.ManualRetry)
            {
                run = run.TransitionTo(RunStatus.Planning).Value
                    .TransitionTo(RunStatus.Executing).Value;
                run = run.StartAttempt(step.Id, DateTimeOffset.UnixEpoch).Value;
                var error = DevForgeError.Create(
                    "DF-FS-002",
                    "The guarded output was temporarily unavailable.",
                    RedactedText.FromTrustedRedaction(
                        "A transient guarded file operation failed.").Value,
                    "execute",
                    step.Id,
                    true,
                    [],
                    []).Value;
                run = run.CompleteAttempt(
                    step.Id,
                    1,
                    StepAttemptOutcome.Failed,
                    DateTimeOffset.UnixEpoch.AddSeconds(1),
                    null,
                    error,
                    null).Value;
                run = run.AppendError(error).Value;
            }

            var request = ExecutionRequest.Create(
                planned,
                run,
                target,
                Path("project"),
                artifacts,
                mode).Value;
            var descriptor = StagingDescriptor.Create(
                Path(".devforge-staging\\run-1"),
                Path(".devforge-staging\\run-1\\payload"),
                Path(".devforge-staging\\run-1\\ownership.json"),
                "run-1").Value;
            var staging = StagingWorkspace.Create(descriptor, payload).Value;
            var package = Package(fingerprint);
            var store = new RecordingCheckpointStore(events, initialSaveFails);
            if (mode is ExecutionMode.Resume or ExecutionMode.ManualRetry)
            {
                store.Preload(RunCheckpoint.Create(
                    run,
                    plan,
                    blueprint,
                    fingerprint,
                    descriptor,
                    TargetDescriptor.Create(target.Root, Path("project"), null).Value,
                    RunArtifactDescriptor.Create(artifacts.Root).Value,
                    priorEvidence is null ? [] : [priorEvidence],
                    FinalizationState.NotStarted,
                    ReportPersistenceState.NotStarted).Value);
            }

            var handler = handlerFactory?.Invoke(events) ?? new RecordingHandler(events);
            var orchestrator = new CheckpointedExecutionOrchestrator(
                store,
                new RecordingStagingManager(
                    events,
                    staging,
                    ownershipError,
                    replayError,
                    createError,
                    cleanupFinalizedError),
                new RecordingBlueprintSource(
                    events,
                    package,
                    blueprintError,
                    blueprintCancels),
                new RecordingRegistryProvider(events, handler),
                new RecordingCompletionCoordinator(events, completionLocalReady),
                TimeProvider.System);
            return new Fixture(request, events, store, orchestrator);
        }

        public static Fixture CreateResume(
            Func<List<string>, IExecutionHandler> handlerFactory,
            DevForgeError? blueprintError = null,
            DevForgeError? ownershipError = null,
            DevForgeError? replayError = null,
            RetryPolicy? retryPolicy = null,
            StepAttemptOutcome resumeAttemptOutcome = StepAttemptOutcome.Succeeded) => Create(
                retryPolicy,
                handlerFactory: handlerFactory,
                mode: ExecutionMode.Resume,
                blueprintError: blueprintError,
                ownershipError: ownershipError,
                replayError: replayError,
                resumeAttemptOutcome: resumeAttemptOutcome);

        public static Fixture CreateManualRetry(
            Func<List<string>, IExecutionHandler> handlerFactory) => Create(
                RetryPolicy.Manual(2).Value,
                handlerFactory,
                ExecutionMode.ManualRetry);

        private static BlueprintExecutionPackage Package(BlueprintFingerprint fingerprint)
        {
            var manifest = BlueprintManifest.Create(
                new BlueprintManifestDraft(
                    "desktop.csharp-wpf-tool",
                    "1.0.0",
                    ">=1.0.0 <2.0.0",
                    [],
                    [],
                    [],
                    [],
                    []),
                new BlueprintTrustAssignment(BlueprintTrust.BuiltIn)).Value;
            var resolved = ResolvedBlueprint.Create(manifest, [], fingerprint).Value;
            return BlueprintExecutionPackage.Create(
                resolved,
                new StubWorkspace("C:\\blueprint-package")).Value;
        }
    }

    private sealed class RecordingCheckpointStore(
        List<string> events,
        bool failFirstSave = false) : IRunCheckpointStore
    {
        public List<RunCheckpoint> Snapshots { get; } = [];

        public TaskCompletionSource FailedAttemptSaved { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public void Preload(RunCheckpoint checkpoint)
        {
            Snapshots.Add(checkpoint);
        }

        public void ReplacePreload(RunCheckpoint checkpoint)
        {
            Snapshots.Clear();
            Snapshots.Add(checkpoint);
        }

        public Task SaveAsync(RunCheckpoint checkpoint, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (failFirstSave && Snapshots.Count == 0)
            {
                throw new IOException("Injected initial checkpoint persistence failure.");
            }

            var suffix = checkpoint.Run.Attempts.LastOrDefault()?.Outcome switch
            {
                StepAttemptOutcome.Running => "running",
                StepAttemptOutcome.Succeeded => "succeeded",
                _ => "initial",
            };
            events.Add("checkpoint.save." + suffix);
            Snapshots.Add(checkpoint);
            if (checkpoint.Run.Attempts.LastOrDefault()?.Outcome == StepAttemptOutcome.Failed)
            {
                FailedAttemptSaved.TrySetResult();
            }

            return Task.CompletedTask;
        }

        public Task<RunCheckpoint?> FindAsync(string runId, CancellationToken cancellationToken)
        {
            events.Add("checkpoint.find");
            return Task.FromResult<RunCheckpoint?>(Snapshots.LastOrDefault());
        }

        public Task<ImmutableArray<RunCheckpoint>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Snapshots.ToImmutableArray());
    }

    private sealed class RecordingStagingManager(
        List<string> events,
        StagingWorkspace workspace,
        DevForgeError? ownershipError,
        DevForgeError? replayError,
        DevForgeError? createError,
        DevForgeError? cleanupFinalizedError) : IStagingWorkspaceManager
    {
        public Task<ExecutionOperationResult<IStagingWorkspaceLease>> CreateAsync(
            ExecutionRequest request,
            CancellationToken cancellationToken)
        {
            events.Add("staging.create");
            return Task.FromResult(createError is null
                ? ExecutionOperationResult.Success<IStagingWorkspaceLease>(
                    new RecordingLease(events, workspace))
                : ExecutionOperationResult.Failure<IStagingWorkspaceLease>(createError));
        }

        public Task<ExecutionOperationResult<IStagingWorkspaceLease>> ValidateOwnershipAsync(
            RunCheckpoint checkpoint,
            IWorkspaceFileSystem targetParentWorkspace,
            CancellationToken cancellationToken)
        {
            events.Add("staging.validate");
            return Task.FromResult(ownershipError is null
                ? ExecutionOperationResult.Success<IStagingWorkspaceLease>(
                    new RecordingLease(events, workspace))
                : ExecutionOperationResult.Failure<IStagingWorkspaceLease>(ownershipError));
        }

        public Task<ExecutionOperationResult<IStagingWorkspaceLease>> RecreateForReplayAsync(
            RunCheckpoint checkpoint,
            ExecutionRequest request,
            CancellationToken cancellationToken)
        {
            events.Add("staging.recreate");
            return Task.FromResult(replayError is null
                ? ExecutionOperationResult.Success<IStagingWorkspaceLease>(
                    new RecordingLease(events, workspace))
                : ExecutionOperationResult.Failure<IStagingWorkspaceLease>(replayError));
        }

        public Task<ExecutionOperationResult<StagingCleanupReceipt>> CleanupAsync(
            RunCheckpoint checkpoint,
            IWorkspaceFileSystem targetParentWorkspace,
            CancellationToken cancellationToken)
        {
            events.Add("staging.cleanup");
            return Task.FromResult(ExecutionOperationResult.Success(
                StagingCleanupReceipt.Create(
                    checkpoint.Run.Id,
                    checkpoint.Staging.MarkerId).Value));
        }

        public Task<ExecutionOperationResult<StagingCleanupReceipt>> CleanupFinalizedAsync(
            RunCheckpoint checkpoint,
            IWorkspaceFileSystem targetParentWorkspace,
            CancellationToken cancellationToken)
        {
            events.Add("staging.cleanup-finalized");
            return Task.FromResult(cleanupFinalizedError is null
                ? ExecutionOperationResult.Success(
                    StagingCleanupReceipt.Create(
                        checkpoint.Run.Id,
                        checkpoint.Staging.MarkerId).Value)
                : ExecutionOperationResult.Failure<StagingCleanupReceipt>(cleanupFinalizedError));
        }
    }

    private sealed class RecordingLease(
        List<string> events,
        StagingWorkspace workspace) : IStagingWorkspaceLease
    {
        public StagingWorkspace Workspace { get; } = workspace;

        public ValueTask DisposeAsync()
        {
            events.Add("lease.dispose");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingBlueprintSource(
        List<string> events,
        BlueprintExecutionPackage package,
        DevForgeError? error,
        bool cancels) : IBlueprintExecutionSource
    {
        public Task<ExecutionOperationResult<BlueprintExecutionPackage>> OpenAsync(
            BlueprintReference blueprint,
            BlueprintFingerprint fingerprint,
            CancellationToken cancellationToken)
        {
            events.Add("blueprint.open");
            if (cancels)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            return Task.FromResult(error is null
                ? ExecutionOperationResult.Success(package)
                : ExecutionOperationResult.Failure<BlueprintExecutionPackage>(error));
        }
    }

    private sealed class RecordingRegistryProvider(
        List<string> events,
        IExecutionHandler handler) : IExecutionHandlerRegistryProvider
    {
        public ExecutionOperationResult<IExecutionHandlerRegistry> Create(BlueprintTrust trust)
        {
            events.Add("registry.create");
            return ExecutionOperationResult.Success<IExecutionHandlerRegistry>(
                new RecordingRegistry(handler));
        }
    }

    private sealed class RecordingRegistry(IExecutionHandler handler) : IExecutionHandlerRegistry
    {
        public IExecutionHandler? Resolve(string handlerId) =>
            StringComparer.Ordinal.Equals(handlerId, handler.Id) ? handler : null;
    }

    private sealed class RecordingCompletionCoordinator(
        List<string> events,
        bool localReady) : IRunCompletionCoordinator
    {
        public Task<RunCheckpoint> CompleteAsync(
            ExecutionRequest request,
            RunCheckpoint checkpoint,
            StagingWorkspace staging,
            BlueprintExecutionPackage blueprintPackage,
            IExecutionHandlerRegistry registry,
            IProgress<ExecutionProgressLine>? progress,
            CancellationToken cancellationToken)
        {
            events.Add("completion.run");
            if (!localReady)
            {
                return Task.FromResult(checkpoint);
            }

            return Task.FromResult(RunCheckpoint.Create(
                checkpoint.Run.TransitionTo(RunStatus.LocalReady).Value,
                checkpoint.Plan,
                checkpoint.Blueprint,
                checkpoint.BlueprintFingerprint,
                checkpoint.Staging,
                checkpoint.Target,
                checkpoint.RunArtifacts,
                checkpoint.Evidence,
                FinalizationState.Succeeded,
                ReportPersistenceState.Succeeded).Value);
        }
    }

    private sealed class RecordingHandler(List<string> events) : IExecutionHandler
    {
        public string Id => "create-directory";

        public ExecutionResumeBehavior ResumeBehavior =>
            ExecutionResumeBehavior.RevalidatePostcondition;

        public Task<ExecutionHandlerResult> PrepareAsync(
            ExecutionHandlerRequest request,
            CancellationToken cancellationToken) => Result("prepare", ExecutionPhase.Prepare);

        public Task<ExecutionHandlerResult> CheckPreconditionsAsync(
            ExecutionHandlerRequest request,
            CancellationToken cancellationToken) => Result("precondition", ExecutionPhase.Precondition);

        public Task<ExecutionHandlerResult> ExecuteAsync(
            ExecutionHandlerRequest request,
            IProgress<ExecutionProgressLine>? progress,
            CancellationToken cancellationToken) => Result("execute", ExecutionPhase.Execute, Fixture.Digest);

        public Task<ExecutionHandlerResult> CheckPostconditionsAsync(
            ExecutionHandlerRequest request,
            CancellationToken cancellationToken) => Result("postcondition", ExecutionPhase.Postcondition);

        public Task<ExecutionHandlerResult> CleanupForRetryAsync(
            ExecutionHandlerRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        private Task<ExecutionHandlerResult> Result(
            string name,
            ExecutionPhase phase,
            string? digest = null)
        {
            events.Add("handler." + name);
            return Task.FromResult(ExecutionHandlerResult.Create(
                phase,
                ExecutionHandlerOutcome.Succeeded,
                phase == ExecutionPhase.Execute ? 0 : null,
                digest,
                null,
                []).Value);
        }
    }

    private sealed class FailOnceHandler(
        List<string> events,
        bool cleanupFails = false) : IExecutionHandler
    {
        private int _executeCount;

        public string Id => "create-directory";

        public ExecutionResumeBehavior ResumeBehavior =>
            ExecutionResumeBehavior.RevalidatePostcondition;

        public Task<ExecutionHandlerResult> PrepareAsync(
            ExecutionHandlerRequest request,
            CancellationToken cancellationToken) => Success("prepare", ExecutionPhase.Prepare);

        public Task<ExecutionHandlerResult> CheckPreconditionsAsync(
            ExecutionHandlerRequest request,
            CancellationToken cancellationToken) => Success("precondition", ExecutionPhase.Precondition);

        public Task<ExecutionHandlerResult> ExecuteAsync(
            ExecutionHandlerRequest request,
            IProgress<ExecutionProgressLine>? progress,
            CancellationToken cancellationToken)
        {
            events.Add("handler.execute");
            _executeCount++;
            if (_executeCount > 1)
            {
                return Task.FromResult(ExecutionHandlerResult.Create(
                    ExecutionPhase.Execute,
                    ExecutionHandlerOutcome.Succeeded,
                    null,
                    Fixture.Digest,
                    null,
                    []).Value);
            }

            var error = DevForgeError.Create(
                "DF-FS-002",
                "The guarded output was temporarily unavailable.",
                RedactedText.FromTrustedRedaction(
                    "A transient guarded file operation failed.").Value,
                "execute",
                request.ItemId,
                true,
                [],
                []).Value;
            return Task.FromResult(ExecutionHandlerResult.Create(
                ExecutionPhase.Execute,
                ExecutionHandlerOutcome.Failed,
                null,
                null,
                error,
                []).Value);
        }

        public Task<ExecutionHandlerResult> CheckPostconditionsAsync(
            ExecutionHandlerRequest request,
            CancellationToken cancellationToken) => Success("postcondition", ExecutionPhase.Postcondition);

        public Task<ExecutionHandlerResult> CleanupForRetryAsync(
            ExecutionHandlerRequest request,
            CancellationToken cancellationToken)
        {
            events.Add("handler.cleanup");
            if (cleanupFails)
            {
                var error = DevForgeError.Create(
                    "DF-FS-002",
                    "The guarded retry cleanup failed.",
                    RedactedText.FromTrustedRedaction(
                        "The declared output could not be cleaned safely.").Value,
                    "prepare",
                    request.ItemId,
                    false,
                    [],
                    []).Value;
                return Task.FromResult(ExecutionHandlerResult.Create(
                    ExecutionPhase.Prepare,
                    ExecutionHandlerOutcome.Failed,
                    null,
                    null,
                    error,
                    []).Value);
            }

            return Task.FromResult(ExecutionHandlerResult.Create(
                ExecutionPhase.Prepare,
                ExecutionHandlerOutcome.Succeeded,
                null,
                null,
                null,
                []).Value);
        }

        private Task<ExecutionHandlerResult> Success(string name, ExecutionPhase phase)
        {
            events.Add("handler." + name);
            return Task.FromResult(ExecutionHandlerResult.Create(
                phase,
                ExecutionHandlerOutcome.Succeeded,
                null,
                null,
                null,
                []).Value);
        }
    }

    private sealed class PostconditionFailureHandler(List<string> events) : IExecutionHandler
    {
        private readonly RecordingHandler _inner = new(events);

        public string Id => _inner.Id;

        public ExecutionResumeBehavior ResumeBehavior => _inner.ResumeBehavior;

        public Task<ExecutionHandlerResult> PrepareAsync(
            ExecutionHandlerRequest request,
            CancellationToken cancellationToken) => _inner.PrepareAsync(request, cancellationToken);

        public Task<ExecutionHandlerResult> CheckPreconditionsAsync(
            ExecutionHandlerRequest request,
            CancellationToken cancellationToken) =>
            _inner.CheckPreconditionsAsync(request, cancellationToken);

        public Task<ExecutionHandlerResult> ExecuteAsync(
            ExecutionHandlerRequest request,
            IProgress<ExecutionProgressLine>? progress,
            CancellationToken cancellationToken) =>
            _inner.ExecuteAsync(request, progress, cancellationToken);

        public Task<ExecutionHandlerResult> CheckPostconditionsAsync(
            ExecutionHandlerRequest request,
            CancellationToken cancellationToken)
        {
            events.Add("handler.postcondition");
            var error = DevForgeError.Create(
                "DF-EXEC-001",
                "The postcondition did not pass.",
                RedactedText.FromTrustedRedaction(
                    "The guarded output did not satisfy its postcondition.").Value,
                "postcondition",
                request.ItemId,
                false,
                [],
                []).Value;
            return Task.FromResult(ExecutionHandlerResult.Create(
                ExecutionPhase.Postcondition,
                ExecutionHandlerOutcome.Failed,
                null,
                null,
                error,
                []).Value);
        }

        public Task<ExecutionHandlerResult> CleanupForRetryAsync(
            ExecutionHandlerRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class CancellingHandler(
        List<string> events,
        bool returnsResult = false) : IExecutionHandler
    {
        public string Id => "create-directory";

        public ExecutionResumeBehavior ResumeBehavior =>
            ExecutionResumeBehavior.RevalidatePostcondition;

        public Task<ExecutionHandlerResult> PrepareAsync(
            ExecutionHandlerRequest request,
            CancellationToken cancellationToken) => Success("prepare", ExecutionPhase.Prepare);

        public Task<ExecutionHandlerResult> CheckPreconditionsAsync(
            ExecutionHandlerRequest request,
            CancellationToken cancellationToken) => Success("precondition", ExecutionPhase.Precondition);

        public Task<ExecutionHandlerResult> ExecuteAsync(
            ExecutionHandlerRequest request,
            IProgress<ExecutionProgressLine>? progress,
            CancellationToken cancellationToken)
        {
            events.Add("handler.execute.cancel");
            if (returnsResult)
            {
                return Task.FromResult(ExecutionHandlerResult.Create(
                    ExecutionPhase.Execute,
                    ExecutionHandlerOutcome.Cancelled,
                    null,
                    null,
                    null,
                    []).Value);
            }

            throw new OperationCanceledException(cancellationToken);
        }

        public Task<ExecutionHandlerResult> CheckPostconditionsAsync(
            ExecutionHandlerRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ExecutionHandlerResult> CleanupForRetryAsync(
            ExecutionHandlerRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        private Task<ExecutionHandlerResult> Success(string name, ExecutionPhase phase)
        {
            events.Add("handler." + name);
            return Task.FromResult(ExecutionHandlerResult.Create(
                phase,
                ExecutionHandlerOutcome.Succeeded,
                null,
                null,
                null,
                []).Value);
        }
    }

    private sealed class ResumeHandler(
        List<string> events,
        bool postconditionPasses,
        ExecutionResumeBehavior resumeBehavior =
            ExecutionResumeBehavior.RevalidatePostcondition,
        bool postconditionCancelled = false) : IExecutionHandler
    {
        private bool _hasExecuted;

        public string Id => "create-directory";

        public ExecutionResumeBehavior ResumeBehavior { get; } = resumeBehavior;

        public Task<ExecutionHandlerResult> PrepareAsync(
            ExecutionHandlerRequest request,
            CancellationToken cancellationToken) => Success("prepare", ExecutionPhase.Prepare);

        public Task<ExecutionHandlerResult> CheckPreconditionsAsync(
            ExecutionHandlerRequest request,
            CancellationToken cancellationToken) => Success("precondition", ExecutionPhase.Precondition);

        public Task<ExecutionHandlerResult> ExecuteAsync(
            ExecutionHandlerRequest request,
            IProgress<ExecutionProgressLine>? progress,
            CancellationToken cancellationToken)
        {
            _hasExecuted = true;
            return Success("execute", ExecutionPhase.Execute, Fixture.Digest);
        }

        public Task<ExecutionHandlerResult> CheckPostconditionsAsync(
            ExecutionHandlerRequest request,
            CancellationToken cancellationToken)
        {
            if (postconditionCancelled)
            {
                events.Add("handler.postcondition");
                return Task.FromResult(ExecutionHandlerResult.Create(
                    ExecutionPhase.Postcondition,
                    ExecutionHandlerOutcome.Cancelled,
                    null,
                    null,
                    null,
                    []).Value);
            }

            if (postconditionPasses || _hasExecuted)
            {
                return Success("postcondition", ExecutionPhase.Postcondition);
            }

            events.Add("handler.postcondition");
            var error = DevForgeError.Create(
                "DF-EXEC-001",
                "The step postcondition drifted.",
                RedactedText.FromTrustedRedaction("The guarded postcondition no longer passed.").Value,
                "postcondition",
                request.ItemId,
                false,
                [],
                []).Value;
            return Task.FromResult(ExecutionHandlerResult.Create(
                ExecutionPhase.Postcondition,
                ExecutionHandlerOutcome.Failed,
                null,
                null,
                error,
                []).Value);
        }

        public Task<ExecutionHandlerResult> CleanupForRetryAsync(
            ExecutionHandlerRequest request,
            CancellationToken cancellationToken) => Success("cleanup", ExecutionPhase.Prepare);

        private Task<ExecutionHandlerResult> Success(
            string name,
            ExecutionPhase phase,
            string? digest = null)
        {
            events.Add("handler." + name);
            return Task.FromResult(ExecutionHandlerResult.Create(
                phase,
                ExecutionHandlerOutcome.Succeeded,
                null,
                digest,
                null,
                []).Value);
        }
    }

    private sealed class ProgressReportingHandler(List<string> events) : IExecutionHandler
    {
        private readonly RecordingHandler _inner = new(events);

        public string Id => _inner.Id;

        public ExecutionResumeBehavior ResumeBehavior => _inner.ResumeBehavior;

        public Task<ExecutionHandlerResult> PrepareAsync(
            ExecutionHandlerRequest request,
            CancellationToken cancellationToken) => _inner.PrepareAsync(request, cancellationToken);

        public Task<ExecutionHandlerResult> CheckPreconditionsAsync(
            ExecutionHandlerRequest request,
            CancellationToken cancellationToken) => _inner.CheckPreconditionsAsync(request, cancellationToken);

        public Task<ExecutionHandlerResult> ExecuteAsync(
            ExecutionHandlerRequest request,
            IProgress<ExecutionProgressLine>? progress,
            CancellationToken cancellationToken)
        {
            progress?.Report(ExecutionProgressLine.Create(
                request.ItemId,
                RedactedText.FromTrustedRedaction("bounded progress").Value).Value);
            return _inner.ExecuteAsync(request, progress: null, cancellationToken);
        }

        public Task<ExecutionHandlerResult> CheckPostconditionsAsync(
            ExecutionHandlerRequest request,
            CancellationToken cancellationToken) =>
            _inner.CheckPostconditionsAsync(request, cancellationToken);

        public Task<ExecutionHandlerResult> CleanupForRetryAsync(
            ExecutionHandlerRequest request,
            CancellationToken cancellationToken) =>
            _inner.CleanupForRetryAsync(request, cancellationToken);
    }

    private sealed class BlockingHandler : IExecutionHandler
    {
        public TaskCompletionSource Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public string Id => "create-directory";

        public ExecutionResumeBehavior ResumeBehavior =>
            ExecutionResumeBehavior.RevalidatePostcondition;

        public Task<ExecutionHandlerResult> PrepareAsync(
            ExecutionHandlerRequest request,
            CancellationToken cancellationToken) => Success(ExecutionPhase.Prepare);

        public Task<ExecutionHandlerResult> CheckPreconditionsAsync(
            ExecutionHandlerRequest request,
            CancellationToken cancellationToken) => Success(ExecutionPhase.Precondition);

        public async Task<ExecutionHandlerResult> ExecuteAsync(
            ExecutionHandlerRequest request,
            IProgress<ExecutionProgressLine>? progress,
            CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return await Success(ExecutionPhase.Execute, Fixture.Digest);
        }

        public Task<ExecutionHandlerResult> CheckPostconditionsAsync(
            ExecutionHandlerRequest request,
            CancellationToken cancellationToken) => Success(ExecutionPhase.Postcondition);

        public Task<ExecutionHandlerResult> CleanupForRetryAsync(
            ExecutionHandlerRequest request,
            CancellationToken cancellationToken) => Success(ExecutionPhase.Prepare);

        private static Task<ExecutionHandlerResult> Success(
            ExecutionPhase phase,
            string? digest = null) => Task.FromResult(ExecutionHandlerResult.Create(
                phase,
                ExecutionHandlerOutcome.Succeeded,
                null,
                digest,
                null,
                []).Value);
    }

    private sealed class ThrowingProgress : IProgress<ExecutionProgressLine>
    {
        public void Report(ExecutionProgressLine value)
        {
            throw new InvalidOperationException("observer failure");
        }
    }

    private sealed class StubWorkspace(string root) : IWorkspaceFileSystem
    {
        public WorkspaceRoot Root { get; } = WorkspaceRoot.Create(root).Value;

        public Task<bool> FileExistsAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> DirectoryExistsAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task CreateDirectoryAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Stream> OpenReadAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Stream> OpenWriteAsync(
            WorkspaceRelativePath path,
            bool overwrite,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DeleteFileAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateAllFilesAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateRootDirectoriesAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateFilesAsync(
            WorkspaceRelativePath directory,
            bool recursive,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateDirectoriesAsync(
            WorkspaceRelativePath directory,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DeleteDirectoryAsync(
            WorkspaceRelativePath path,
            DirectoryCleanupIntent intent,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task MoveDirectoryAsync(
            WorkspaceRelativePath source,
            WorkspaceRelativePath destination,
            WorkspaceMoveIntent intent,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private static WorkspaceRelativePath Path(string value) =>
        WorkspaceRelativePath.Create(value).Value;

    private static DevForgeError FailureError(string phase) => DevForgeError.Create(
        "DF-EXEC-003",
        "Execution recovery evidence could not be verified.",
        RedactedText.FromTrustedRedaction(
            "The checkpoint, marker, or blueprint fingerprint did not match.").Value,
        phase,
        null,
        true,
        [],
        []).Value;
}
