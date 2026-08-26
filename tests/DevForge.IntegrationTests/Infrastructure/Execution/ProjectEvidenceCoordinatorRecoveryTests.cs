using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DevForge.Application.Contracts;
using DevForge.Application.Execution;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Execution;
using DevForge.Domain.Privacy;
using DevForge.Domain.Projects;
using DevForge.Domain.Reports;
using DevForge.Domain.Runs;
using DevForge.Domain.Validation;
using DevForge.Infrastructure.Execution;
using DevForge.Infrastructure.FileSystem;

namespace DevForge.IntegrationTests.Infrastructure.Execution;

[Collection(ExecutionRecoveryActivityTestGroup.Name)]
public sealed class ProjectEvidenceCoordinatorRecoveryTests
{
    private static readonly WorkspaceRelativePath[] _evidencePaths =
    [
        Path(@".devforge\project.recipe.yaml"),
        Path("devforge.lock.json"),
        Path("generation-report.json"),
        Path("policy.snapshot.json"),
    ];

    [Fact]
    public async Task ResumeThroughCoordinatorRecoversEveryAtomicEvidenceKillWindowByteIdentically()
    {
        var rootPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "DevForge.ProjectEvidenceCoordinatorRecoveryTests",
            Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(rootPath);
        try
        {
            var expected = await ExecuteCleanAsync(System.IO.Path.Combine(rootPath, "expected"));
            for (var killAfterWrites = 0; killAfterWrites <= 4; killAfterWrites++)
            {
                var scenarioRoot = System.IO.Path.Combine(rootPath, $"write-{killAfterWrites}");
                var fixture = await Fixture.CreateAsync(scenarioRoot, killAfterWrites, cancelInFinalizer: false);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Coordinator.CompleteAsync(
                    fixture.Request,
                    fixture.Checkpoint,
                    fixture.Staging,
                    fixture.Package,
                    fixture.Registry,
                    progress: null,
                    CancellationToken.None));

                var recovered = await fixture.ResumeWithCleanBoundariesAsync();

                Assert.Equal(RunStatus.LocalReady, recovered.Run.Status);
                Assert.Equal(expected, await fixture.ReadEvidenceAsync());
            }

            var intentFixture = await Fixture.CreateAsync(
                System.IO.Path.Combine(rootPath, "intent"),
                killAfterWrites: null,
                cancelInFinalizer: true);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                intentFixture.Coordinator.CompleteAsync(
                    intentFixture.Request,
                    intentFixture.Checkpoint,
                    intentFixture.Staging,
                    intentFixture.Package,
                    intentFixture.Registry,
                    progress: null,
                    CancellationToken.None));
            Assert.Equal(FinalizationState.IntentPersisted, intentFixture.Store.Last!.FinalizationState);

            var intentRecovered = await intentFixture.ResumeWithCleanBoundariesAsync();

            Assert.Equal(RunStatus.LocalReady, intentRecovered.Run.Status);
            Assert.Equal(expected, await intentFixture.ReadEvidenceAsync());
            using var report = JsonDocument.Parse(
                intentFixture.EvidenceBytes["generation-report.json"]);
            Assert.Equal(
                "1.0.0",
                Assert.Single(report.RootElement.GetProperty("toolStatuses").EnumerateArray())
                    .GetProperty("detectedVersion").GetString());
            Assert.Equal(
                "persisted.warning",
                Assert.Single(report.RootElement.GetProperty("warnings").EnumerateArray())
                    .GetProperty("code").GetString());
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    private static async Task<ImmutableDictionary<string, string>> ExecuteCleanAsync(string root)
    {
        var fixture = await Fixture.CreateAsync(root, killAfterWrites: null, cancelInFinalizer: false);
        var result = await fixture.Coordinator.CompleteAsync(
            fixture.Request,
            fixture.Checkpoint,
            fixture.Staging,
            fixture.Package,
            fixture.Registry,
            progress: null,
            CancellationToken.None);
        Assert.Equal(RunStatus.LocalReady, result.Run.Status);
        return await fixture.ReadEvidenceAsync();
    }

    private sealed class Fixture
    {
        private readonly IAtomicFileWorkspaceFileSystem _payload;
        private readonly FixedTimeProvider _timeProvider;

