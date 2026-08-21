using System.Collections.Immutable;
using System.IO;
using DevForge.Application.Contracts;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Desktop.Execution;
using DevForge.Domain.Execution;
using DevForge.Domain.Projects;
using DevForge.Domain.Runs;

namespace DevForge.E2ETests.Desktop;

public sealed class LocalReadyViewModelTests
{
    [Fact]
    public async Task LocalReadyNeverClaimsDomainCompletedAndIdeFailureDoesNotMutateCheckpoint()
    {
        var snapshot = CreateLocalReadySnapshot();
        var launcher = new FailingLocalReadyService();
        var sut = new LocalReadyViewModel(snapshot, launcher);
        var checkpoint = snapshot.Checkpoint;

        Assert.False(sut.CanOpenIde);

        Assert.Equal("LOCAL PROJECT READY", sut.StatusLabel);
        Assert.False(sut.IsDomainCompleted);
        Assert.DoesNotContain("COMPLETED", sut.StatusLabel, StringComparison.Ordinal);
        Assert.Same(checkpoint, sut.Snapshot.Checkpoint);
        Assert.Null(sut.IdeErrorMessage);
        Assert.Equal(FinalizationState.Succeeded, sut.FinalizationState);
        Assert.Equal(ReportPersistenceState.Succeeded, sut.ReportState);
        Assert.Equal(snapshot.Checkpoint.PlanHash, sut.PlanHash);
        Assert.Contains("sample.local", sut.BlueprintLabel, StringComparison.Ordinal);
        Assert.Equal("C:\\Projects\\sample", sut.TargetDisplayPath);
        Assert.Equal(
            ["C:\\DevForgeData\\runs\\run-0123456789abcdef0123456789abcdef\\reports\\run-0123456789abcdef0123456789abcdef.json",
             "C:\\DevForgeData\\runs\\run-0123456789abcdef0123456789abcdef\\reports\\run-0123456789abcdef0123456789abcdef.md"],
            sut.ReportReferences.ToArray());
        Assert.Equal("Step | create | Passed", Assert.Single(sut.Evidence).DisplayText);
        Assert.False(sut.CanRetryPublish);
        Assert.False(sut.RetryPublishCommand.CanExecute(null));
        Assert.Null(sut.InitialCommitId);
        Assert.Empty(sut.Branches);
        Assert.Null(sut.RepositoryUrl);
        Assert.Empty(sut.PublicationReceiptReferences);
    }

    [Fact]
    public async Task PublishPendingRetryUsesOnlyRunIdentityAndProjectsCompletedEvidence()
    {
        var plan = ExecutionCenterViewModelTests.CreatePlan(initializeRepository: true);
        var pending = ExecutionCenterViewModelTests.CreatePublishPendingExecution(plan);
        var completed = ExecutionCenterViewModelTests.CreateCompletedExecution(plan);
        var publication = new RecordingPublication(completed.Checkpoint);
        var sut = new LocalReadyViewModel(
            pending,
            new FailingLocalReadyService(),
            publication);

        Assert.Equal("PUBLISH PENDING", sut.StatusLabel);
        Assert.True(sut.CanRetryPublish);
        Assert.Contains("local project is safe", sut.PublicationRemediation, StringComparison.OrdinalIgnoreCase);

        await sut.RetryPublishCommand.ExecuteAsync(null);

        Assert.Equal(plan.RunId, publication.RunId);
        Assert.Equal(PublicationMutationMode.Normal, publication.Mode);
        Assert.Equal("COMPLETED", sut.StatusLabel);
        Assert.True(sut.IsDomainCompleted);
        Assert.False(sut.CanRetryPublish);
        Assert.Equal(new string('a', 40), sut.InitialCommitId);
        Assert.Equal(["main"], sut.Branches.ToArray());
        Assert.Equal([$"reports\\{plan.RunId}.publication.json"], sut.PublicationReceiptReferences.ToArray());
    }

    [Fact]
    public void SafeModeDisablesPublishPendingMutation()
    {
        var plan = ExecutionCenterViewModelTests.CreatePlan(initializeRepository: true);
        var pending = ExecutionCenterViewModelTests.CreatePublishPendingExecution(plan);
        var sut = new LocalReadyViewModel(
            pending,
            new FailingLocalReadyService(),
            new RecordingPublication(ExecutionCenterViewModelTests.CreateCompletedExecution(plan).Checkpoint),
            isReadOnly: true);

        Assert.False(sut.CanRetryPublish);
        Assert.False(sut.RetryPublishCommand.CanExecute(null));
    }

