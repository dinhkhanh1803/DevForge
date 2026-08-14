using System.Collections.Immutable;
using DevForge.Application.Contracts;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Desktop.Execution;
using DevForge.Domain.Execution;
using DevForge.Domain.Privacy;
using DevForge.Domain.Projects;
using DevForge.Domain.Validation;

namespace DevForge.E2ETests.Desktop;

public sealed class ExecutionSessionCoordinatorTests
{
    [Fact]
    public async Task SafeReadOnlyModeRefusesExecutionAndRecovery()
    {
        var sut = new ExecutionSessionCoordinator(new ProgressWorkflow(), new UnusedRecovery());
        sut.EnterReadOnlyMode();

        Assert.True(sut.IsReadOnly);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ExecuteAsync(CreatePlan(), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.RecoverInterruptedAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RefusesSecondSessionAndCancellationReachesWorkflow()
    {
        var workflow = new BlockingWorkflow();
        var sut = new ExecutionSessionCoordinator(workflow, new UnusedRecovery());
        var first = sut.ExecuteAsync(CreatePlan(), CancellationToken.None);
        await workflow.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ExecuteAsync(CreatePlan(), CancellationToken.None));
        Assert.True(sut.Cancel());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.False(sut.IsActive);
        Assert.False(sut.Cancel());
    }

    [Fact]
    public async Task ProgressIsBoundedRedactedAndObserverFailuresAreIsolated()
    {
        var workflow = new ProgressWorkflow();
        var sut = new ExecutionSessionCoordinator(workflow, new UnusedRecovery());
        var observerCalls = 0;
        sut.ProgressChanged += (_, _) => throw new InvalidOperationException("observer failure");
        sut.ProgressChanged += (_, _) => observerCalls++;

        var result = await sut.ExecuteAsync(CreatePlan(), CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.InRange(sut.ProgressLines.Length, 1, 500);
        Assert.InRange(sut.ProgressLines.Sum(item => item.Text.Length), 1, 65_536);
        Assert.All(sut.ProgressLines, item => Assert.DoesNotContain("token=", item.Text));
        Assert.True(observerCalls > 0);
    }

    [Fact]
    public async Task StartupRecoveryDelegatesOnceThroughTheSameActivityGate()
    {
        var recovery = new RecordingRecovery();
        var sut = new ExecutionSessionCoordinator(new ProgressWorkflow(), recovery);

        var result = await sut.RecoverInterruptedAsync(CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.Empty(result.Value.Checkpoints);
        Assert.Equal(1, recovery.RecoverCount);
        Assert.False(sut.IsActive);
    }

    private static ProjectCreationPlanSnapshot CreatePlan()
    {
        var reference = BlueprintReference.Create("sample.local", "1.0.0").Value;
        var draft = ProjectCreationDraft.Create(
            "Sample", "C:\\Projects", "sample", reference, [], [], "none",
            initializeRepository: false).Value;
        var git = GitOptions.Create(initializeRepository: false).Value;
        var completion = CompletionOptions.Create().Value;
        var recipe = ProjectRecipe.Create(new ProjectRecipeDraft(
            "Sample", "C:\\Projects\\sample", "sample.local", "1.0.0",
            new Dictionary<string, string?>(), [], null, git, completion)).Value;
        var step = ExecutionStep.Create(
            "create", "Create", "create-directory", [], TimeSpan.FromSeconds(30), RetryPolicy.None).Value;
        var hash = $"sha256:{new string('1', 64)}";
        var plan = ExecutionPlan.Create(hash, [step], []).Value;
        var preview = PlanPreview.Create(
            reference,
            [new PlanPreviewStep("create", "create-directory", TimeSpan.FromSeconds(30))],
            [], [], [], [], [], [], [], [],
            git, completion, hash).Value;
        var fingerprint = BlueprintFingerprint.Create(
            "built-in", WorkspaceRelativePath.Create("sample.local\\1.0.0").Value,
            BlueprintTrust.BuiltIn, $"sha256:{new string('2', 64)}").Value;
        var planned = PlannedProject.Create(plan, preview, fingerprint).Value;
        var target = ProjectTargetDescriptor.Create(
            WorkspaceRoot.Create("C:\\Projects").Value,
            WorkspaceRelativePath.Create("sample").Value).Value;
        return ProjectCreationPlanSnapshot.Create(
            draft, target, recipe, planned,
            "run-0123456789abcdef0123456789abcdef",
            "recipe-0123456789abcdef0123456789abcdef",
            new DateTimeOffset(2026, 8, 13, 9, 0, 0, TimeSpan.Zero)).Value;
    }

    private sealed class BlockingWorkflow : IProjectCreationWorkflow
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<BlueprintCatalogSnapshot> LoadCatalogAsync(bool forceRefresh, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ValidationResult<ProjectCreationPlanSnapshot>> CreatePlanAsync(ProjectCreationDraft draft, CancellationToken cancellationToken) => throw new NotSupportedException();

        public async Task<ValidationResult<ProjectCreationExecutionSnapshot>> ExecuteAsync(
            ProjectCreationPlanSnapshot plan,
            IProgress<ExecutionProgressLine>? progress,
            CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }
    }

    private sealed class ProgressWorkflow : IProjectCreationWorkflow
    {
        public Task<BlueprintCatalogSnapshot> LoadCatalogAsync(bool forceRefresh, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ValidationResult<ProjectCreationPlanSnapshot>> CreatePlanAsync(ProjectCreationDraft draft, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ValidationResult<ProjectCreationExecutionSnapshot>> ExecuteAsync(
            ProjectCreationPlanSnapshot plan,
            IProgress<ExecutionProgressLine>? progress,
            CancellationToken cancellationToken)
        {
            for (var index = 0; index < 2_000; index++)
            {
                var text = RedactedText.FromTrustedRedaction($"safe progress line {index:D4} {new string('x', 60)}").Value;
                progress?.Report(ExecutionProgressLine.Create("create", text).Value);
            }

            return Task.FromResult(ValidationResult.Failure<ProjectCreationExecutionSnapshot>(
            [
                new ValidationIssue("test.execution.finished", "Test execution finished.", "execution"),
            ]));
        }
    }

    private sealed class UnusedRecovery : IRunRecoveryService
    {
        public Task<ExecutionOperationResult<RunRecoveryBatch>> RecoverInterruptedAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ExecutionOperationResult<RunCheckpoint>> NormalizeInterruptedAsync(RunCheckpoint checkpoint, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ExecutionOperationResult<RunCheckpoint>> ResumeAsync(ExecutionRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ExecutionOperationResult<StagingCleanupReceipt>> CleanupAsync(RunCheckpoint checkpoint, IWorkspaceFileSystem targetParentWorkspace, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingRecovery : IRunRecoveryService
    {
        public int RecoverCount { get; private set; }

        public Task<ExecutionOperationResult<RunRecoveryBatch>> RecoverInterruptedAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RecoverCount++;
            return Task.FromResult(ExecutionOperationResult.Success(
                RunRecoveryBatch.Create([]).Value));
        }

        public Task<ExecutionOperationResult<RunCheckpoint>> NormalizeInterruptedAsync(RunCheckpoint checkpoint, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ExecutionOperationResult<RunCheckpoint>> ResumeAsync(ExecutionRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ExecutionOperationResult<StagingCleanupReceipt>> CleanupAsync(RunCheckpoint checkpoint, IWorkspaceFileSystem targetParentWorkspace, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
