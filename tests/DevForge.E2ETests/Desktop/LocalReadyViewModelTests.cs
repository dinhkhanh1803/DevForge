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
        var launcher = new FailingIdeLauncher();
        var sut = new LocalReadyViewModel(snapshot, launcher);
        var checkpoint = snapshot.Checkpoint;

        await sut.OpenIdeAsync(new StubWorkspace(), "vscode", CancellationToken.None);

        Assert.Equal("LOCAL PROJECT READY", sut.StatusLabel);
        Assert.False(sut.IsDomainCompleted);
        Assert.DoesNotContain("COMPLETED", sut.StatusLabel, StringComparison.Ordinal);
        Assert.Same(checkpoint, sut.Snapshot.Checkpoint);
        Assert.Equal("IDE could not be opened.", sut.IdeErrorMessage);
        Assert.Equal(FinalizationState.Succeeded, sut.FinalizationState);
        Assert.Equal(ReportPersistenceState.Succeeded, sut.ReportState);
    }

    private static ProjectCreationExecutionSnapshot CreateLocalReadySnapshot()
    {
        var reference = BlueprintReference.Create("sample.local", "1.0.0").Value;
        var draft = ProjectCreationDraft.Create(
            "Sample", "C:\\Projects", "sample", reference, [], [], "none").Value;
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
            run, plan, reference, fingerprint,
            StagingDescriptor.Create(
                WorkspaceRelativePath.Create($".devforge-staging\\{planSnapshot.RunId}").Value,
                WorkspaceRelativePath.Create($".devforge-staging\\{planSnapshot.RunId}\\payload").Value,
                WorkspaceRelativePath.Create($".devforge-staging\\{planSnapshot.RunId}\\ownership.json").Value,
                "marker-1").Value,
            TargetDescriptor.Create(
                target.ParentRoot, target.TargetDirectory,
                WorkspaceRelativePath.Create($".devforge-finalize-{planSnapshot.RunId}").Value).Value,
            RunArtifactDescriptor.Create(WorkspaceRoot.Create("C:\\DevForgeData\\runs\\run-0123456789abcdef0123456789abcdef").Value).Value,
            [], FinalizationState.Succeeded, ReportPersistenceState.Succeeded).Value;
        return ProjectCreationExecutionSnapshot.Create(planSnapshot, checkpoint).Value;
    }

    private sealed class FailingIdeLauncher : IIdeLauncher
    {
        public Task LaunchAsync(IdeLaunchRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Failure must remain UI-only.");
    }

    private sealed class StubWorkspace : IWorkspaceFileSystem
    {
        public WorkspaceRoot Root { get; } = WorkspaceRoot.Create("C:\\Projects\\sample").Value;
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
