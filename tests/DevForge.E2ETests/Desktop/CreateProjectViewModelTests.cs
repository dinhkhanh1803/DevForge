using System.Collections.Immutable;
using DevForge.Application.Contracts;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Desktop.CreateProject;
using DevForge.Desktop.Execution;
using DevForge.Desktop.Navigation;
using DevForge.Domain.Projects;
using DevForge.Domain.Validation;

namespace DevForge.E2ETests.Desktop;

public sealed class CreateProjectViewModelTests
{
    [Fact]
    public async Task SafeReadOnlyModePreventsTargetPreflightAndPlanning()
    {
        var workflow = new StubWorkflow(CreateBlueprint());
        var sut = CreateViewModel(workflow);
        sut.Name = "Sample";
        sut.RootPath = "C:\\Projects";
        sut.OutputFolder = "sample";
        await sut.LoadAsync(CancellationToken.None);

        sut.EnterReadOnlyMode();
        await sut.ReviewPlanAsync(CancellationToken.None);

        Assert.True(sut.IsReadOnly);
        Assert.False(sut.ReviewPlanCommand.CanExecute(null));
        Assert.Null(workflow.Draft);
    }

    [Fact]
    public void DynamicEditorsApplyTypedDefaultsAndValidateBounds()
    {
        var text = new DynamicInputViewModel(Definition(
            "name", BlueprintInputKind.Text, required: true,
            BlueprintValue.FromString("sample").Value, [], 2, 10, null, null));
        var choice = new DynamicInputViewModel(Definition(
            "style", BlueprintInputKind.Choice, required: true,
            BlueprintValue.FromString("modern").Value, ["modern", "classic"], null, null, null, null));
        var boolean = new DynamicInputViewModel(Definition(
            "tests", BlueprintInputKind.Boolean, required: false,
            BlueprintValue.FromBoolean(true), [], null, null, null, null));
        var number = new DynamicInputViewModel(Definition(
            "projects", BlueprintInputKind.WholeNumber, required: true,
            BlueprintValue.FromInteger(3), [], null, null, 1, 5));
        var changeCount = 0;
        text.ValueChanged += (_, _) => changeCount++;

        Assert.Equal("sample", text.TextValue);
        Assert.Equal("modern", choice.TextValue);
        Assert.True(boolean.BooleanValue);
        Assert.Equal(3, number.WholeNumberValue);
        Assert.True(text.BuildValue().IsValid);
        Assert.True(choice.BuildValue().IsValid);
        Assert.True(boolean.BuildValue().IsValid);
        Assert.True(number.BuildValue().IsValid);

        text.TextValue = "x";
        choice.TextValue = "unsupported";
        number.WholeNumberValue = 6;
        Assert.False(text.BuildValue().IsValid);
        Assert.False(choice.BuildValue().IsValid);
        Assert.False(number.BuildValue().IsValid);
        Assert.Equal(1, changeCount);
    }

    [Fact]
    public async Task ConfigureLoadsCatalogAndSubmitsGuardedDraft()
    {
        var workflow = new StubWorkflow(CreateBlueprint());
        var sut = CreateViewModel(workflow);
        sut.Name = "Sample";
        sut.RootPath = "C:\\Projects";
        sut.OutputFolder = "sample";
        sut.IdeId = "none";

        await sut.LoadAsync(CancellationToken.None);
        await sut.ReviewPlanAsync(CancellationToken.None);

        Assert.Equal(4, sut.Inputs.Count);
        Assert.NotNull(workflow.Draft);
        Assert.Equal("sample", workflow.Draft.OutputFolder);
        Assert.Equal(4, workflow.Draft.Inputs.Count);
        Assert.True(workflow.Draft.Git.InitializeRepository);
        Assert.Equal(GitBranchPolicy.Main, workflow.Draft.Git.BranchPolicy);
        Assert.False(workflow.Draft.Git.PublishToGitHub);
        Assert.True(workflow.Draft.Git.IsPrivate);
        Assert.Contains(sut.ValidationIssues, issue => issue.Code == "test.plan.unavailable");
        Assert.Equal(ProjectCreationStage.Configure, sut.Stage);
    }

    [Fact]
    public async Task InvalidConfigureFormNeverCallsPlanningWorkflow()
    {
        var workflow = new StubWorkflow(CreateBlueprint());
        var sut = CreateViewModel(workflow);
        await sut.LoadAsync(CancellationToken.None);

        await sut.ReviewPlanAsync(CancellationToken.None);

        Assert.Null(workflow.Draft);
        Assert.NotEmpty(sut.ValidationIssues);
        Assert.Equal(ProjectCreationStage.Configure, sut.Stage);
    }

