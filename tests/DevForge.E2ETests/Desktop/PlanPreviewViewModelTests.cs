using DevForge.Application.Contracts;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Desktop.CreateProject;
using DevForge.Desktop.Execution;
using DevForge.Domain.Execution;
using DevForge.Domain.Privacy;
using DevForge.Domain.Projects;
using DevForge.Domain.Validation;

namespace DevForge.E2ETests.Desktop;

public sealed class PlanPreviewViewModelTests
{
    [Fact]
    public async Task ProjectionPreservesExactReviewedOrderAndSafeEvidence()
    {
        var snapshot = CreateSnapshot();
        ProjectCreationPlanSnapshot? executed = null;
        var backedOut = false;
        var sut = new PlanPreviewViewModel(
            snapshot,
            (plan, _) =>
            {
                executed = plan;
                return Task.CompletedTask;
            },
            () => backedOut = true);

        Assert.Same(snapshot, sut.Snapshot);
        Assert.Equal(snapshot.PlannedProject.Plan.Id, sut.PlanHash);
        Assert.Equal("sample.local 1.0.0", sut.BlueprintLabel);
        Assert.Equal("BuiltIn", sut.TrustLabel);
        Assert.Equal(["src", "README.md"], sut.Artifacts.Select(item => item.Path));
        Assert.Equal(["package-a", "package-b"], sut.Dependencies.Select(item => item.Id));
        Assert.Equal(["dotnet", "git"], sut.Tools.Select(item => item.Id));
        Assert.Equal(["create", "render"], sut.Steps.Select(item => item.Id));
        Assert.Equal(["build", "test"], sut.Validators.Select(item => item.Id));
        Assert.Equal(["framework"], sut.Inputs.Select(item => item.Id));
        Assert.Equal(["docs"], sut.Features.ToArray());
        Assert.Equal(["preview.warning"], sut.Warnings.Select(item => item.Code));
        Assert.Equal("dotnet restore [ARGS REDACTED]", sut.Steps[0].ProcessPreview);
        Assert.False(sut.GitEnabled);

        await sut.CreateAndValidateAsync(CancellationToken.None);
        sut.BackToConfigureCommand.Execute(null);

        Assert.Same(snapshot, executed);
        Assert.True(backedOut);
    }

    [Fact]
    public void EditingConfigureStateClearsReviewedPlan()
    {
        var workflow = new NoOpWorkflow();
        var sut = new CreateProjectViewModel(
            workflow,
            new ExecutionSessionCoordinator(workflow, new UnsupportedRecovery()))
        {
            ReviewedPlan = CreateSnapshot(),
            Stage = ProjectCreationStage.ReviewPlan,
        };

        sut.Name = "Changed";

        Assert.Null(sut.ReviewedPlan);
        Assert.Null(sut.PlanPreview);
        Assert.Equal(ProjectCreationStage.Configure, sut.Stage);
    }

    private static ProjectCreationPlanSnapshot CreateSnapshot()
    {
        var reference = BlueprintReference.Create("sample.local", "1.0.0").Value;
        var draft = ProjectCreationDraft.Create(
            "Sample", "C:\\Projects", "sample", reference,
            new Dictionary<string, DynamicInputValue?>
            {
                ["framework"] = DynamicInputValue.Text("net10.0").Value,
            },
            ["docs"],
            "none").Value;
        var git = GitOptions.Create(initializeRepository: false).Value;
        var completion = CompletionOptions.Create().Value;
        var recipe = ProjectRecipe.Create(new ProjectRecipeDraft(
            "Sample", "C:\\Projects\\sample", "sample.local", "1.0.0",
            new Dictionary<string, string?> { ["framework"] = "net10.0" },
            ["docs"], null, git, completion)).Value;
        var restorePreview = RedactedText.FromTrustedRedaction("dotnet restore [ARGS REDACTED]").Value;
        var steps = new[]
        {
            ExecutionStep.Create("create", "Create", "create-directory", [], TimeSpan.FromSeconds(30), RetryPolicy.None).Value,
            ExecutionStep.Create("render", "Render", "render-template", [], TimeSpan.FromSeconds(30), RetryPolicy.None).Value,
        };
        var validators = new[]
        {
            ExecutionValidator.Create("build", "validate-command", [], TimeSpan.FromMinutes(2), required: true).Value,
            ExecutionValidator.Create("test", "validate-command", [], TimeSpan.FromMinutes(2), required: true).Value,
        };
        var hash = $"sha256:{new string('1', 64)}";
        var plan = ExecutionPlan.Create(hash, steps, validators).Value;
        var preview = PlanPreview.Create(
            reference,
            [
                new PlanPreviewStep("create", "create-directory", TimeSpan.FromSeconds(30), restorePreview),
                new PlanPreviewStep("render", "render-template", TimeSpan.FromSeconds(30)),
            ],
            [
                new PlanPreviewValidator("build", "validate-command", TimeSpan.FromMinutes(2), true),
                new PlanPreviewValidator("test", "validate-command", TimeSpan.FromMinutes(2), true),
            ],
            [
                new ToolRequirement("dotnet", ">=10.0.0 <11.0.0"),
                new ToolRequirement("git", ">=2.40.0"),
            ],
            [
                new PlanPreviewToolStatus("dotnet", ">=10.0.0 <11.0.0", true, true, true, "10.0.302"),
                new PlanPreviewToolStatus("git", ">=2.40.0", true, true, true, "2.51.0"),
            ],
            [new BlueprintDependency("package-a", "1.0.0"), new BlueprintDependency("package-b", "2.0.0")],
            [new BlueprintArtifact("src"), new BlueprintArtifact("README.md")],
            [new ValidationIssue("preview.warning", "Review this setting.", "preview")],
            [KeyValuePair.Create<string, PlanValue?>("framework", PlanValue.FromString("net10.0").Value)],
            ["docs"],
            git,
            completion,
            hash).Value;
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

    private sealed class NoOpWorkflow : IProjectCreationWorkflow
    {
        public Task<BlueprintCatalogSnapshot> LoadCatalogAsync(bool forceRefresh, CancellationToken cancellationToken) =>
            Task.FromResult(BlueprintCatalogSnapshot.Create([], []).Value);

        public Task<ValidationResult<ProjectCreationPlanSnapshot>> CreatePlanAsync(
            ProjectCreationDraft draft,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ValidationResult<ProjectCreationExecutionSnapshot>> ExecuteAsync(
            ProjectCreationPlanSnapshot plan,
            IProgress<ExecutionProgressLine>? progress,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class UnsupportedRecovery : IRunRecoveryService
    {
        public Task<ExecutionOperationResult<RunRecoveryBatch>> RecoverInterruptedAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ExecutionOperationResult<RunCheckpoint>> NormalizeInterruptedAsync(RunCheckpoint checkpoint, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ExecutionOperationResult<RunCheckpoint>> ResumeAsync(ExecutionRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ExecutionOperationResult<StagingCleanupReceipt>> CleanupAsync(RunCheckpoint checkpoint, IWorkspaceFileSystem targetParentWorkspace, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
