using System.Collections.Immutable;
using System.Reflection;
using DevForge.Application.Contracts;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Execution;
using DevForge.Domain.Projects;
using DevForge.Domain.Runs;

namespace DevForge.UnitTests.Application;

public sealed class RecoverableExecutionContractTests
{
    private static readonly string[] _forbiddenPropertyFragments =
        ["password", "token", "secret", "rawoutput", "absolutepath"];

    [Fact]
    public void ExecutionModesAndCheckpointStatesHaveStableNonzeroValues()
    {
        Assert.Equal(["Fresh", "Resume", "ManualRetry"], Enum.GetNames<ExecutionMode>());
        Assert.Equal([1, 2, 3], Enum.GetValues<ExecutionMode>().Select(value => (int)value));
        Assert.Equal(
            ["NotStarted", "IntentPersisted", "Succeeded", "Failed"],
            Enum.GetNames<FinalizationState>());
        Assert.Equal([1, 2, 3, 4], Enum.GetValues<FinalizationState>().Select(value => (int)value));
        Assert.Equal(
            ["NotStarted", "Succeeded", "Failed"],
            Enum.GetNames<ReportPersistenceState>());
        Assert.Equal([1, 2, 3], Enum.GetValues<ReportPersistenceState>().Select(value => (int)value));
        Assert.Equal(
            ["Prepare", "Precondition", "Execute", "Postcondition", "Persist", "Decide"],
            Enum.GetNames<ExecutionPhase>());
    }

    [Fact]
    public void PlannedProjectCarriesExactBlueprintFingerprint()
    {
        var components = CreatePlanningComponents();

        var result = PlannedProject.Create(
            components.Plan,
            components.Preview,
            components.Fingerprint);

        Assert.True(result.IsValid);
        Assert.Same(components.Fingerprint, result.Value.BlueprintFingerprint);
        Assert.False(PlannedProject.Create(null, null, null).IsValid);
    }

    [Fact]
    public void ExecutionRequestAggregatesMissingAndInvalidBoundaryValues()
    {
        var invalid = ExecutionRequest.Create(
            null,
            null,
            null,
            null,
            null,
            (ExecutionMode)999);

        Assert.False(invalid.IsValid);
        Assert.Equal(6, invalid.Issues.Length);
        Assert.Contains(invalid.Issues, issue => issue.Code == "execution.request.mode.invalid");

        var valid = CreateExecutionRequest();
        var terminalRun = ProjectRun.Create("run-2", "recipe-1").Value
            .TransitionTo(RunStatus.Planning).Value
            .TransitionTo(RunStatus.Executing).Value
            .TransitionTo(RunStatus.Failed).Value;
        var unsafeResume = ExecutionRequest.Create(
            valid.PlannedProject,
            terminalRun,
            valid.TargetParentWorkspace,
            valid.TargetDirectory,
            valid.RunArtifactWorkspace,
            ExecutionMode.Resume);

        Assert.Equal(ExecutionMode.Fresh, valid.Mode);
        Assert.Equal("project", valid.TargetDirectory.Value);
        Assert.NotSame(valid.TargetParentWorkspace, valid.RunArtifactWorkspace);
        Assert.Contains(unsafeResume.Issues, issue => issue.Code == "execution.request.mode.status-mismatch");
    }

    [Fact]
    public void StagingDescriptorRequiresOwnedNestedCanonicalPaths()
    {
        var container = Path(".devforge-staging\\run-1");
        var payload = Path(".devforge-staging\\run-1\\payload");
        var marker = Path(".devforge-staging\\run-1\\ownership.json");

        var valid = StagingDescriptor.Create(container, payload, marker, "marker-1");
        var escapedPayload = StagingDescriptor.Create(
            container,
            Path("other\\payload"),
            marker,
            "marker-1");

        Assert.True(valid.IsValid);
        Assert.False(escapedPayload.IsValid);
        Assert.Contains(escapedPayload.Issues, issue => issue.Code == "staging.payload.outside-container");
    }