        private Fixture(
            ExecutionRequest request,
            RunCheckpoint checkpoint,
            StagingWorkspace staging,
            BlueprintExecutionPackage package,
            IExecutionHandlerRegistry registry,
            ValidatedRunCompletionCoordinator coordinator,
            Store store,
            IAtomicFileWorkspaceFileSystem payload,
            FixedTimeProvider timeProvider)
        {
            Request = request;
            Checkpoint = checkpoint;
            Staging = staging;
            Package = package;
            Registry = registry;
            Coordinator = coordinator;
            Store = store;
            _payload = payload;
            _timeProvider = timeProvider;
        }

        public ExecutionRequest Request { get; }
        public RunCheckpoint Checkpoint { get; }
        public StagingWorkspace Staging { get; }
        public BlueprintExecutionPackage Package { get; }
        public IExecutionHandlerRegistry Registry { get; }
        public ValidatedRunCompletionCoordinator Coordinator { get; }
        public Store Store { get; }
        public Dictionary<string, byte[]> EvidenceBytes { get; } = new(StringComparer.Ordinal);

        public static async Task<Fixture> CreateAsync(
            string root,
            int? killAfterWrites,
            bool cancelInFinalizer)
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(System.IO.Path.Combine(root, "payload"));
            Directory.CreateDirectory(System.IO.Path.Combine(root, "target"));
            Directory.CreateDirectory(System.IO.Path.Combine(root, "artifacts"));
            var fileSystem = new WindowsFileSystem();
            var payload = Assert.IsAssignableFrom<IAtomicFileWorkspaceFileSystem>(
                await fileSystem.OpenWorkspaceAsync(
                    WorkspaceRoot.Create(System.IO.Path.Combine(root, "payload")).Value,
                    CancellationToken.None));
            var target = await fileSystem.OpenWorkspaceAsync(
                WorkspaceRoot.Create(System.IO.Path.Combine(root, "target")).Value,
                CancellationToken.None);
            var artifacts = await fileSystem.OpenWorkspaceAsync(
                WorkspaceRoot.Create(System.IO.Path.Combine(root, "artifacts")).Value,
                CancellationToken.None);
            await payload.CreateDirectoryAsync(Path("src"), CancellationToken.None);
            await payload.WriteFileAtomicallyAsync(
                Path(@"src\App.csproj"),
                "<Project />"u8.ToArray(),
                overwrite: false,
                CancellationToken.None);

            var blueprint = BlueprintReference.Create("desktop.csharp-wpf-tool", "1.0.0").Value;
            var fingerprint = BlueprintFingerprint.Create(
                "built-in",
                Path(@"desktop.csharp-wpf-tool\1.0.0"),
                BlueprintTrust.BuiltIn,
                $"sha256:{new string('2', 64)}").Value;
            var hash = $"sha256:{new string('1', 64)}";
            var plan = ExecutionPlan.Create(
                hash,
                [],
                [],
                [
                    KeyValuePair.Create<string, string?>("project.name", "project"),
                    KeyValuePair.Create<string, string?>("team.snapshot_status", "none"),
                    KeyValuePair.Create<string, string?>("engine.version", "1.0.0"),
                ]).Value;
            var tool = new ToolRequirement("dotnet", ">=10.0.0", true);
            var persistedPreview = Preview(blueprint, hash, tool, "1.0.0", "persisted.warning");
            var changedRequestPreview = Preview(blueprint, hash, tool, "9.9.9", "request.warning");
            var run = ProjectRun.Create("run-recovery", "recipe-1").Value
                .TransitionTo(RunStatus.Planning).Value
                .TransitionTo(RunStatus.Executing).Value;
            var planned = PlannedProject.Create(plan, changedRequestPreview, fingerprint).Value;
            var request = ExecutionRequest.Create(
                planned,
                run,
                target,
                Path("project"),
                artifacts,
                ExecutionMode.Resume).Value;
            var descriptor = StagingDescriptor.Create(
                Path(@".devforge-staging\run-recovery"),
                Path(@".devforge-staging\run-recovery\payload"),
                Path(@".devforge-staging\run-recovery\ownership.json"),
                "run-recovery").Value;
            var checkpoint = RunCheckpoint.Create(
                run,
                plan,
                persistedPreview,
                blueprint,
                fingerprint,
                descriptor,
                TargetDescriptor.Create(target.Root, Path("project"), null).Value,
                RunArtifactDescriptor.Create(artifacts.Root).Value,
                [],
                FinalizationState.NotStarted,
                ReportPersistenceState.NotStarted).Value;
            var manifest = BlueprintManifest.Create(
                new BlueprintManifestDraft(
                    blueprint.Id,
                    blueprint.Version,
                    ">=1.0.0 <2.0.0",
                    [], [], [], [], [],
                    Artifacts: [new BlueprintArtifact(@"src\App.csproj")]),
                new BlueprintTrustAssignment(BlueprintTrust.BuiltIn)).Value;
            var package = BlueprintExecutionPackage.Create(
                ResolvedBlueprint.Create(manifest, [], fingerprint).Value,
                payload).Value;
            var store = new Store();
            var timeProvider = new FixedTimeProvider(DateTimeOffset.UnixEpoch.AddDays(1));
            IWorkspaceFileSystem evidenceWorkspace = killAfterWrites is null
                ? payload
                : new CancellingAtomicWorkspace(payload, killAfterWrites.Value);
            var staging = StagingWorkspace.Create(descriptor, evidenceWorkspace).Value;
            var coordinator = CreateCoordinator(
                store,
                cancelInFinalizer,
                timeProvider);
            return new Fixture(
                request,
                checkpoint,
                staging,
                package,
                new EmptyRegistry(),
                coordinator,
                store,
                payload,
                timeProvider);
        }

