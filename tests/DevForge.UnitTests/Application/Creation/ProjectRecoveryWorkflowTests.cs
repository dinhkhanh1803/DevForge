using DevForge.Application.Contracts;
using DevForge.Application.Creation;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Execution;
using DevForge.Domain.Projects;
using DevForge.Domain.Runs;

namespace DevForge.UnitTests.Application.Creation;

public sealed class ProjectRecoveryWorkflowTests
{
    [Fact]
    public async Task InspectFailsClosedWhenExactBlueprintFingerprintIsUnavailable()
    {
        var workspaces = new ThrowingWorkspaceFactory();
        var sut = new ProjectRecoveryWorkflow(
            null!, workspaces, null!, null!, new StubBlueprintInspector(isCurrent: false));

        var result = await sut.InspectAsync(CreateCheckpoint(), CancellationToken.None);

        Assert.Equal(ProjectRecoveryEligibility.None, result);
        Assert.Equal(0, workspaces.OpenCount);
    }

    [Fact]
    public async Task InspectFailsClosedWhenGuardedWorkspacesCannotBeOpened()
    {
        var workspaces = new ThrowingWorkspaceFactory();
        var sut = new ProjectRecoveryWorkflow(
            null!, workspaces, null!, null!, new StubBlueprintInspector(isCurrent: true));

        var result = await sut.InspectAsync(CreateCheckpoint(), CancellationToken.None);

        Assert.Equal(ProjectRecoveryEligibility.None, result);
        Assert.Equal(1, workspaces.OpenCount);
    }

    private static RunCheckpoint CreateCheckpoint()
    {
        var blueprint = BlueprintReference.Create("sample.local", "1.0.0").Value;
        var step = ExecutionStep.Create(
            "create", "Create", "create-directory", [], TimeSpan.FromSeconds(30), RetryPolicy.None).Value;
        var plan = ExecutionPlan.Create($"sha256:{new string('1', 64)}", [step], []).Value;
        var preview = PlanPreview.Create(
            blueprint,
            [new PlanPreviewStep(step.Id, step.Handler, step.Timeout)],
            [], [], [], [], [], [], [], [],
            GitOptions.Create().Value,
            CompletionOptions.Create().Value,
            plan.Id).Value;
        var fingerprint = BlueprintFingerprint.Create(
            "trusted-local",
            WorkspaceRelativePath.Create("sample.local\\1.0.0").Value,
            BlueprintTrust.TrustedLocal,
            $"sha256:{new string('2', 64)}").Value;
        var run = ProjectRun.Create("run-1", "recipe-1").Value
            .TransitionTo(RunStatus.Planning).Value
            .TransitionTo(RunStatus.Executing).Value
            .TransitionTo(RunStatus.Cancelled).Value;
        return RunCheckpoint.Create(
            run,
            plan,
            preview,
            blueprint,
            fingerprint,
            StagingDescriptor.Create(
                WorkspaceRelativePath.Create(".devforge-staging\\run-1").Value,
                WorkspaceRelativePath.Create(".devforge-staging\\run-1\\payload").Value,
                WorkspaceRelativePath.Create(".devforge-staging\\run-1\\ownership.json").Value,
                "marker-1").Value,
            TargetDescriptor.Create(
                WorkspaceRoot.Create("C:\\Projects").Value,
                WorkspaceRelativePath.Create("sample").Value,
                WorkspaceRelativePath.Create(".devforge-finalize-run-1").Value).Value,
            RunArtifactDescriptor.Create(WorkspaceRoot.Create("C:\\DevForgeData\\runs\\run-1").Value).Value,
            [],
            FinalizationState.NotStarted,
            ReportPersistenceState.NotStarted).Value;
    }

    private sealed class StubBlueprintInspector(bool isCurrent) : IBlueprintRecoveryInspector
    {
        public Task<bool> IsCurrentAsync(
            BlueprintReference blueprint,
            BlueprintFingerprint fingerprint,
            CancellationToken cancellationToken) => Task.FromResult(isCurrent);
    }

    private sealed class ThrowingWorkspaceFactory : IProjectRecoveryWorkspaceFactory
    {
        public int OpenCount { get; private set; }

        public Task<ProjectRecoveryWorkspaces> OpenAsync(
            RunCheckpoint checkpoint,
            CancellationToken cancellationToken)
        {
            OpenCount++;
            throw new IOException("The test workspace is unavailable.");
        }

        public Task<IWorkspaceFileSystem> OpenFinalProjectAsync(
            RunCheckpoint checkpoint,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public LocalReadyPresentation DescribeLocalReady(RunCheckpoint checkpoint) =>
            throw new NotSupportedException();
    }
}
