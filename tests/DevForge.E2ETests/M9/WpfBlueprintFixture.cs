using System.Collections.Immutable;
using System.IO;
using DevForge.Application.Contracts;
using DevForge.Application.Contracts.Persistence;
using DevForge.Application.Creation;
using DevForge.Application.Execution;
using DevForge.Application.Planning;
using DevForge.Application.Planning.CompatibilityRules;
using DevForge.Application.Publication;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Blueprints.BuiltIn;
using DevForge.Domain.Environment;
using DevForge.Domain.Projects;
using DevForge.Infrastructure.Blueprints;
using DevForge.Infrastructure.Creation;
using DevForge.Infrastructure.Execution;
using DevForge.Infrastructure.FileSystem;
using DevForge.Infrastructure.Git;
using DevForge.Infrastructure.Persistence;
using DevForge.Infrastructure.Persistence.Migrations;
using DevForge.Infrastructure.Persistence.Repositories;
using DevForge.Infrastructure.Processes;
using DevForge.Infrastructure.Publication;
using DevForge.Infrastructure.Security;

namespace DevForge.E2ETests.M9;

internal sealed class WpfBlueprintFixture : IAsyncDisposable
{
    private readonly string _root;
    private readonly string _targetRoot;
    private readonly IProjectRecoveryWorkspaceFactory _recoveryWorkspaces;
    private readonly IStagingWorkspaceManager _staging;
    private readonly IRunCheckpointStore _checkpointStore;
    private readonly IExecutionOrchestrator _orchestrator;
    private readonly TimeProvider _timeProvider;
    private readonly IProjectPublicationCoordinator _publication;

    private WpfBlueprintFixture(
        string root,
        string targetRoot,
        ProjectCreationWorkflow workflow,
        RecordingProcessRunner runner,
        BlueprintCatalog catalog,
        IProjectRecoveryWorkspaceFactory recoveryWorkspaces,
        IStagingWorkspaceManager staging,
        IRunCheckpointStore checkpointStore,
        IExecutionOrchestrator orchestrator,
        TimeProvider timeProvider,
        IProjectPublicationCoordinator publication,
        RejectingRemoteGitHubService remoteGitHub,
        string projectName,
        string targetName,
        string blueprintId)
    {
        _root = root;
        _targetRoot = targetRoot;
        _recoveryWorkspaces = recoveryWorkspaces;
        _staging = staging;
        _checkpointStore = checkpointStore;
        _orchestrator = orchestrator;
        _timeProvider = timeProvider;
        _publication = publication;
        TargetPath = Path.Combine(targetRoot, targetName);
        Workflow = workflow;
        Runner = runner;
        Catalog = catalog;
        RemoteGitHub = remoteGitHub;
        Draft = ProjectCreationDraft.Create(
            projectName,
            targetRoot,
            targetName,
            BlueprintReference.Create(blueprintId, "1.0.0").Value,
            new Dictionary<string, DynamicInputValue?>(),
            [],
            "none").Value;
    }

    public ProjectCreationWorkflow Workflow { get; }

    public RecordingProcessRunner Runner { get; }

    public BlueprintCatalog Catalog { get; }

    public RejectingRemoteGitHubService RemoteGitHub { get; }

    public ProjectCreationDraft Draft { get; }

    public string TargetPath { get; }

    public ProjectCreationDraft CreateDraft(string projectName, string targetName) =>
        ProjectCreationDraft.Create(
            projectName,
            _targetRoot,
            targetName,
            Draft.Blueprint,
            Draft.Inputs.Select(item =>
                new KeyValuePair<string, DynamicInputValue?>(item.Key, item.Value)),
            Draft.Features,
            Draft.IdeId,
            Draft.Git.InitializeRepository,
            Draft.Git.BranchPolicy,
            Draft.Git.PublishToGitHub,
            Draft.Git.IsPrivate,
            Draft.Git.GitHubAccount,
            Draft.Git.GitHubRepository).Value;

    public async Task<StagingCleanupReceipt> CleanupFailedAsync(RunCheckpoint checkpoint)
    {
        var workspaces = await _recoveryWorkspaces.OpenAsync(checkpoint, CancellationToken.None);
        var recovery = new RunRecoveryService(
            _checkpointStore,
            _orchestrator,
            _staging,
            _timeProvider);
        var result = await recovery.CleanupAsync(
            checkpoint,
            workspaces.TargetParent,
            CancellationToken.None);
        Assert.True(result.IsSuccessful);
        return result.Value;
    }