    [Fact]
    public void RunCheckpointSnapshotsEvidenceOnceAndEnforcesCompletionOrdering()
    {
        var request = CreateExecutionRequest();
        var staging = CreateStaging();
        var target = TargetDescriptor.Create(
            request.TargetParentWorkspace.Root,
            request.TargetDirectory,
            null).Value;
        var runArtifacts = RunArtifactDescriptor.Create(request.RunArtifactWorkspace.Root).Value;
        var evidence = ExecutionEvidence.Create(
            ExecutionEvidenceKind.Step,
            "create",
            ExecutionEvidenceStatus.Passed,
            $"sha256:{new string('a', 64)}").Value;
        var source = new SingleUseEnumerable<ExecutionEvidence?>([evidence]);
        var reference = BlueprintReference.Create("desktop.csharp-wpf-tool", "1.0.0").Value;

        var valid = RunCheckpoint.Create(
            request.Run,
            request.PlannedProject.Plan,
            reference,
            request.PlannedProject.BlueprintFingerprint,
            staging,
            target,
            runArtifacts,
            source,
            FinalizationState.NotStarted,
            ReportPersistenceState.NotStarted);
        var invalidOrder = RunCheckpoint.Create(
            request.Run,
            request.PlannedProject.Plan,
            reference,
            request.PlannedProject.BlueprintFingerprint,
            staging,
            target,
            runArtifacts,
            [],
            FinalizationState.NotStarted,
            ReportPersistenceState.Succeeded);
        var failedBeforeFinalization = RunCheckpoint.Create(
            request.Run,
            request.PlannedProject.Plan,
            reference,
            request.PlannedProject.BlueprintFingerprint,
            staging,
            target,
            runArtifacts,
            [],
            FinalizationState.NotStarted,
            ReportPersistenceState.Failed);
        var unknownEvidence = ExecutionEvidence.Create(
            ExecutionEvidenceKind.Step,
            "unknown",
            ExecutionEvidenceStatus.Passed,
            $"sha256:{new string('b', 64)}").Value;
        var unknown = RunCheckpoint.Create(
            request.Run,
            request.PlannedProject.Plan,
            reference,
            request.PlannedProject.BlueprintFingerprint,
            staging,
            target,
            runArtifacts,
            [unknownEvidence],
            FinalizationState.NotStarted,
            ReportPersistenceState.NotStarted);

        Assert.True(valid.IsValid);
        Assert.Equal(1, source.EnumerationCount);
        Assert.Equal(evidence, Assert.Single(valid.Value.Evidence));
        Assert.Equal(valid.Value.Plan.Id, valid.Value.PlanHash);
        Assert.Equal(request.TargetParentWorkspace.Root, valid.Value.Target.ParentRoot);
        Assert.Equal(request.RunArtifactWorkspace.Root, valid.Value.RunArtifacts.Root);
        Assert.Contains(invalidOrder.Issues, issue => issue.Code == "checkpoint.report.before-finalization");
        Assert.Contains(
            failedBeforeFinalization.Issues,
            issue => issue.Code == "checkpoint.report.before-finalization");
        Assert.Contains(unknown.Issues, issue => issue.Code == "checkpoint.evidence.step.unknown");
    }

    [Fact]
    public void CompletionReceiptsAreGuardedAndPrivacySafe()
    {
        var target = TargetDescriptor.Create(
            WorkspaceRoot.Create("C:\\target-parent").Value,
            Path("project"),
            null).Value;
        var validFinalization = FinalizationReceipt.Create(
            target,
            $"sha256:{new string('a', 64)}");
        var invalidFinalization = FinalizationReceipt.Create(target, "raw tree listing");
        var validReport = ReportWriteReceipt.Create(
            Path("run-1\\generation-report.json"),
            Path("run-1\\generation-report.md"));
        var sameReport = ReportWriteReceipt.Create(
            Path("run-1\\generation-report.json"),
            Path("run-1\\generation-report.json"));
        var cleanup = StagingCleanupReceipt.Create("run-1", "marker-1");

        Assert.True(validFinalization.IsValid);
        Assert.False(invalidFinalization.IsValid);
        Assert.True(validReport.IsValid);
        Assert.False(sameReport.IsValid);
        Assert.True(cleanup.IsValid);
    }

