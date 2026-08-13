using System.Collections.Immutable;
using DevForge.Application.Contracts;
using DevForge.Application.Creation;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Execution;
using DevForge.Domain.Projects;
using DevForge.Domain.Runs;
using DevForge.Domain.Validation;

namespace DevForge.UnitTests.Application.Creation;

public sealed class ProjectCreationWorkflowTests
{
    [Fact]
    public async Task PlanningUsesExactBlueprintAndPreflightsBeforePlannerOnce()
    {
        var events = new List<string>();
        var fixture = CreateFixture(events);

        var result = await fixture.Workflow.CreatePlanAsync(
            ValidDraft(),
            CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal(["catalog.find", "target.preflight", "planner"], events);
        Assert.Equal(1, fixture.Planner.CallCount);
        Assert.False(result.Value.Recipe.Git.InitializeRepository);
        Assert.Equal("net10.0", result.Value.Recipe.Inputs["framework"]);
        Assert.Equal(fixture.Blueprint.Fingerprint, result.Value.PlannedProject.BlueprintFingerprint);
        Assert.Equal("run-0123456789abcdef0123456789abcdef", result.Value.RunId);
        Assert.Equal("recipe-0123456789abcdef0123456789abcdef", result.Value.RecipeId);
    }

    [Fact]
    public async Task PlanningStopsBeforePlannerWhenTargetIsRejected()
    {
        var fixture = CreateFixture();
        fixture.Target.Result = ValidationResult.Failure<ProjectTargetDescriptor>(
        [
            new ValidationIssue("project.target.not-empty", "Target exists.", "outputFolder"),
        ]);

        var result = await fixture.Workflow.CreatePlanAsync(ValidDraft(), CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "project.target.not-empty");
        Assert.Equal(0, fixture.Planner.CallCount);
    }

    [Fact]
    public async Task PlanningRejectsPlannerFingerprintSubstitution()
    {
        var fixture = CreateFixture();
        fixture.Planner.Result = CreatePlannedProject(
            BlueprintFingerprint.Create(
                "built-in",
                Relative("other\\1.0.0"),
                BlueprintTrust.BuiltIn,
                $"sha256:{new string('9', 64)}").Value);

        var result = await fixture.Workflow.CreatePlanAsync(ValidDraft(), CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "creation.plan.blueprint-fingerprint.mismatch");
    }

    [Fact]
    public async Task PlanningRejectsPlannerBlueprintIdentitySubstitution()
    {
        var fixture = CreateFixture();
        fixture.Planner.Result = CreatePlannedProject(
            fixture.Blueprint.Fingerprint,
            BlueprintReference.Create("other.local", "1.0.0").Value);

        var result = await fixture.Workflow.CreatePlanAsync(ValidDraft(), CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "creation.plan.blueprint.mismatch");
    }

    [Fact]
    public async Task CatalogRefreshIsExplicit()
    {
        var fixture = CreateFixture();

        var first = await fixture.Workflow.LoadCatalogAsync(false, CancellationToken.None);
        var second = await fixture.Workflow.LoadCatalogAsync(true, CancellationToken.None);

        Assert.Single(first.ExecutableBlueprints);
        Assert.Single(second.ExecutableBlueprints);
        Assert.Equal(1, fixture.Catalog.RefreshCount);
        Assert.Equal(2, fixture.Catalog.InspectCount);
    }

    [Fact]
    public async Task ExecutionCreatesFreshRunAndForwardsProgressToOrchestrator()
    {
        var fixture = CreateFixture();
        var plan = (await fixture.Workflow.CreatePlanAsync(
            ValidDraft(),
            CancellationToken.None)).Value;
        var checkpoint = CreateCheckpoint(plan);
        fixture.Orchestrator.Result = checkpoint;
        var progress = new Progress<ExecutionProgressLine>();

        var result = await fixture.Workflow.ExecuteAsync(plan, progress, CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Same(checkpoint, result.Value.Checkpoint);
        Assert.NotNull(fixture.Orchestrator.Request);
        Assert.Equal(ExecutionMode.Fresh, fixture.Orchestrator.Request.Mode);
        Assert.Equal(RunStatus.Draft, fixture.Orchestrator.Request.Run.Status);
        Assert.Same(progress, fixture.Orchestrator.Progress);
        Assert.Equal(1, fixture.Workspaces.CallCount);
    }

    [Fact]
    public async Task ExecutionReturnsWorkspaceFailureWithoutCallingOrchestrator()
    {
        var fixture = CreateFixture();
        var plan = (await fixture.Workflow.CreatePlanAsync(
            ValidDraft(),
            CancellationToken.None)).Value;
        fixture.Workspaces.Result = ValidationResult.Failure<ProjectExecutionWorkspaces>(
        [
            new ValidationIssue("project.workspaces.unavailable", "Unavailable.", "target"),
        ]);

        var result = await fixture.Workflow.ExecuteAsync(plan, null, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Null(fixture.Orchestrator.Request);
    }

    [Fact]
    public async Task CancellationStopsPlanningBeforeMutation()
    {
        var fixture = CreateFixture();
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            fixture.Workflow.CreatePlanAsync(ValidDraft(), source.Token));

        Assert.Equal(0, fixture.Planner.CallCount);
        Assert.Equal(0, fixture.Target.CallCount);
    }

    private static Fixture CreateFixture(List<string>? events = null)
    {
        var blueprint = CreateBlueprint();
        var catalog = new StubCatalog(blueprint, events);
        var target = new StubTarget(events);
        var planned = CreatePlannedProject(blueprint.Fingerprint);
        var planner = new StubPlanner(planned, events);
        var targetParent = new StubWorkspace("C:\\Projects");
        var artifacts = new StubWorkspace("C:\\DevForgeData\\runs\\run-0123456789abcdef0123456789abcdef");
        var workspaces = new StubWorkspaceFactory(
            ProjectExecutionWorkspaces.Create(target.Result.Value, targetParent, artifacts));
        var orchestrator = new StubOrchestrator();
        var workflow = new ProjectCreationWorkflow(
            catalog,
            planner,
            target,
            workspaces,
            new FixedIdentityGenerator(),
            orchestrator,
            new FixedTimeProvider());
        return new Fixture(
            workflow,
            catalog,
            target,
            planner,
            workspaces,
            orchestrator,
            blueprint);
    }

    private static ProjectCreationDraft ValidDraft()
    {
        return ProjectCreationDraft.Create(
            "Sample",
            "C:\\Projects",
            "sample",
            BlueprintReference.Create("sample.local", "1.0.0").Value,
            new Dictionary<string, DynamicInputValue?>
            {
                ["framework"] = DynamicInputValue.Text("net10.0").Value,
            },
            [],
            "none").Value;
    }

    private static ResolvedBlueprint CreateBlueprint()
    {
        var manifest = BlueprintManifest.Create(
            new BlueprintManifestDraft(
                "sample.local",
                "1.0.0",
                ">=1.0.0 <2.0.0",
                [],
                [new InputDefinition("framework", BlueprintInputKind.Text, true)],
                [],
                [new BlueprintStepDefinition("create", "create-directory", TimeSpan.FromSeconds(30))],
                []),
            new BlueprintTrustAssignment(BlueprintTrust.BuiltIn)).Value;
        var schema = BlueprintInputPropertyDefinition.Create(
            new BlueprintInputPropertyDraft(
                "framework",
                BlueprintInputKind.Text,
                true,
                null,
                [],
                1,
                40,
                null,
                null)).Value;
        var fingerprint = BlueprintFingerprint.Create(
            "built-in",
            Relative("sample.local\\1.0.0"),
            BlueprintTrust.BuiltIn,
            $"sha256:{new string('2', 64)}").Value;
        return ResolvedBlueprint.Create(manifest, [schema], fingerprint).Value;
    }

    private static PlannedProject CreatePlannedProject(
        BlueprintFingerprint fingerprint,
        BlueprintReference? blueprint = null)
    {
        var hash = $"sha256:{new string('1', 64)}";
        var step = ExecutionStep.Create(
            "create",
            "Create",
            "create-directory",
            [],
            TimeSpan.FromSeconds(30),
            RetryPolicy.None).Value;
        var plan = ExecutionPlan.Create(hash, [step], []).Value;
        var preview = PlanPreview.Create(
            blueprint ?? BlueprintReference.Create("sample.local", "1.0.0").Value,
            [new PlanPreviewStep("create", "create-directory", TimeSpan.FromSeconds(30))],
            [], [], [], [], [], [], [], [],
            GitOptions.Create(initializeRepository: false).Value,
            CompletionOptions.Create().Value,
            hash).Value;
        return PlannedProject.Create(plan, preview, fingerprint).Value;
    }

    private static RunCheckpoint CreateCheckpoint(ProjectCreationPlanSnapshot snapshot)
    {
        var run = ProjectRun.Create(snapshot.RunId, snapshot.RecipeId).Value;
        var planning = run.TransitionTo(RunStatus.Planning).Value;
        var executing = planning.TransitionTo(RunStatus.Executing).Value;
        return RunCheckpoint.Create(
            executing,
            snapshot.PlannedProject.Plan,
            snapshot.Draft.Blueprint,
            snapshot.PlannedProject.BlueprintFingerprint,
            StagingDescriptor.Create(
                Relative($".devforge-staging\\{snapshot.RunId}"),
                Relative($".devforge-staging\\{snapshot.RunId}\\payload"),
                Relative($".devforge-staging\\{snapshot.RunId}\\ownership.json"),
                "marker-1").Value,
            TargetDescriptor.Create(
                snapshot.Target.ParentRoot,
                snapshot.Target.TargetDirectory,
                Relative($".devforge-finalize-{snapshot.RunId}")).Value,
            RunArtifactDescriptor.Create(
                WorkspaceRoot.Create("C:\\DevForgeData\\runs\\run-0123456789abcdef0123456789abcdef").Value).Value,
            [],
            FinalizationState.NotStarted,
            ReportPersistenceState.NotStarted).Value;
    }

    private static WorkspaceRelativePath Relative(string value) =>
        WorkspaceRelativePath.Create(value).Value;

    private sealed record Fixture(
        ProjectCreationWorkflow Workflow,
        StubCatalog Catalog,
        StubTarget Target,
        StubPlanner Planner,
        StubWorkspaceFactory Workspaces,
        StubOrchestrator Orchestrator,
        ResolvedBlueprint Blueprint);

    private sealed class StubCatalog(ResolvedBlueprint blueprint, List<string>? events) : IBlueprintCatalog
    {
        public int RefreshCount { get; private set; }
        public int InspectCount { get; private set; }

        public Task RefreshAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RefreshCount++;
            return Task.CompletedTask;
        }

        public Task<BlueprintCatalogSnapshot> InspectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InspectCount++;
            return Task.FromResult(BlueprintCatalogSnapshot.Create([blueprint], []).Value);
        }

        public Task<ImmutableArray<ResolvedBlueprint>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ImmutableArray.Create(blueprint));