    public Task<ExecutionOperationResult<RunCheckpoint>> PublishLocalAsync(string runId) =>
        _publication.PublishAsync(
            PublicationRequest.Create(runId, PublicationMutationMode.Normal).Value,
            CancellationToken.None);

    public static Task<WpfBlueprintFixture> CreateAsync() => CreateAsync(
        "Team Tool",
        "team-tool",
        "desktop.csharp-wpf-tool",
        [new EnvironmentTool("dotnet", "10.0.302", true)]);

    public static Task<WpfBlueprintFixture> CreateReactAsync() => CreateAsync(
        "Team Portal",
        "team-portal",
        "web.react-vite-ts",
        [
            new EnvironmentTool("node", "22.21.1", true),
            new EnvironmentTool("pnpm", "10.24.0", true),
        ]);

    public static Task<WpfBlueprintFixture> CreatePythonAsync() => CreateAsync(
        "Team Tool",
        "team-tool",
        "tool.python-cli",
        [
            new EnvironmentTool("python", "3.14.6", true),
            new EnvironmentTool("uv", "0.12.1", true),
        ]);

    private static async Task<WpfBlueprintFixture> CreateAsync(
        string projectName,
        string targetName,
        string blueprintId,
        IReadOnlyCollection<EnvironmentTool> tools)
    {
        var root = Path.Combine(Path.GetTempPath(), "DevForge-M9-Blueprint-E2E-" + Guid.NewGuid().ToString("N"));
        var targetRoot = Path.Combine(root, "projects");
        var localDataRoot = Path.Combine(root, "local-data");
        Directory.CreateDirectory(targetRoot);
        Directory.CreateDirectory(localDataRoot);

        try
        {
            var fileSystem = new WindowsFileSystem();
            var sourceRoot = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                BuiltInBlueprintCatalog.OutputDirectory));
            var sourceWorkspace = await fileSystem.OpenWorkspaceAsync(
                WorkspaceRoot.Create(sourceRoot).Value,
                CancellationToken.None);
            var source = BlueprintPackageSource.Create(
                BuiltInBlueprintCatalog.SourceId,
                sourceWorkspace,
                BlueprintSourceProvenance.BuiltIn).Value;
            var location = DatabaseLocation.Create(localDataRoot, "devforge.db").Value;
            await new EfDatabaseMigrationExecutor().MigrateAsync(location, CancellationToken.None);
            var dbFactory = new DevForgeDbContextFactory(location);
            var metadata = new BlueprintMetadataStore(dbFactory);
            var catalog = new BlueprintCatalog([source], metadata);
            await catalog.RefreshAsync(CancellationToken.None);
            var environment = EnvironmentSnapshot.Create(
                DateTimeOffset.UtcNow,
                tools,
                []).Value;
            var planner = new ProjectPlanner(
                catalog,
                new FixedEnvironmentDoctor(environment),
                new FixedRuntimeProvider(PlanningRuntimeContext.Create("1.0.0", "windows", "x64").Value),
                new InputSchemaValidator(),
                new CompatibilityRuleEvaluator(),
                new VariableTemplateResolver());
            var localDataWorkspaceRoot = WorkspaceRoot.Create(localDataRoot).Value;
            var targets = new WindowsProjectTargetService(fileSystem, localDataWorkspaceRoot);
            var checkpointStore = new SqliteRunCheckpointStore(dbFactory);
            var staging = new OwnedStagingWorkspaceManager(fileSystem);
            var blueprintSource = new BlueprintExecutionSource([source], metadata);
            var runner = new RecordingProcessRunner(targetRoot, blueprintId);
            var timeProvider = new FixedTimeProvider();
            var completion = new ValidatedRunCompletionCoordinator(
                checkpointStore,
                new WorkspaceSecretScanner(),
                new AtomicProjectFinalizer(),
                new CanonicalProjectEvidenceWriter(),
                new CanonicalGenerationReportWriter(),
                timeProvider);
            var orchestrator = new CheckpointedExecutionOrchestrator(
                checkpointStore,
                staging,
                blueprintSource,
                new ClosedExecutionHandlerRegistryProvider(runner),
                completion,
                timeProvider);
            var workflow = new ProjectCreationWorkflow(
                catalog,
                planner,
                targets,
                targets,
                new GuidRunIdentityGenerator(),
                orchestrator,
                timeProvider);
            var remoteGitHub = new RejectingRemoteGitHubService();
            var publication = new ProjectPublicationCoordinator(
                checkpointStore,
                new WindowsPublicationLeaseProvider(fileSystem, localDataWorkspaceRoot),
                new ProjectPublicationWorkspaceFactory(targets),
                new LocalGitService(new WindowsProcessRunner(), new WorkspaceSecretScanner()),
                remoteGitHub,
                new AtomicPublicationReceiptStore(),
                new FixedNonceGenerator());
            return new WpfBlueprintFixture(
                root,
                targetRoot,
                workflow,
                runner,
                catalog,
                targets,
                staging,
                checkpointStore,
                orchestrator,
                timeProvider,
                publication,
                remoteGitHub,
                projectName,
                targetName,
                blueprintId);
        }
        catch
        {
            DeleteRoot(root);
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        DeleteRoot(_root);
        return ValueTask.CompletedTask;
    }

    private static void DeleteRoot(string root)
    {
        var fullPath = Path.GetFullPath(root);
        if (!fullPath.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(fullPath).StartsWith("DevForge-M9-Blueprint-E2E-", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Refusing to clean an unexpected M9 fixture path.");
        }

        if (Directory.Exists(fullPath))
        {
            foreach (var file in Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories))
            {
                var attributes = File.GetAttributes(file);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
                }
            }

            Directory.Delete(fullPath, recursive: true);
        }
    }

    private sealed class FixedEnvironmentDoctor(EnvironmentSnapshot snapshot) : IEnvironmentDoctor
    {
        public Task<EnvironmentSnapshot> InspectAsync(CancellationToken cancellationToken) =>
            Task.FromResult(snapshot);
    }

    private sealed class FixedRuntimeProvider(PlanningRuntimeContext context) : IPlanningRuntimeContextProvider
    {
        public PlanningRuntimeContext GetCurrent() => context;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch;
    }

    internal sealed class RecordingProcessRunner(string targetRoot, string blueprintId) : IProcessRunner
    {
        private readonly List<CommandSpec> _commands = [];
        private bool _failNext;

        public ImmutableArray<CommandSpec> Commands => [.. _commands];

        public void FailNext() => _failNext = true;

        public Task CheckPreconditionsAsync(CommandSpec command, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<ProcessResult> RunAsync(
            CommandSpec command,
            IProgress<ProcessOutputLine>? progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _commands.Add(command);
            if (_failNext)
            {
                _failNext = false;
                return Task.FromResult(ProcessResult.Create(
                    ProcessTerminationReason.Exited,
                    1,
                    []).Value);
            }

            if (blueprintId == "web.react-vite-ts"
                && command.ArgumentList.SequenceEqual(["run", "build"]))
            {
                var stagingRoot = Path.Combine(targetRoot, ".devforge-staging");
                var payload = Directory.EnumerateDirectories(stagingRoot)
                    .Select(path => Path.Combine(path, "payload"))
                    .Single(Directory.Exists);
                var dist = Path.Combine(payload, "dist");
                Directory.CreateDirectory(dist);
                File.WriteAllText(Path.Combine(dist, "index.html"), "<!doctype html><title>Team Portal</title>\n");
            }
            return Task.FromResult(ProcessResult.Create(ProcessTerminationReason.Exited, 0, []).Value);
        }
    }

    internal sealed class RejectingRemoteGitHubService : IPublicationGitHubService
    {
        public int Calls { get; private set; }

        public Task<GitHubPublishResult> PublishAsync(
            GitHubPublishRequest request,
            IGitHubPublicationProgress progress,
            CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("The M9 local-Git matrix must not contact a remote.");
        }

        public Task<GitHubPublishResult> VerifyAsync(
            GitHubPublishRequest request,
            CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("The M9 local-Git matrix must not contact a remote.");
        }
    }

    private sealed class FixedNonceGenerator : IPublicationNonceGenerator
    {
        public string CreateOwnershipNonce() => new('a', 32);
    }
}