        public async Task<RunCheckpoint> ResumeWithCleanBoundariesAsync()
        {
            var interrupted = Store.Last!;
            var resumedRun = interrupted.Run.Status == RunStatus.Executing
                ? interrupted.Run
                : interrupted.Run.ResumeExecution().Value;
            var resumed = RunCheckpoint.Create(
                resumedRun,
                interrupted.Plan,
                interrupted.Preview,
                interrupted.Blueprint,
                interrupted.BlueprintFingerprint,
                interrupted.Staging,
                interrupted.Target,
                interrupted.RunArtifacts,
                interrupted.Evidence,
                interrupted.FinalizationState,
                interrupted.ReportState).Value;
            var cleanStaging = StagingWorkspace.Create(interrupted.Staging, _payload).Value;
            var cleanCoordinator = CreateCoordinator(Store, cancelInFinalizer: false, _timeProvider);
            return await cleanCoordinator.CompleteAsync(
                Request,
                resumed,
                cleanStaging,
                Package,
                Registry,
                progress: null,
                CancellationToken.None);
        }

        public async Task<ImmutableDictionary<string, string>> ReadEvidenceAsync()
        {
            EvidenceBytes.Clear();
            foreach (var path in _evidencePaths)
            {
                await using var input = await _payload.OpenReadAsync(path, CancellationToken.None);
                using var memory = new MemoryStream();
                await input.CopyToAsync(memory, CancellationToken.None);
                EvidenceBytes.Add(path.Value, memory.ToArray());
            }

            return EvidenceBytes.ToImmutableDictionary(
                item => item.Key,
                item => $"{Convert.ToHexString(item.Value)}|sha256:"
                    + Convert.ToHexStringLower(SHA256.HashData(item.Value)),
                StringComparer.Ordinal);
        }

        private static PlanPreview Preview(
            BlueprintReference blueprint,
            string hash,
            ToolRequirement tool,
            string version,
            string warningCode) => PlanPreview.Create(
                blueprint,
                [], [],
                [tool],
                [new PlanPreviewToolStatus(tool.Id, tool.VersionRange, true, true, true, version)],
                [],
                [new BlueprintArtifact(@"src\App.csproj")],
                [new ValidationIssue(warningCode, warningCode == "persisted.warning"
                    ? "Persisted warning."
                    : "Changed request warning.", "preview")],
                [], [],
                GitOptions.Create().Value,
                CompletionOptions.Create().Value,
                hash).Value;