        public Task<ResolvedBlueprint?> FindAsync(
            BlueprintReference reference,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events?.Add("catalog.find");
            return Task.FromResult<ResolvedBlueprint?>(blueprint);
        }
    }

    private sealed class StubTarget(List<string>? events) : IProjectTargetPreflight
    {
        public int CallCount { get; private set; }
        public ValidationResult<ProjectTargetDescriptor> Result { get; set; } =
            ProjectTargetDescriptor.Create(
                WorkspaceRoot.Create("C:\\Projects").Value,
                Relative("sample"));

        public Task<ValidationResult<ProjectTargetDescriptor>> PreflightAsync(
            string rootPath,
            string outputFolder,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            events?.Add("target.preflight");
            return Task.FromResult(Result);
        }
    }

    private sealed class StubPlanner(
        PlannedProject plannedProject,
        List<string>? events) : IProjectPlanner
    {
        public int CallCount { get; private set; }
        public PlannedProject Result { get; set; } = plannedProject;

        public Task<ValidationResult<PlannedProject>> CreatePlanAsync(
            ProjectRecipe recipe,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            events?.Add("planner");
            return Task.FromResult(ValidationResult.Success(Result));
        }
    }

    private sealed class StubWorkspaceFactory(
        ValidationResult<ProjectExecutionWorkspaces> result) : IProjectExecutionWorkspaceFactory
    {
        public int CallCount { get; private set; }
        public ValidationResult<ProjectExecutionWorkspaces> Result { get; set; } = result;

        public Task<ValidationResult<ProjectExecutionWorkspaces>> OpenAsync(
            ProjectTargetDescriptor target,
            string runId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(Result);
        }
    }

    private sealed class StubOrchestrator : IExecutionOrchestrator
    {
        public RunCheckpoint? Result { get; set; }
        public ExecutionRequest? Request { get; private set; }
        public IProgress<ExecutionProgressLine>? Progress { get; private set; }

        public Task<RunCheckpoint> ExecuteAsync(
            ExecutionRequest request,
            IProgress<ExecutionProgressLine>? progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Request = request;
            Progress = progress;
            return Task.FromResult(Result ?? throw new InvalidOperationException("No checkpoint configured."));
        }
    }

    private sealed class FixedIdentityGenerator : IRunIdentityGenerator
    {
        public string CreateRunId() => "run-0123456789abcdef0123456789abcdef";
        public string CreateRecipeId() => "recipe-0123456789abcdef0123456789abcdef";
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            new(2026, 8, 13, 9, 0, 0, TimeSpan.Zero);
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
}
