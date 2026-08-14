using DevForge.Application.Contracts;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Desktop.Execution;
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

    private static ProjectCreationPlanSnapshot CreatePlan()
    {
        var reference = BlueprintReference.Create("sample.local", "1.0.0").Value;
        var draft = ProjectCreationDraft.Create(
            "Sample", "C:\\Projects", "sample", reference, [], [], "none").Value;
        var recipe = ProjectRecipe.Create(new ProjectRecipeDraft(
            "Sample", "C:\\Projects\\sample", "sample.local", "1.0.0",
            new Dictionary<string, string?>(), [], null,
            GitOptions.Create(initializeRepository: false).Value,
            CompletionOptions.Create().Value)).Value;
        var step = ExecutionStep.Create(
            "create", "Create", "create-directory", [], TimeSpan.FromSeconds(30), RetryPolicy.None).Value;
        var hash = $"sha256:{new string('1', 64)}";
        var executionPlan = ExecutionPlan.Create(hash, [step], []).Value;
        var preview = PlanPreview.Create(
            reference, [new PlanPreviewStep("create", "create-directory", TimeSpan.FromSeconds(30))],
            [], [], [], [], [], [], [], [],
            GitOptions.Create(initializeRepository: false).Value,
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

    private static ProjectCreationExecutionSnapshot CreateExecution(ProjectCreationPlanSnapshot plan)
    {
        var run = ProjectRun.Create(plan.RunId, plan.RecipeId).Value
            .TransitionTo(RunStatus.Planning).Value
            .TransitionTo(RunStatus.Executing).Value
            .TransitionTo(RunStatus.LocalReady).Value;
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
            [], FinalizationState.Succeeded, ReportPersistenceState.Succeeded).Value;
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
}