    [Fact]
    public void EvidenceAndHandlerResultsRejectRawOrMalformedOutputEvidence()
    {
        var invalidEvidence = ExecutionEvidence.Create(
            ExecutionEvidenceKind.Step,
            "build",
            ExecutionEvidenceStatus.Passed,
            $"sha256:{new string('A', 64)}");
        var invalidResult = ExecutionHandlerResult.Create(
            ExecutionPhase.Execute,
            ExecutionHandlerOutcome.Succeeded,
            0,
            "raw process output",
            null,
            []);

        Assert.False(invalidEvidence.IsValid);
        Assert.False(invalidResult.IsValid);
        Assert.DoesNotContain(
            typeof(ExecutionHandlerResult).GetProperties(),
            property => property.Name.Contains("Output", StringComparison.OrdinalIgnoreCase)
                && property.Name != nameof(ExecutionHandlerResult.OutputDigest));
    }

    [Fact]
    public void HandlerRequestCarriesTheHashedPlanThatOwnsItsStepAndTemplateContext()
    {
        var planProperty = typeof(ExecutionHandlerRequest).GetProperty(
            nameof(ExecutionHandlerRequest.Plan));
        var handlerProperty = typeof(ExecutionHandlerRequest).GetProperty(
            nameof(ExecutionHandlerRequest.HandlerId));
        var inputProperty = typeof(ExecutionHandlerRequest).GetProperty(
            nameof(ExecutionHandlerRequest.Inputs));
        var factories = typeof(ExecutionHandlerRequest).GetMethods()
            .Where(method => method.Name == nameof(ExecutionHandlerRequest.Create))
            .ToArray();

        Assert.NotNull(planProperty);
        Assert.Equal(typeof(ExecutionPlan), planProperty.PropertyType);
        Assert.NotNull(handlerProperty);
        Assert.NotNull(inputProperty);
        Assert.Equal(2, factories.Length);
        Assert.All(
            factories,
            factory =>
            {
                Assert.Contains(
                    factory.GetParameters(),
                    parameter => parameter.ParameterType == typeof(ExecutionPlan));
                Assert.DoesNotContain(
                    factory.GetParameters(),
                    parameter => parameter.ParameterType.IsGenericType
                        && parameter.ParameterType.GetGenericTypeDefinition() == typeof(IEnumerable<>));
            });
        Assert.Contains(
            factories,
            factory => factory.GetParameters().Any(
                parameter => parameter.ParameterType == typeof(ExecutionValidator)));
    }

    [Fact]
    public void RecoverableExecutionPortsAreAsyncCancellableAndOrchestratorUsesRequestCheckpoint()
    {
        Type[] ports =
        [
            typeof(IRunCheckpointStore),
            typeof(IStagingWorkspaceManager),
            typeof(IBlueprintExecutionSource),
            typeof(IProjectFinalizer),
            typeof(IGenerationReportWriter),
            typeof(IExecutionHandler),
            typeof(IExecutionHandlerRegistryProvider),
            typeof(IRunRecoveryService),
        ];

        foreach (var method in ports.SelectMany(port => port.GetMethods()).Where(IsAsyncOperation))
        {
            Assert.Equal(typeof(CancellationToken), method.GetParameters()[^1].ParameterType);
        }

        var execute = typeof(IExecutionOrchestrator).GetMethod(nameof(IExecutionOrchestrator.ExecuteAsync));
        Assert.NotNull(execute);
        Assert.Equal(typeof(Task<RunCheckpoint>), execute.ReturnType);
        Assert.Equal(typeof(ExecutionRequest), execute.GetParameters()[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), execute.GetParameters()[^1].ParameterType);

        var createStaging = typeof(IStagingWorkspaceManager).GetMethod(
            nameof(IStagingWorkspaceManager.CreateAsync));
        Assert.NotNull(createStaging);
        Assert.Equal(
            typeof(Task<ExecutionOperationResult<IStagingWorkspaceLease>>),
            createStaging.ReturnType);
        Assert.True(typeof(IAsyncDisposable).IsAssignableFrom(typeof(IStagingWorkspaceLease)));

        var replayStaging = typeof(IStagingWorkspaceManager).GetMethod(
            nameof(IStagingWorkspaceManager.RecreateForReplayAsync));
        Assert.NotNull(replayStaging);
        Assert.Equal(
            typeof(Task<ExecutionOperationResult<IStagingWorkspaceLease>>),
            replayStaging.ReturnType);
    }