    private static ProjectCreationExecutionSnapshot CreateLocalReadySnapshot()
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
            reference, [new PlanPreviewStep("create", "create-directory", TimeSpan.FromSeconds(30))],
            [], [], [], [], [], [], [], [], git, completion, hash).Value;
        var fingerprint = BlueprintFingerprint.Create(
            "built-in", WorkspaceRelativePath.Create("sample.local\\1.0.0").Value,
            BlueprintTrust.BuiltIn, $"sha256:{new string('2', 64)}").Value;
        var planned = PlannedProject.Create(plan, preview, fingerprint).Value;
        var target = ProjectTargetDescriptor.Create(
            WorkspaceRoot.Create("C:\\Projects").Value,
            WorkspaceRelativePath.Create("sample").Value).Value;
        var planSnapshot = ProjectCreationPlanSnapshot.Create(
            draft, target, recipe, planned,
            "run-0123456789abcdef0123456789abcdef",
            "recipe-0123456789abcdef0123456789abcdef",
            new DateTimeOffset(2026, 8, 13, 9, 0, 0, TimeSpan.Zero)).Value;
        var run = ProjectRun.Create(planSnapshot.RunId, planSnapshot.RecipeId).Value
            .TransitionTo(RunStatus.Planning).Value
            .TransitionTo(RunStatus.Executing).Value
            .TransitionTo(RunStatus.LocalReady).Value;
        var checkpoint = RunCheckpoint.Create(
            run, plan, preview, reference, fingerprint,
            StagingDescriptor.Create(
                WorkspaceRelativePath.Create($".devforge-staging\\{planSnapshot.RunId}").Value,
                WorkspaceRelativePath.Create($".devforge-staging\\{planSnapshot.RunId}\\payload").Value,
                WorkspaceRelativePath.Create($".devforge-staging\\{planSnapshot.RunId}\\ownership.json").Value,
                "marker-1").Value,
            TargetDescriptor.Create(
                target.ParentRoot, target.TargetDirectory,
                WorkspaceRelativePath.Create($".devforge-finalize-{planSnapshot.RunId}").Value).Value,
            RunArtifactDescriptor.Create(WorkspaceRoot.Create("C:\\DevForgeData\\runs\\run-0123456789abcdef0123456789abcdef").Value).Value,
            [ExecutionEvidence.Create(
                ExecutionEvidenceKind.Step,
                "create",
                ExecutionEvidenceStatus.Passed,
                $"sha256:{new string('3', 64)}").Value],
            FinalizationState.Succeeded,
            ReportPersistenceState.Succeeded).Value;
        return ProjectCreationExecutionSnapshot.Create(planSnapshot, checkpoint).Value;
    }

    private sealed class FailingLocalReadyService : ILocalReadyService
    {
        public LocalReadyPresentation Describe(RunCheckpoint checkpoint) =>
            LocalReadyPresentation.Create(
                "C:\\Projects\\sample",
                [
                    "C:\\DevForgeData\\runs\\run-0123456789abcdef0123456789abcdef\\reports\\run-0123456789abcdef0123456789abcdef.json",
                    "C:\\DevForgeData\\runs\\run-0123456789abcdef0123456789abcdef\\reports\\run-0123456789abcdef0123456789abcdef.md",
                ]).Value;

        public Task OpenIdeAsync(RunCheckpoint checkpoint, string ideId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Failure must remain UI-only.");
    }

    private sealed class RecordingPublication(RunCheckpoint completed)
        : IProjectPublicationWorkflow
    {
        public string? RunId { get; private set; }

        public PublicationMutationMode? Mode { get; private set; }

        public Task<ExecutionOperationResult<ProjectPublicationOutcome>> CompleteAsync(
            string runId,
            PublicationMutationMode mutationMode,
            CancellationToken cancellationToken)
        {
            RunId = runId;
            Mode = mutationMode;
            return Task.FromResult(ExecutionOperationResult.Success(
                ProjectPublicationOutcome.Create(completed, error: null).Value));
        }
    }
}
