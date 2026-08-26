using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using DevForge.Application.Contracts;
using DevForge.Application.Execution;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Diagnostics;
using DevForge.Domain.Execution;
using DevForge.Domain.Privacy;
using DevForge.Domain.Projects;
using DevForge.Domain.Reports;
using DevForge.Domain.Runs;
using DevForge.Domain.Validation;

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
                typeof(IProjectEvidenceWriter),
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
    public async Task ResumeRevalidatesPassedEvidenceAndRetainsOriginalTimingsWhenDigestsMatch()
    {
        var validator = new ValidatorHandler(ExecutionPhase.Execute, ExecutionHandlerOutcome.Succeeded);
        var startedAt = new DateTimeOffset(2026, 8, 26, 9, 0, 0, TimeSpan.Zero);
        var validatorEvidence = ExecutionEvidence.Create(
            ExecutionEvidenceKind.Validator,
            "quality-gate",
            ExecutionEvidenceStatus.Passed,
            $"sha256:{new string('4', 64)}",
            startedAt,
            startedAt.AddMilliseconds(125),
            null,
            null).Value;
        var scanEvidence = ExecutionEvidence.Create(
            ExecutionEvidenceKind.SecretScan,
            "secret-scan",
            ExecutionEvidenceStatus.Passed,
            "sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            startedAt.AddSeconds(1),
            startedAt.AddSeconds(1).AddMilliseconds(75),
            null,
            null).Value;
        var fixture = Fixture.Create(
            secretFound: false,
            validator,
            initialEvidence: [validatorEvidence, scanEvidence]);

        var result = await fixture.Coordinator.CompleteAsync(
            fixture.Request,
            fixture.Checkpoint,
            fixture.Staging,
            fixture.Package,
            fixture.Registry,
            progress: null,
            CancellationToken.None);

        Assert.Equal(RunStatus.LocalReady, result.Run.Status);
        Assert.Equal(
            [ExecutionPhase.Prepare, ExecutionPhase.Precondition, ExecutionPhase.Execute, ExecutionPhase.Postcondition],
            validator.Phases);
        Assert.Contains("secret.scan", fixture.Events);
        Assert.Collection(
            result.Evidence,
            item =>
            {
                Assert.Equal(validatorEvidence.Kind, item.Kind);
                Assert.Equal(validatorEvidence.Id, item.Id);
                Assert.Equal(validatorEvidence.Status, item.Status);
                Assert.Equal(validatorEvidence.OutputDigest, item.OutputDigest);
                Assert.Equal(validatorEvidence.StartedAt, item.StartedAt);
                Assert.Equal(validatorEvidence.CompletedAt, item.CompletedAt);
            },
            item =>
            {
                Assert.Equal(scanEvidence.Kind, item.Kind);
                Assert.Equal(scanEvidence.Id, item.Id);
                Assert.Equal(scanEvidence.Status, item.Status);
                Assert.Equal(scanEvidence.OutputDigest, item.OutputDigest);
                Assert.Equal(scanEvidence.StartedAt, item.StartedAt);
                Assert.Equal(scanEvidence.CompletedAt, item.CompletedAt);
            });
        Assert.Equal(
            ["quality-gate", "whole-payload-secret-scan"],
            fixture.EvidenceWriter.LastReport!.Validations.Select(item => item.Id));
    }

    [Fact]
    public async Task ResumeFailsClosedWhenPassedValidatorEvidenceDiverges()
    {
        var validator = new ValidatorHandler(ExecutionPhase.Execute, ExecutionHandlerOutcome.Succeeded);
        var prior = ExecutionEvidence.Create(
            ExecutionEvidenceKind.Validator,
            "quality-gate",
            ExecutionEvidenceStatus.Passed,
            $"sha256:{new string('6', 64)}",
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddMilliseconds(1),
            null,
            null).Value;
        var fixture = Fixture.Create(secretFound: false, validator, initialEvidence: [prior]);

        var result = await fixture.Coordinator.CompleteAsync(
            fixture.Request, fixture.Checkpoint, fixture.Staging, fixture.Package,
            fixture.Registry, progress: null, CancellationToken.None);

        Assert.Equal(RunStatus.ValidationFailed, result.Run.Status);
        Assert.Equal("DF-VALID-002", Assert.Single(result.Run.Errors).Code);
        Assert.Equal(prior, Assert.Single(result.Evidence));
        Assert.DoesNotContain("secret.scan", fixture.Events);
        Assert.DoesNotContain("finalizer.run", fixture.Events);
    }

    [Fact]
    public async Task ResumeRevalidatesOptionalWarningAndRetainsOriginalTimingAndErrorEvidence()
    {
        var handler = new ValidatorHandler(ExecutionPhase.Execute, ExecutionHandlerOutcome.Failed);
        var startedAt = new DateTimeOffset(2026, 8, 26, 9, 0, 0, TimeSpan.Zero);
        var prior = ExecutionEvidence.Create(
            ExecutionEvidenceKind.Validator,
            "quality-gate",
            ExecutionEvidenceStatus.Warning,
            Digest("validator:quality-gate:Failed:DF-VALID-001"),
            startedAt,
            startedAt.AddMilliseconds(225),
            "DF-VALID-001",
            "Validation failed.").Value;
        var fixture = Fixture.Create(
            secretFound: false,
            handler,
            validatorRequired: false,
            initialEvidence: [prior]);

        var result = await fixture.Coordinator.CompleteAsync(
            fixture.Request, fixture.Checkpoint, fixture.Staging, fixture.Package,
            fixture.Registry, progress: null, CancellationToken.None);

        Assert.Equal(RunStatus.LocalReady, result.Run.Status);
        Assert.Equal(
            [ExecutionPhase.Prepare, ExecutionPhase.Precondition, ExecutionPhase.Execute],
            handler.Phases);
        var retained = Assert.Single(result.Evidence, item => item.Kind == ExecutionEvidenceKind.Validator);
        Assert.Equal(prior.Status, retained.Status);
        Assert.Equal(prior.OutputDigest, retained.OutputDigest);
        Assert.Equal(prior.StartedAt, retained.StartedAt);
        Assert.Equal(prior.CompletedAt, retained.CompletedAt);
        Assert.Equal(prior.ErrorCode, retained.ErrorCode);
        Assert.Equal(prior.ErrorSummary?.Value, retained.ErrorSummary?.Value);
        var validation = Assert.Single(
            fixture.EvidenceWriter.LastReport!.Validations,
            item => item.Id == "quality-gate");
        Assert.Equal(ValidationCheckStatus.Warning, validation.Status);
        Assert.Equal(prior.ErrorSummary?.Value, validation.Summary);
    }

    [Theory]
    [InlineData(ExecutionHandlerOutcome.Failed, false)]
    [InlineData(ExecutionHandlerOutcome.Succeeded, true)]
    public async Task ResumeFailsClosedWhenOptionalWarningStatusOrDigestDiverges(
        ExecutionHandlerOutcome resumedOutcome,
        bool retainMatchingDigest)
    {
        var handler = new ValidatorHandler(ExecutionPhase.Execute, resumedOutcome);
        var prior = ExecutionEvidence.Create(
            ExecutionEvidenceKind.Validator,
            "quality-gate",
            ExecutionEvidenceStatus.Warning,
            retainMatchingDigest
                ? $"sha256:{new string('4', 64)}"
                : $"sha256:{new string('6', 64)}",
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddMilliseconds(10),
            "DF-VALID-001",
            "Validation failed.").Value;
        var fixture = Fixture.Create(
            secretFound: false,
            handler,
            validatorRequired: false,
            initialEvidence: [prior]);

        var result = await fixture.Coordinator.CompleteAsync(
            fixture.Request, fixture.Checkpoint, fixture.Staging, fixture.Package,
            fixture.Registry, progress: null, CancellationToken.None);

        Assert.Equal(RunStatus.ValidationFailed, result.Run.Status);
        Assert.Equal("DF-VALID-002", Assert.Single(result.Run.Errors).Code);
        Assert.DoesNotContain("secret.scan", fixture.Events);
        Assert.DoesNotContain("finalizer.run", fixture.Events);
        var retained = Assert.Single(result.Evidence);
        Assert.Equal(prior.OutputDigest, retained.OutputDigest);
        Assert.Equal(prior.StartedAt, retained.StartedAt);
    }

    [Fact]
    public async Task ResumeRerunsSecretScanAndBlocksASecretFindingDespitePriorPass()
    {
        var prior = ExecutionEvidence.Create(
            ExecutionEvidenceKind.SecretScan,
            "secret-scan",
            ExecutionEvidenceStatus.Passed,
            "sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddMilliseconds(1),
            null,
            null).Value;
        var fixture = Fixture.Create(secretFound: true, initialEvidence: [prior]);

        var result = await fixture.Coordinator.CompleteAsync(
            fixture.Request, fixture.Checkpoint, fixture.Staging, fixture.Package,
            fixture.Registry, progress: null, CancellationToken.None);

        Assert.Equal(RunStatus.ValidationFailed, result.Run.Status);
        Assert.Equal("DF-SECRET-001", Assert.Single(result.Run.Errors).Code);
        Assert.Contains("secret.scan", fixture.Events);
        Assert.DoesNotContain("finalizer.run", fixture.Events);
    }

    [Fact]
    public async Task ResumeFailsClosedWhenPassedSecretScanDigestDiverges()
    {
        var prior = ExecutionEvidence.Create(
            ExecutionEvidenceKind.SecretScan,
            "secret-scan",
            ExecutionEvidenceStatus.Passed,
            $"sha256:{new string('6', 64)}",
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddMilliseconds(1),
            null,
            null).Value;
        var fixture = Fixture.Create(secretFound: false, initialEvidence: [prior]);

        var result = await fixture.Coordinator.CompleteAsync(
            fixture.Request, fixture.Checkpoint, fixture.Staging, fixture.Package,
            fixture.Registry, progress: null, CancellationToken.None);

        Assert.Equal(RunStatus.ValidationFailed, result.Run.Status);
        Assert.Equal("DF-SECRET-002", Assert.Single(result.Run.Errors).Code);
        Assert.Equal(prior, Assert.Single(result.Evidence));
        Assert.Contains("secret.scan", fixture.Events);
        Assert.DoesNotContain("finalizer.run", fixture.Events);
    }

    [Fact]
    public async Task ManualRetryDoesNotReuseFailedRequiredValidatorEvidence()
    {
        var validator = new ValidatorHandler(ExecutionPhase.Execute, ExecutionHandlerOutcome.Succeeded);
        var failedEvidence = ExecutionEvidence.Create(
            ExecutionEvidenceKind.Validator,
            "quality-gate",
            ExecutionEvidenceStatus.Failed,
            $"sha256:{new string('4', 64)}",
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddMilliseconds(1),
            "DF-VALID-001",
            "Validation failed.").Value;
        var fixture = Fixture.Create(
            secretFound: false,
            validator,
            initialEvidence: [failedEvidence],
            mode: ExecutionMode.ManualRetry);

        var result = await fixture.Coordinator.CompleteAsync(
            fixture.Request,
            fixture.Checkpoint,
            fixture.Staging,
            fixture.Package,
            fixture.Registry,
            progress: null,
            CancellationToken.None);

        Assert.Equal(RunStatus.LocalReady, result.Run.Status);
        Assert.Equal(
            [ExecutionPhase.Prepare, ExecutionPhase.Precondition, ExecutionPhase.Execute, ExecutionPhase.Postcondition],
            validator.Phases);
        Assert.Equal(
            ExecutionEvidenceStatus.Passed,
            Assert.Single(result.Evidence, item => item.Kind == ExecutionEvidenceKind.Validator).Status);
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

    [Fact]
    public async Task CompletionUsesOnlyReviewedArtifactsWithoutEnumeratingPayload()
    {
        var fixture = Fixture.Create(secretFound: false, rejectPayloadEnumeration: true);

        var result = await fixture.Coordinator.CompleteAsync(
            fixture.Request, fixture.Checkpoint, fixture.Staging, fixture.Package,
            fixture.Registry, progress: null, CancellationToken.None);

        Assert.Equal(RunStatus.LocalReady, result.Run.Status);
        Assert.Equal(0, fixture.Payload.EnumerationCount);
        Assert.Equal(
            "src\\App.csproj",
            Assert.Single(fixture.EvidenceWriter.LastReport!.GeneratedArtifacts));
    }

    [Fact]
    public async Task ResumeUsesPersistedPreviewWhenRequestPreviewHasChangedToolDetectionAndWarnings()
    {
        var fixture = Fixture.Create(secretFound: false, changedRequestPreview: true);

        var result = await fixture.Coordinator.CompleteAsync(
            fixture.Request, fixture.Checkpoint, fixture.Staging, fixture.Package,
            fixture.Registry, progress: null, CancellationToken.None);

        Assert.Equal(RunStatus.LocalReady, result.Run.Status);
        var report = Assert.IsType<GenerationReport>(fixture.EvidenceWriter.LastReport);
        var tool = Assert.Single(report.ToolStatuses);
        Assert.Equal("persisted-tool", tool.Id);
        Assert.Equal("1.0.0", tool.DetectedVersion);
        var warning = Assert.Single(report.Warnings);
        Assert.Equal("persisted.warning", warning.Code);
        Assert.Equal("Persisted warning.", warning.Message.Value);
    }

    [Fact]
    public async Task MissingReviewedArtifactFailsClosedBeforeEvidenceAndFinalization()
    {
        var fixture = Fixture.Create(secretFound: false, missingReviewedArtifact: true);

        var result = await fixture.Coordinator.CompleteAsync(
            fixture.Request, fixture.Checkpoint, fixture.Staging, fixture.Package,
            fixture.Registry, progress: null, CancellationToken.None);

        Assert.Equal(RunStatus.Failed, result.Run.Status);
        Assert.Equal("DF-EVIDENCE-001", Assert.Single(result.Run.Errors).Code);
        Assert.DoesNotContain("evidence.write", fixture.Events);
        Assert.DoesNotContain("finalizer.run", fixture.Events);
    }

    [Fact]
    public async Task DirectoryAtReviewedArtifactPathFailsClosedBeforeEvidenceAndFinalization()
    {
        var fixture = Fixture.Create(secretFound: false, directoryOnlyReviewedArtifact: true);

        var result = await fixture.Coordinator.CompleteAsync(
            fixture.Request, fixture.Checkpoint, fixture.Staging, fixture.Package,
            fixture.Registry, progress: null, CancellationToken.None);

        Assert.Equal(RunStatus.Failed, result.Run.Status);
        Assert.Equal("DF-EVIDENCE-001", Assert.Single(result.Run.Errors).Code);
        Assert.DoesNotContain("evidence.write", fixture.Events);
        Assert.DoesNotContain("finalizer.run", fixture.Events);
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
            EvidenceWriter evidenceWriter,
            StubWorkspace payload,
            List<string> events)
        {
            Request = request;
            Checkpoint = checkpoint;
            Staging = staging;
            Package = package;
            Registry = registry;
            Coordinator = coordinator;
            Store = store;
            EvidenceWriter = evidenceWriter;
            Payload = payload;
            Events = events;
        }

        public ExecutionRequest Request { get; }
        public RunCheckpoint Checkpoint { get; }
        public StagingWorkspace Staging { get; }
        public BlueprintExecutionPackage Package { get; }
        public IExecutionHandlerRegistry Registry { get; }
        public ValidatedRunCompletionCoordinator Coordinator { get; }
        public Store Store { get; }
        public EvidenceWriter EvidenceWriter { get; }
        public StubWorkspace Payload { get; }
        public List<string> Events { get; }

        public static Fixture Create(
            bool secretFound,
            ValidatorHandler? validatorHandler = null,
            bool validatorRequired = true,
            bool reportFails = false,
            bool finalizerFails = false,
            CancellationTokenSource? cancelAfterFinalize = null,
            bool scannerThrows = false,
            IEnumerable<ExecutionEvidence>? initialEvidence = null,
            ExecutionMode mode = ExecutionMode.Resume,
            bool rejectPayloadEnumeration = false,
            bool missingReviewedArtifact = false,
            bool directoryOnlyReviewedArtifact = false,
            bool changedRequestPreview = false)
        {
            var events = new List<string>();
            var target = new StubWorkspace("C:\\target");
            var artifacts = new StubWorkspace("C:\\artifacts");
            var reviewedArtifacts = new[] { new BlueprintArtifact("src\\App.csproj") };
            var payload = new StubWorkspace(
                "C:\\target\\.devforge-staging\\run\\payload",
                missingReviewedArtifact || directoryOnlyReviewedArtifact
                    ? []
                    : [Path("src\\App.csproj")],
                rejectPayloadEnumeration,
                directoryOnlyReviewedArtifact ? [Path("src\\App.csproj")] : []);
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
            var requiredTools = new[]
            {
                new ToolRequirement("persisted-tool", ">=1.0.0", true),
            };
            var persistedPreview = PlanPreview.Create(
                blueprint,
                [],
                validators.Select(item => new PlanPreviewValidator(
                    item.Id,
                    item.Handler,
                    item.Timeout,
                    item.Required)),
                requiredTools,
                [new PlanPreviewToolStatus(
                    "persisted-tool", ">=1.0.0", true, true, true, "1.0.0")],
                [],
                reviewedArtifacts,
                [new ValidationIssue("persisted.warning", "Persisted warning.", "preview")],
                [], [],
                GitOptions.Create().Value,
                CompletionOptions.Create().Value,
                plan.Id).Value;
            var requestPreview = changedRequestPreview
                ? PlanPreview.Create(
                    blueprint,
                    [],
                    validators.Select(item => new PlanPreviewValidator(
                        item.Id, item.Handler, item.Timeout, item.Required)),
                    requiredTools,
                    [new PlanPreviewToolStatus(
                        "persisted-tool", ">=1.0.0", true, true, true, "9.9.9")],
                    [],
                    reviewedArtifacts,
                    [new ValidationIssue("request.warning", "Changed request warning.", "preview")],
                    [], [],
                    GitOptions.Create().Value,
                    CompletionOptions.Create().Value,
                    plan.Id).Value
                : persistedPreview;
            var planned = PlannedProject.Create(plan, requestPreview, fingerprint).Value;
            var run = ProjectRun.Create("run", "recipe").Value
                .TransitionTo(RunStatus.Planning).Value
                .TransitionTo(RunStatus.Executing).Value;
            if (mode == ExecutionMode.ManualRetry)
            {
                var retryableError = DevForgeError.Create(
                    "DF-VALID-001",
                    "Validation failed.",
                    RedactedText.FromTrustedRedaction("Validation failed safely.").Value,
                    "execute",
                    "generation",
                    true,
                    [],
                    []).Value;
                run = run.StartAttempt("generation", DateTimeOffset.UnixEpoch).Value
                    .CompleteAttempt(
                        "generation",
                        1,
                        StepAttemptOutcome.Failed,
                        DateTimeOffset.UnixEpoch.AddSeconds(1),
                        null,
                        retryableError,
                        null).Value;
            }
            var request = ExecutionRequest.Create(
                planned, run, target, Path("project"), artifacts, mode).Value;
            var descriptor = StagingDescriptor.Create(
                Path(".devforge-staging\\run"),
                Path(".devforge-staging\\run\\payload"),
                Path(".devforge-staging\\run\\ownership.json"),
                "run").Value;
            var staging = StagingWorkspace.Create(descriptor, payload).Value;
            var checkpoint = RunCheckpoint.Create(
                run,
                plan,
                persistedPreview,
                blueprint,
                fingerprint,
                descriptor,
                TargetDescriptor.Create(target.Root, Path("project"), null).Value,
                RunArtifactDescriptor.Create(artifacts.Root).Value,
                initialEvidence ?? [],
                FinalizationState.NotStarted,
                ReportPersistenceState.NotStarted).Value;
            var manifest = BlueprintManifest.Create(
                new BlueprintManifestDraft(
                    blueprint.Id, blueprint.Version, ">=1.0.0 <2.0.0", [], [], [], [], [],
                    Artifacts: reviewedArtifacts),
                new BlueprintTrustAssignment(BlueprintTrust.BuiltIn)).Value;
            var resolved = ResolvedBlueprint.Create(manifest, [], fingerprint).Value;
            var package = BlueprintExecutionPackage.Create(
                resolved,
                new StubWorkspace("C:\\blueprint")).Value;
            var store = new Store(events);
            var evidenceWriter = new EvidenceWriter(events);
            var coordinator = new ValidatedRunCompletionCoordinator(
                store,
                new Scanner(events, secretFound, scannerThrows),
                new Finalizer(events, checkpoint.Target, finalizerFails, cancelAfterFinalize),
                evidenceWriter,
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
                evidenceWriter,
                payload,
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

    private sealed class EvidenceWriter(List<string> events) : IProjectEvidenceWriter
    {
        public GenerationReport? LastReport { get; private set; }

        public Task<ExecutionOperationResult<ProjectEvidenceWriteReceipt>> WriteAsync(
            RunCheckpoint checkpoint,
            GenerationReport report,
            IWorkspaceFileSystem payloadWorkspace,
            CancellationToken cancellationToken)
        {
            events.Add("evidence.write");
            LastReport = report;
            return Task.FromResult(ExecutionOperationResult.Success(
                ProjectEvidenceWriteReceipt.Create(
                [
                    Path(@".devforge\project.recipe.yaml"),
                    Path("devforge.lock.json"),
                    Path("generation-report.json"),
                    Path("policy.snapshot.json"),
                ],
                Enumerable.Repeat($"sha256:{new string('1', 64)}", 4)).Value));
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

    private sealed class StubWorkspace(
        string root,
        IEnumerable<WorkspaceRelativePath>? existingFiles = null,
        bool rejectEnumeration = false,
        IEnumerable<WorkspaceRelativePath>? existingDirectories = null) : IWorkspaceFileSystem
    {
        private readonly HashSet<WorkspaceRelativePath> _existingFiles =
            [.. existingFiles ?? []];
        private readonly HashSet<WorkspaceRelativePath> _existingDirectories =
            [.. existingDirectories ?? []];

        public WorkspaceRoot Root { get; } = WorkspaceRoot.Create(root).Value;
        public int EnumerationCount { get; private set; }
        public Task<System.Collections.Immutable.ImmutableArray<WorkspaceRelativePath>> EnumerateAllFilesAsync(
            CancellationToken cancellationToken)
        {
            EnumerationCount++;
            return rejectEnumeration
                ? throw new InvalidOperationException("Payload enumeration is forbidden by the test.")
                : Task.FromResult(_existingFiles.ToImmutableArray());
        }
        public Task<bool> FileExistsAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) =>
            Task.FromResult(_existingFiles.Contains(path));
        public Task<bool> DirectoryExistsAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) =>
            Task.FromResult(_existingDirectories.Contains(path));
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

    private static string Digest(string value) =>
        $"sha256:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))}";
}
