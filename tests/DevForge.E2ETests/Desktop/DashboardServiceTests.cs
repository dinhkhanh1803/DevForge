using System.Collections.Immutable;
using DevForge.Application.Contracts;
using DevForge.Application.Contracts.Persistence;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Desktop.Dashboard;
using DevForge.Desktop.EnvironmentDoctor;
using DevForge.Domain.Execution;
using DevForge.Domain.Runs;

namespace DevForge.E2ETests.Desktop;

public sealed class DashboardServiceTests
{
    [Fact]
    public async Task AggregatesBoundedRecentPresetActionAndHealthState()
    {
        var now = new DateTimeOffset(2026, 8, 12, 8, 0, 0, TimeSpan.Zero);
        var recent = RecentProjectRecord.Create(
            @"C:\Projects\Missing",
            "Missing project",
            null,
            "rider",
            now).Value;
        var recipe = PersistableJson.Create("{}").Value;
        var preset = PresetRecord.Create("wpf", "WPF App", 1, recipe, now).Value;
        var location = new FakeLocationProbe(ProjectLocationStatus.Unavailable);
        var health = new EnvironmentHealthSnapshot(
            [],
            now,
            EnvironmentSnapshotSource.Cache,
            IsStale: false,
            ScanFailed: false);
        var sut = new DashboardService(
            new RecentStore(recent),
            new PresetStore(preset),
            new CheckpointStore(CreateFailedCheckpoint()),
            location,
            new FakeEnvironmentService(health));

        var result = await sut.LoadAsync(CancellationToken.None);

        Assert.Single(result.RecentProjects);
        Assert.Equal(ProjectLocationStatus.Unavailable, result.RecentProjects[0].LocationStatus);
        Assert.Single(result.SavedPresets);
        Assert.Single(result.ActionNeededRuns);
        Assert.Equal(RunStatus.Failed, result.ActionNeededRuns[0].Status);
        Assert.Same(health, result.EnvironmentHealth);
    }

    [Fact]
    public async Task EmptyStoresProduceExplicitEmptyStates()
    {
        var sut = new DashboardService(
            new RecentStore(),
            new PresetStore(),
            new CheckpointStore(),
            new FakeLocationProbe(ProjectLocationStatus.Available),
            new FakeEnvironmentService(new EnvironmentHealthSnapshot(
                [], null, EnvironmentSnapshotSource.Cache, true, false)));

        var result = await sut.LoadAsync(CancellationToken.None);

        Assert.True(result.HasNoRecentProjects);
        Assert.True(result.HasNoSavedPresets);
        Assert.True(result.HasNoActionNeededRuns);
    }

    private static RunCheckpoint CreateFailedCheckpoint()
    {
        var step = ExecutionStep.Create(
            "build", "Build", "run-process", [], TimeSpan.FromMinutes(1), RetryPolicy.None).Value;
        var plan = ExecutionPlan.Create($"sha256:{new string('1', 64)}", [step], []).Value;
        var run = ProjectRun.Create("run-dashboard", "recipe").Value
            .TransitionTo(RunStatus.Planning).Value
            .TransitionTo(RunStatus.Executing).Value
            .TransitionTo(RunStatus.Failed).Value;
        var blueprint = BlueprintReference.Create("desktop.csharp-wpf-tool", "1.0.0").Value;
        var fingerprint = BlueprintFingerprint.Create(
            "built-in", Relative("desktop.csharp-wpf-tool\\1.0.0"), BlueprintTrust.BuiltIn,
            $"sha256:{new string('2', 64)}").Value;
        return RunCheckpoint.Create(
            run,
            plan,
            blueprint,
            fingerprint,
            StagingDescriptor.Create(
                Relative(".devforge-staging\\run-dashboard"),
                Relative(".devforge-staging\\run-dashboard\\payload"),
                Relative(".devforge-staging\\run-dashboard\\ownership.json"),
                "run-dashboard").Value,
            TargetDescriptor.Create(
                WorkspaceRoot.Create(@"C:\Target").Value,
                Relative("project"),
                null).Value,
            RunArtifactDescriptor.Create(WorkspaceRoot.Create(@"C:\Artifacts").Value).Value,
            [],
            FinalizationState.NotStarted,
            ReportPersistenceState.NotStarted).Value;
    }

    private static WorkspaceRelativePath Relative(string value) =>
        WorkspaceRelativePath.Create(value).Value;

    private sealed class RecentStore(params RecentProjectRecord[] values) : IRecentProjectStore
    {
        public Task<RecentProjectRecord?> GetAsync(string projectPath, CancellationToken cancellationToken) =>
            Task.FromResult<RecentProjectRecord?>(values.FirstOrDefault());
        public Task<ImmutableArray<RecentProjectRecord>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult(values.ToImmutableArray());
        public Task UpsertAsync(RecentProjectRecord project, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> RemoveAsync(string projectPath, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class PresetStore(params PresetRecord[] values) : IPresetStore
    {
        public Task<PresetRecord?> GetAsync(string id, CancellationToken cancellationToken) =>
            Task.FromResult<PresetRecord?>(values.FirstOrDefault());
        public Task<ImmutableArray<PresetRecord>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult(values.ToImmutableArray());
        public Task UpsertAsync(PresetRecord preset, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> RemoveAsync(string id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class CheckpointStore(params RunCheckpoint[] values) : IRunCheckpointStore
    {
        public Task SaveAsync(RunCheckpoint checkpoint, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<RunCheckpoint?> FindAsync(string runId, CancellationToken cancellationToken) =>
            Task.FromResult<RunCheckpoint?>(values.FirstOrDefault());
        public Task<ImmutableArray<RunCheckpoint>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult(values.ToImmutableArray());
    }

    private sealed class FakeLocationProbe(ProjectLocationStatus status) : IProjectLocationProbe
    {
        public Task<ProjectLocationStatus> InspectAsync(string? canonicalRoot, CancellationToken cancellationToken) =>
            Task.FromResult(status);
    }

    private sealed class FakeEnvironmentService(EnvironmentHealthSnapshot snapshot)
        : IEnvironmentDoctorService
    {
        public Task<EnvironmentHealthSnapshot> LoadAsync(bool forceRefresh, CancellationToken cancellationToken) =>
            Task.FromResult(snapshot);

        public Task<EnvironmentHealthSnapshot> LoadCachedAsync(CancellationToken cancellationToken) =>
            Task.FromResult(snapshot);
    }
}