    [Fact]
    public void DisablingGitOrPublishRestoresSafeClosedDefaults()
    {
        var sut = CreateViewModel(new StubWorkflow(CreateBlueprint()));
        sut.GitHubAccount = "reviewed-owner";
        sut.GitHubRepository = "reviewed-repository";
        sut.PublishToGitHub = true;
        sut.IsPrivate = false;

        sut.PublishToGitHub = false;

        Assert.True(sut.IsPrivate);
        Assert.Null(sut.GitHubAccount);
        Assert.Null(sut.GitHubRepository);

        sut.PublishToGitHub = true;
        sut.InitializeRepository = false;

        Assert.False(sut.PublishToGitHub);
        Assert.Equal(GitBranchPolicy.Main, sut.BranchPolicy);
        Assert.False(sut.CanConfigureGit);
        Assert.False(sut.CanConfigureGitHub);
    }

    [Fact]
    public async Task OneButtonFlowContinuesLocalReadyIntoApplicationPublication()
    {
        var plan = ExecutionCenterViewModelTests.CreatePlan(initializeRepository: true);
        var execution = ExecutionCenterViewModelTests.CreateExecution(plan);
        var completed = ExecutionCenterViewModelTests.CreateCompletedExecution(plan);
        var workflow = new SuccessfulWorkflow(CreateBlueprint(), plan, execution);
        var publication = new RecordingPublicationWorkflow(completed.Checkpoint);
        var sut = CreateViewModel(workflow, publication);
        sut.Name = "Sample";
        sut.RootPath = "C:\\Projects";
        sut.OutputFolder = "sample";

        await sut.LoadAsync(CancellationToken.None);
        await sut.ReviewPlanAsync(CancellationToken.None);
        await sut.PlanPreview!.CreateAndValidateAsync(CancellationToken.None);

        Assert.Equal(plan.RunId, publication.RunId);
        Assert.Equal(PublicationMutationMode.Normal, publication.Mode);
        Assert.Equal(ProjectCreationStage.Completed, sut.Stage);
        Assert.True(sut.LocalReady!.IsDomainCompleted);
        Assert.Equal(new string('a', 40), sut.LocalReady.InitialCommitId);
        Assert.Equal(["main"], sut.LocalReady.Branches.ToArray());
    }

    [Theory]
    [InlineData("name")]
    [InlineData("root")]
    [InlineData("output")]
    [InlineData("ide")]
    [InlineData("input")]
    [InlineData("git")]
    [InlineData("branch")]
    [InlineData("publish")]
    [InlineData("visibility")]
    [InlineData("account")]
    [InlineData("repository")]
    public async Task AnyReviewedDraftChangeInvalidatesThePlan(string change)
    {
        var plan = ExecutionCenterViewModelTests.CreatePlan();
        var workflow = new StubWorkflow(CreateBlueprint(), plan);
        var sut = CreateViewModel(workflow);
        sut.Name = "Sample";
        sut.RootPath = "C:\\Projects";
        sut.OutputFolder = "sample";
        await sut.LoadAsync(CancellationToken.None);
        await sut.ReviewPlanAsync(CancellationToken.None);
        Assert.Equal(ProjectCreationStage.ReviewPlan, sut.Stage);
        Assert.NotNull(sut.ReviewedPlan);

        switch (change)
        {
            case "name": sut.Name = "Changed"; break;
            case "root": sut.RootPath = "C:\\Other"; break;
            case "output": sut.OutputFolder = "changed"; break;
            case "ide": sut.IdeId = "vscode"; break;
            case "input": sut.Inputs[0].TextValue = "changed"; break;
            case "git": sut.InitializeRepository = false; break;
            case "branch": sut.BranchPolicy = GitBranchPolicy.MainAndDevelop; break;
            case "publish": sut.PublishToGitHub = true; break;
            case "visibility": sut.IsPrivate = false; break;
            case "account": sut.GitHubAccount = "reviewed-owner"; break;
            case "repository": sut.GitHubRepository = "reviewed-repository"; break;
            default: throw new ArgumentOutOfRangeException(nameof(change));
        }

        Assert.Equal(ProjectCreationStage.Configure, sut.Stage);
        Assert.Null(sut.ReviewedPlan);
        Assert.Null(sut.PlanPreview);
        Assert.Null(sut.ExecutionSnapshot);
        Assert.Null(sut.LocalReady);
    }

    private static BlueprintInputPropertyDefinition Definition(
        string id,
        BlueprintInputKind kind,
        bool required,
        BlueprintValue? defaultValue,
        IReadOnlyCollection<string?> allowed,
        int? minLength,
        int? maxLength,
        long? min,
        long? max)
    {
        return BlueprintInputPropertyDefinition.Create(new BlueprintInputPropertyDraft(
            id, kind, required, defaultValue, allowed, minLength, maxLength, min, max)).Value;
    }

    private static CreateProjectViewModel CreateViewModel(
        IProjectCreationWorkflow workflow,
        IProjectPublicationWorkflow? publicationWorkflow = null)
    {
        return new CreateProjectViewModel(
            workflow,
            new ExecutionCenterViewModel(
                new ExecutionSessionCoordinator(workflow, new UnsupportedRecovery())),
            new UnusedLocalReadyService(),
            new ProjectCreationSelection(),
            publicationWorkflow ?? new UnusedProjectPublicationWorkflow());
    }