    [Fact]
    public void NewSnapshotContractsAreImmutableAndDoNotExposeAbsolutePathsOrSensitiveData()
    {
        Type[] snapshots =
        [
            typeof(ExecutionRequest),
            typeof(StagingDescriptor),
            typeof(TargetDescriptor),
            typeof(RunArtifactDescriptor),
            typeof(ExecutionEvidence),
            typeof(RunCheckpoint),
            typeof(ExecutionHandlerResult),
            typeof(FinalizationReceipt),
            typeof(ReportWriteReceipt),
            typeof(StagingCleanupReceipt),
        ];

        Assert.All(
            snapshots.SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)),
            property => Assert.Null(property.SetMethod));
        Assert.DoesNotContain(
            snapshots.SelectMany(type => type.GetProperties()),
            property => _forbiddenPropertyFragments
                .Any(fragment => Normalize(property.Name).Contains(fragment, StringComparison.Ordinal)));
    }

    private static ExecutionRequest CreateExecutionRequest()
    {
        var components = CreatePlanningComponents();
        var planned = PlannedProject.Create(
            components.Plan,
            components.Preview,
            components.Fingerprint).Value;
        var run = ProjectRun.Create("run-1", "recipe-1").Value;
        return ExecutionRequest.Create(
            planned,
            run,
            new StubWorkspace("C:\\target-parent"),
            Path("project"),
            new StubWorkspace("C:\\run-artifacts"),
            ExecutionMode.Fresh).Value;
    }

    private static StagingDescriptor CreateStaging()
    {
        return StagingDescriptor.Create(
            Path(".devforge-staging\\run-1"),
            Path(".devforge-staging\\run-1\\payload"),
            Path(".devforge-staging\\run-1\\ownership.json"),
            "marker-1").Value;
    }

    private static (ExecutionPlan Plan, PlanPreview Preview, BlueprintFingerprint Fingerprint)
        CreatePlanningComponents()
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
        var reference = BlueprintReference.Create("desktop.csharp-wpf-tool", "1.0.0").Value;
        var preview = PlanPreview.Create(
            reference,
            [new PlanPreviewStep("create", "create-directory", TimeSpan.FromSeconds(30))],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            GitOptions.Create().Value,
            CompletionOptions.Create().Value,
            hash).Value;
        var fingerprint = BlueprintFingerprint.Create(
            "built-in",
            Path("desktop.csharp-wpf-tool\\1.0.0"),
            BlueprintTrust.BuiltIn,
            $"sha256:{new string('2', 64)}").Value;
        return (plan, preview, fingerprint);
    }

    private static bool IsAsyncOperation(MethodInfo method)
    {
        return typeof(Task).IsAssignableFrom(method.ReturnType);
    }

    private static WorkspaceRelativePath Path(string value) => WorkspaceRelativePath.Create(value).Value;

    private static string Normalize(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private sealed class SingleUseEnumerable<T>(IEnumerable<T> values) : IEnumerable<T>
    {
        private readonly IEnumerable<T> _values = values;

        public int EnumerationCount { get; private set; }

        public IEnumerator<T> GetEnumerator()
        {
            EnumerationCount++;
            if (EnumerationCount > 1)
            {
                throw new InvalidOperationException("The sequence was enumerated more than once.");
            }

            return _values.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class StubWorkspace(string root) : IWorkspaceFileSystem
    {
        public WorkspaceRoot Root { get; } = WorkspaceRoot.Create(root).Value;

        public Task<bool> FileExistsAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> DirectoryExistsAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task CreateDirectoryAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Stream> OpenReadAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Stream> OpenWriteAsync(
            WorkspaceRelativePath path,
            bool overwrite,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DeleteFileAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateAllFilesAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateRootDirectoriesAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateFilesAsync(
            WorkspaceRelativePath directory,
            bool recursive,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateDirectoriesAsync(
            WorkspaceRelativePath directory,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DeleteDirectoryAsync(
            WorkspaceRelativePath path,
            DirectoryCleanupIntent intent,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task MoveDirectoryAsync(
            WorkspaceRelativePath source,
            WorkspaceRelativePath destination,
            WorkspaceMoveIntent intent,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