        private static ValidatedRunCompletionCoordinator CreateCoordinator(
            Store store,
            bool cancelInFinalizer,
            TimeProvider timeProvider) => new(
                store,
                new EmptyScanner(),
                new Finalizer(cancelInFinalizer),
                new CanonicalProjectEvidenceWriter(),
                new ReportWriter(),
                timeProvider);
    }

    private sealed class Store : IRunCheckpointStore
    {
        public RunCheckpoint? Last { get; private set; }
        public Task SaveAsync(RunCheckpoint checkpoint, CancellationToken cancellationToken)
        {
            Last = checkpoint;
            return Task.CompletedTask;
        }
        public Task<RunCheckpoint?> FindAsync(string runId, CancellationToken cancellationToken) =>
            Task.FromResult(Last);
        public Task<ImmutableArray<RunCheckpoint>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Last is null ? ImmutableArray<RunCheckpoint>.Empty : [Last]);
    }

    private sealed class EmptyScanner : ISecretScanner
    {
        public Task<SecretScanResult> ScanAsync(
            SecretScanRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(SecretScanResult.Create([]).Value);
    }

    private sealed class Finalizer(bool cancel) : IProjectFinalizer
    {
        public Task<ExecutionOperationResult<FinalizationReceipt>> FinalizeAsync(
            RunCheckpoint checkpoint,
            StagingWorkspace staging,
            IWorkspaceFileSystem targetParentWorkspace,
            CancellationToken cancellationToken)
        {
            if (cancel)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            return Task.FromResult(ExecutionOperationResult.Success(
                FinalizationReceipt.Create(
                    checkpoint.Target,
                    $"sha256:{new string('3', 64)}").Value));
        }
    }

    private sealed class ReportWriter : IGenerationReportWriter
    {
        public Task<ExecutionOperationResult<ReportWriteReceipt>> WriteAsync(
            RunCheckpoint checkpoint,
            GenerationReport report,
            IWorkspaceFileSystem runArtifactWorkspace,
            CancellationToken cancellationToken) => Task.FromResult(
                ExecutionOperationResult.Success(
                    ReportWriteReceipt.Create(Path("run.json"), Path("run.md")).Value));
    }

    private sealed class EmptyRegistry : IExecutionHandlerRegistry
    {
        public IExecutionHandler? Resolve(string handlerId) => null;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class CancellingAtomicWorkspace(
        IAtomicFileWorkspaceFileSystem inner,
        int killAfterWrites) : IAtomicFileWorkspaceFileSystem
    {
        private int _writes;
        public WorkspaceRoot Root => inner.Root;

        public async Task WriteFileAtomicallyAsync(
            WorkspaceRelativePath path,
            ReadOnlyMemory<byte> content,
            bool overwrite,
            CancellationToken cancellationToken)
        {
            if (_writes == killAfterWrites)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            await inner.WriteFileAtomicallyAsync(path, content, overwrite, cancellationToken);
            _writes++;
            if (_writes == killAfterWrites)
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }

        public Task<bool> FileExistsAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => inner.FileExistsAsync(path, cancellationToken);
        public Task<bool> DirectoryExistsAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => inner.DirectoryExistsAsync(path, cancellationToken);
        public Task CreateDirectoryAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => inner.CreateDirectoryAsync(path, cancellationToken);
        public Task<Stream> OpenReadAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => inner.OpenReadAsync(path, cancellationToken);
        public Task<Stream> OpenWriteAsync(WorkspaceRelativePath path, bool overwrite, CancellationToken cancellationToken) => inner.OpenWriteAsync(path, overwrite, cancellationToken);
        public Task DeleteFileAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => inner.DeleteFileAsync(path, cancellationToken);
        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateAllFilesAsync(CancellationToken cancellationToken) => inner.EnumerateAllFilesAsync(cancellationToken);
        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateRootDirectoriesAsync(CancellationToken cancellationToken) => inner.EnumerateRootDirectoriesAsync(cancellationToken);
        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateFilesAsync(WorkspaceRelativePath directory, bool recursive, CancellationToken cancellationToken) => inner.EnumerateFilesAsync(directory, recursive, cancellationToken);
        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateDirectoriesAsync(WorkspaceRelativePath directory, CancellationToken cancellationToken) => inner.EnumerateDirectoriesAsync(directory, cancellationToken);
        public Task DeleteDirectoryAsync(WorkspaceRelativePath path, DirectoryCleanupIntent intent, CancellationToken cancellationToken) => inner.DeleteDirectoryAsync(path, intent, cancellationToken);
        public Task MoveDirectoryAsync(WorkspaceRelativePath source, WorkspaceRelativePath destination, WorkspaceMoveIntent intent, CancellationToken cancellationToken) => inner.MoveDirectoryAsync(source, destination, intent, cancellationToken);
    }

    private static WorkspaceRelativePath Path(string value) => WorkspaceRelativePath.Create(value).Value;
}