    private static ResolvedBlueprint CreateBlueprint()
    {
        var definitions = new[]
        {
            Definition("name", BlueprintInputKind.Text, true,
                BlueprintValue.FromString("sample").Value, [], 2, 40, null, null),
            Definition("style", BlueprintInputKind.Choice, true,
                BlueprintValue.FromString("modern").Value, ["modern", "classic"], null, null, null, null),
            Definition("tests", BlueprintInputKind.Boolean, false,
                BlueprintValue.FromBoolean(true), [], null, null, null, null),
            Definition("projects", BlueprintInputKind.WholeNumber, true,
                BlueprintValue.FromInteger(3), [], null, null, 1, 5),
        };
        var manifest = BlueprintManifest.Create(
            new BlueprintManifestDraft(
                "sample.local", "1.0.0", ">=1.0.0 <2.0.0", [],
                [
                    new InputDefinition("name", BlueprintInputKind.Text, true, "sample"),
                    new InputDefinition("style", BlueprintInputKind.Choice, true, "modern"),
                    new InputDefinition("tests", BlueprintInputKind.Boolean, false, "true"),
                    new InputDefinition("projects", BlueprintInputKind.WholeNumber, true, "3"),
                ],
                [],
                [new BlueprintStepDefinition("create", "create-directory", TimeSpan.FromSeconds(30))],
                []),
            new BlueprintTrustAssignment(BlueprintTrust.BuiltIn)).Value;
        var fingerprint = BlueprintFingerprint.Create(
            "built-in",
            WorkspaceRelativePath.Create("sample.local\\1.0.0").Value,
            BlueprintTrust.BuiltIn,
            $"sha256:{new string('2', 64)}").Value;
        return ResolvedBlueprint.Create(manifest, definitions, fingerprint).Value;
    }

    private sealed class StubWorkflow(
        ResolvedBlueprint blueprint,
        ProjectCreationPlanSnapshot? plan = null) : IProjectCreationWorkflow
    {
        public ProjectCreationDraft? Draft { get; private set; }

        public Task<BlueprintCatalogSnapshot> LoadCatalogAsync(
            bool forceRefresh,
            CancellationToken cancellationToken) =>
            Task.FromResult(BlueprintCatalogSnapshot.Create([blueprint], []).Value);

        public Task<ValidationResult<ProjectCreationPlanSnapshot>> CreatePlanAsync(
            ProjectCreationDraft draft,
            CancellationToken cancellationToken)
        {
            Draft = draft;
            return Task.FromResult(plan is null
                ? ValidationResult.Failure<ProjectCreationPlanSnapshot>(
                [
                    new ValidationIssue("test.plan.unavailable", "Test plan unavailable.", "plan"),
                ])
                : ValidationResult.Success(plan));
        }

        public Task<ValidationResult<ProjectCreationExecutionSnapshot>> ExecuteAsync(
            ProjectCreationPlanSnapshot plan,
            IProgress<ExecutionProgressLine>? progress,
            CancellationToken cancellationToken) =>
            Task.FromResult(ValidationResult.Failure<ProjectCreationExecutionSnapshot>(
            [
                new ValidationIssue("test.execution.unavailable", "Test execution unavailable.", "execution"),
            ]));
    }

    private sealed class SuccessfulWorkflow(
        ResolvedBlueprint blueprint,
        ProjectCreationPlanSnapshot plan,
        ProjectCreationExecutionSnapshot execution) : IProjectCreationWorkflow
    {
        public Task<BlueprintCatalogSnapshot> LoadCatalogAsync(bool forceRefresh, CancellationToken cancellationToken) =>
            Task.FromResult(BlueprintCatalogSnapshot.Create([blueprint], []).Value);

        public Task<ValidationResult<ProjectCreationPlanSnapshot>> CreatePlanAsync(
            ProjectCreationDraft draft,
            CancellationToken cancellationToken) => Task.FromResult(ValidationResult.Success(plan));

        public Task<ValidationResult<ProjectCreationExecutionSnapshot>> ExecuteAsync(
            ProjectCreationPlanSnapshot reviewedPlan,
            IProgress<ExecutionProgressLine>? progress,
            CancellationToken cancellationToken) => Task.FromResult(ValidationResult.Success(execution));
    }

    private sealed class RecordingPublicationWorkflow(RunCheckpoint checkpoint)
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
                ProjectPublicationOutcome.Create(checkpoint, error: null).Value));
        }
    }

    private sealed class UnsupportedRecovery : IRunRecoveryService
    {
        public Task<ExecutionOperationResult<RunRecoveryBatch>> RecoverInterruptedAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ExecutionOperationResult<RunCheckpoint>> NormalizeInterruptedAsync(RunCheckpoint checkpoint, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ExecutionOperationResult<RunCheckpoint>> ResumeAsync(ExecutionRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ExecutionOperationResult<StagingCleanupReceipt>> CleanupAsync(RunCheckpoint checkpoint, IWorkspaceFileSystem targetParentWorkspace, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
