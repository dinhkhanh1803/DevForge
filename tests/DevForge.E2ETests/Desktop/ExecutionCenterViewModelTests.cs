using DevForge.Application.Contracts;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Desktop.Diagnostics;
using DevForge.Desktop.EnvironmentDoctor;
using DevForge.Desktop.Execution;
using DevForge.Desktop.Notifications;
using DevForge.Domain.Diagnostics;
using DevForge.Domain.Execution;
using DevForge.Domain.Privacy;
using DevForge.Domain.Projects;
using DevForge.Domain.Runs;
using DevForge.Domain.Validation;

namespace DevForge.E2ETests.Desktop;

public sealed class ExecutionCenterViewModelTests
{
    [Fact]
    public async Task AuthoritativeCheckpointEnablesSingleFlightSupportExportAndCopy()
    {
        var plan = CreatePlan();
        var execution = CreateExecution(plan);
        var receipt = SupportBundleReceipt.Create(
            "bundle-001",
            WorkspaceRelativePath.Create("support-bundles\\bundle-001.zip").Value,
            new string('a', 64),
            123,
            DateTimeOffset.UnixEpoch).Value;
        var clipboard = new RecordingClipboard();
        var diagnostics = new DesktopDiagnosticsCoordinator(
            new SuccessfulBundleCoordinator(receipt),
            new UnusedBundleCleanup(),
            clipboard,
            new NotificationService());
        var sut = new ExecutionCenterViewModel(
            new ExecutionSessionCoordinator(
                new SequencedWorkflow(execution),
                new UnsupportedRecovery()),
            diagnostics);

        Assert.False(sut.CreateSupportBundleCommand.CanExecute(null));
        sut.ApplyRecovered(plan.PlannedProject, execution.Checkpoint);
        Assert.True(sut.CreateSupportBundleCommand.CanExecute(null));

        await sut.CreateSupportBundleCommand.ExecuteAsync(null);

        Assert.Equal(receipt, sut.LastSupportBundleReceipt);
        Assert.True(sut.CopySupportBundleReceiptCommand.CanExecute(null));
        sut.CopySupportBundleReceiptCommand.Execute(null);
        Assert.Contains(receipt.RelativePath.Value, clipboard.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void SafeModeDisablesEveryMutatingRecoveryCommand()
    {
        var plan = CreatePlan();
        var execution = CreateExecution(plan);
        var sut = new ExecutionCenterViewModel(
            new ExecutionSessionCoordinator(
                new SequencedWorkflow(execution),
                new UnsupportedRecovery()));
        sut.ApplyRecovered(
            plan.PlannedProject,
            execution.Checkpoint,
            new ProjectRecoveryEligibility(true, true, true));

        sut.EnterReadOnlyMode();

        Assert.False(sut.ResumeCommand.CanExecute(null));
        Assert.False(sut.RetryCommand.CanExecute(null));
        Assert.False(sut.CleanupCommand.CanExecute(null));
    }

    [Fact]
    public async Task FailedNewExecutionCannotReusePreviousSuccessfulSnapshot()
    {
        var plan = CreatePlan();
        var workflow = new SequencedWorkflow(CreateExecution(plan));
        var sut = new ExecutionCenterViewModel(
            new ExecutionSessionCoordinator(workflow, new UnsupportedRecovery()));

        await sut.ExecuteAsync(plan, CancellationToken.None);
        Assert.NotNull(sut.Snapshot);

        await sut.ExecuteAsync(plan, CancellationToken.None);

        Assert.Null(sut.Snapshot);
        Assert.Contains(sut.ValidationIssues, issue => issue.Code == "test.execution.failed");
    }

    [Theory]
    [InlineData(RunStatus.Draft, "DRAFT")]
    [InlineData(RunStatus.Executing, "EXECUTING")]
    [InlineData(RunStatus.ValidationFailed, "VALIDATION FAILED")]
    [InlineData(RunStatus.LocalReady, "LOCAL PROJECT READY")]
    [InlineData(RunStatus.Cancelled, "CANCELLED")]
    [InlineData(RunStatus.Failed, "FAILED")]
    public void StatusProjectionHasExactTextAndIcon(RunStatus status, string expected)
    {
        var projection = ExecutionCenterViewModel.ProjectStatus(status);

        Assert.Equal(expected, projection.Label);
        Assert.False(string.IsNullOrWhiteSpace(projection.Glyph));
    }

    [Fact]
    public void StepProjectionUsesAttemptEvidenceAndNeverEnablesM10Actions()
    {
        var error = DevForgeError.Create(
            "DF-EXEC-001",
            "Execution failed.",
            RedactedText.FromTrustedRedaction("Safe remediation.").Value,
            "Execute",
            "create",
            isRetryable: false,
            ["Review the generated input."],
            []).Value;
        var failed = StepAttempt.Rehydrate(
            "create",
            1,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddSeconds(2),
            StepAttemptOutcome.Failed,
            exitCode: 1,
            error,
            outputDigest: $"sha256:{new string('1', 64)}").Value;

        var item = ExecutionStepViewModel.From("create", "Create files", failed);

        Assert.Equal("FAILED", item.StatusLabel);
        Assert.Equal("DF-EXEC-001", item.ErrorCode);
        Assert.Equal(1, item.AttemptNumber);
        Assert.Equal(TimeSpan.FromSeconds(2), item.Duration);
        Assert.False(item.CanOpenStaging);
        Assert.False(item.CanCreateSupportBundle);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, true)]
    public void RecoveryEligibilityDrivesExactExecutionCenterActions(
        bool canResume,
        bool canRetry,
        bool canCleanup)
    {
        var plan = CreatePlan();
        var execution = CreateExecution(plan);
        var sut = new ExecutionCenterViewModel(
            new ExecutionSessionCoordinator(new SequencedWorkflow(execution), new UnsupportedRecovery()));

        sut.ApplyRecovered(
            plan.PlannedProject,
            execution.Checkpoint,
            new ProjectRecoveryEligibility(canResume, canRetry, canCleanup));

        Assert.Equal(canResume, sut.CanResume);
        Assert.Equal(canRetry, sut.CanRetry);
        Assert.Equal(canCleanup, sut.CanCleanup);
        Assert.Equal(canResume, sut.ResumeCommand.CanExecute(null));
        Assert.Equal(canRetry, sut.RetryCommand.CanExecute(null));
        Assert.Equal(canCleanup, sut.CleanupCommand.CanExecute(null));
        Assert.False(sut.CanOpenStaging);
        Assert.False(sut.CanCreateSupportBundle);
    }

    [Fact]
    public async Task CleanupDelegatesOnlyTheRunIdentityToApplicationWorkflow()
    {
        var plan = CreatePlan();
        var execution = CreateExecution(plan);
        var recovery = new RecordingProjectRecovery(
            new ProjectRecoverySnapshot(plan.PlannedProject, execution.Checkpoint));
        var sut = new ExecutionCenterViewModel(new ExecutionSessionCoordinator(
            new SequencedWorkflow(execution),
            new UnsupportedRecovery(),
            recovery));
        sut.ApplyRecovered(
            plan.PlannedProject,
            execution.Checkpoint,
            new ProjectRecoveryEligibility(false, false, true));

        await sut.CleanupCommand.ExecuteAsync(null);

        Assert.Equal(plan.RunId, recovery.CleanupRunId);
        Assert.False(sut.CanCleanup);
    }

    internal static ProjectCreationPlanSnapshot CreatePlan(bool initializeRepository = false)
    {
        var reference = BlueprintReference.Create("sample.local", "1.0.0").Value;
        var git = GitOptions.Create(initializeRepository).Value;
        var draft = ProjectCreationDraft.Create(
            "Sample", "C:\\Projects", "sample", reference, [], [], "none",
            initializeRepository).Value;
        var recipe = ProjectRecipe.Create(new ProjectRecipeDraft(
            "Sample", "C:\\Projects\\sample", "sample.local", "1.0.0",
            new Dictionary<string, string?>(), [], null,
            git,
            CompletionOptions.Create().Value)).Value;
        var step = ExecutionStep.Create(
            "create", "Create", "create-directory", [], TimeSpan.FromSeconds(30), RetryPolicy.None).Value;
        var hash = $"sha256:{new string('1', 64)}";
        var executionPlan = ExecutionPlan.Create(hash, [step], []).Value;
        var preview = PlanPreview.Create(
            reference, [new PlanPreviewStep("create", "create-directory", TimeSpan.FromSeconds(30))],
            [], [], [], [], [], [], [], [],
            git,
            CompletionOptions.Create().Value,
            hash).Value;
        var fingerprint = BlueprintFingerprint.Create(
            "local", WorkspaceRelativePath.Create("sample.local\\1.0.0").Value,
            BlueprintTrust.TrustedLocal, $"sha256:{new string('2', 64)}").Value;
        var planned = PlannedProject.Create(executionPlan, preview, fingerprint).Value;
        var target = ProjectTargetDescriptor.Create(
            WorkspaceRoot.Create("C:\\Projects").Value,
            WorkspaceRelativePath.Create("sample").Value).Value;
        var result = ProjectCreationPlanSnapshot.Create(
            draft, target, recipe, planned,
            $"run-{new string('1', 32)}",
            $"recipe-{new string('2', 32)}",
            DateTimeOffset.UnixEpoch.AddSeconds(1));
        Assert.True(result.IsValid, string.Join("; ", result.Issues.Select(issue => issue.Code)));
        return result.Value;
    }

    internal static ProjectCreationExecutionSnapshot CreateExecution(ProjectCreationPlanSnapshot plan)
    {
        var run = ProjectRun.Create(plan.RunId, plan.RecipeId).Value
            .TransitionTo(RunStatus.Planning).Value
            .TransitionTo(RunStatus.Executing).Value
            .TransitionTo(RunStatus.LocalReady).Value;
        var publication = PublicationSnapshot.CreateNotRequested(
            $"sha256:{new string('4', 64)}").Value;
        var checkpoint = RunCheckpoint.Create(
            run, plan.PlannedProject.Plan, plan.PlannedProject.Preview,
            plan.PlannedProject.Preview.Blueprint, plan.PlannedProject.BlueprintFingerprint,
            StagingDescriptor.Create(
                WorkspaceRelativePath.Create($".devforge-staging\\{plan.RunId}").Value,
                WorkspaceRelativePath.Create($".devforge-staging\\{plan.RunId}\\payload").Value,
                WorkspaceRelativePath.Create($".devforge-staging\\{plan.RunId}\\ownership.json").Value,
                $"marker-{plan.RunId}").Value,
            TargetDescriptor.Create(
                WorkspaceRoot.Create("C:\\Projects").Value,
                WorkspaceRelativePath.Create("sample").Value, null).Value,
            RunArtifactDescriptor.Create(WorkspaceRoot.Create($"C:\\Artifacts\\{plan.RunId}").Value).Value,
            [], FinalizationState.Succeeded, ReportPersistenceState.Succeeded, publication).Value;
        return ProjectCreationExecutionSnapshot.Create(plan, checkpoint).Value;
    }

    internal static ProjectCreationExecutionSnapshot CreateCompletedExecution(
        ProjectCreationPlanSnapshot plan)
    {
        var localReady = CreateExecution(plan);
        var run = localReady.Checkpoint.Run
            .TransitionTo(RunStatus.PublishPending).Value
            .TransitionTo(RunStatus.Completed).Value;
        var publication = PublicationSnapshot.Create(
            GitPublicationState.Succeeded,
            GitHubPublicationState.NotRequested,
            PublicationReceiptState.Succeeded,
            localReady.Checkpoint.Publication.FinalTreeDigest,
            new string('a', 40),
            ["main"],
            repositoryIdentity: null,
            isPrivate: true,
            ownershipNonce: null,
            repositoryUrl: null,
            WorkspaceRelativePath.Create($"reports\\{plan.RunId}.publication.json").Value,
            $"sha256:{new string('b', 64)}").Value;
        var checkpoint = RunCheckpoint.Create(
            run,
            localReady.Checkpoint.Plan,
            localReady.Checkpoint.Preview,
            localReady.Checkpoint.Blueprint,
            localReady.Checkpoint.BlueprintFingerprint,
            localReady.Checkpoint.Staging,
            localReady.Checkpoint.Target,
            localReady.Checkpoint.RunArtifacts,
            localReady.Checkpoint.Evidence,
            localReady.Checkpoint.FinalizationState,
            localReady.Checkpoint.ReportState,
            publication).Value;
        return ProjectCreationExecutionSnapshot.Create(plan, checkpoint).Value;
    }

    internal static ProjectCreationExecutionSnapshot CreatePublishPendingExecution(
        ProjectCreationPlanSnapshot plan)
    {
        var localReady = CreateExecution(plan);
        var run = localReady.Checkpoint.Run.TransitionTo(RunStatus.PublishPending).Value;
        var publication = PublicationSnapshot.Create(
            GitPublicationState.Failed,
            GitHubPublicationState.NotRequested,
            PublicationReceiptState.NotRequested,
            localReady.Checkpoint.Publication.FinalTreeDigest,
            initialCommitId: null,
            branches: [],
            repositoryIdentity: null,
            isPrivate: true,
            ownershipNonce: null,
            repositoryUrl: null,
            receiptPath: null,
            receiptBodyDigest: null).Value;
        var checkpoint = RunCheckpoint.Create(
            run,
            localReady.Checkpoint.Plan,
            localReady.Checkpoint.Preview,
            localReady.Checkpoint.Blueprint,
            localReady.Checkpoint.BlueprintFingerprint,
            localReady.Checkpoint.Staging,
            localReady.Checkpoint.Target,
            localReady.Checkpoint.RunArtifacts,
            localReady.Checkpoint.Evidence,
            localReady.Checkpoint.FinalizationState,
            localReady.Checkpoint.ReportState,
            publication).Value;
        return ProjectCreationExecutionSnapshot.Create(plan, checkpoint).Value;
    }

    private sealed class SequencedWorkflow(ProjectCreationExecutionSnapshot success) : IProjectCreationWorkflow
    {
        private int _count;
        public Task<BlueprintCatalogSnapshot> LoadCatalogAsync(bool forceRefresh, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ValidationResult<ProjectCreationPlanSnapshot>> CreatePlanAsync(ProjectCreationDraft draft, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ValidationResult<ProjectCreationExecutionSnapshot>> ExecuteAsync(ProjectCreationPlanSnapshot plan, IProgress<ExecutionProgressLine>? progress, CancellationToken cancellationToken) =>
            Task.FromResult(Interlocked.Increment(ref _count) == 1
                ? ValidationResult.Success(success)
                : ValidationResult.Failure<ProjectCreationExecutionSnapshot>(
                    [new ValidationIssue("test.execution.failed", "Execution failed.", "execution")]));
    }

    private sealed class UnsupportedRecovery : IRunRecoveryService
    {
        public Task<ExecutionOperationResult<RunRecoveryBatch>> RecoverInterruptedAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ExecutionOperationResult<RunCheckpoint>> NormalizeInterruptedAsync(RunCheckpoint checkpoint, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ExecutionOperationResult<RunCheckpoint>> ResumeAsync(ExecutionRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ExecutionOperationResult<StagingCleanupReceipt>> CleanupAsync(RunCheckpoint checkpoint, IWorkspaceFileSystem targetParentWorkspace, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingProjectRecovery(ProjectRecoverySnapshot snapshot)
        : IProjectRecoveryWorkflow
    {
        public string? CleanupRunId { get; private set; }

        public Task<ProjectRecoveryEligibility> InspectAsync(
            RunCheckpoint checkpoint,
            CancellationToken cancellationToken) =>
            Task.FromResult(ProjectRecoveryEligibility.None);

        public Task<ProjectRecoverySnapshot> ContinueAsync(
            string runId,
            ExecutionMode mode,
            CancellationToken cancellationToken) =>
            Task.FromResult(snapshot);

        public Task CleanupAsync(string runId, CancellationToken cancellationToken)
        {
            CleanupRunId = runId;
            return Task.CompletedTask;
        }
    }

    private sealed class SuccessfulBundleCoordinator(SupportBundleReceipt receipt)
        : ISupportBundleCoordinator
    {
        public Task<ExecutionOperationResult<SupportBundleReceipt>> ExportAsync(
            SupportBundleRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(ExecutionOperationResult.Success(receipt));
    }

    private sealed class UnusedBundleCleanup : ISupportBundleCleanupService
    {
        public Task<ExecutionOperationResult<SupportBundleCleanupReceipt>> CleanupAsync(
            SupportBundleReceipt receipt,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingClipboard : IClipboardService
    {
        public string Text { get; private set; } = string.Empty;

        public void SetText(string text) => Text = text;
    }
}
