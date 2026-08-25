using System.Collections.Immutable;
using System.IO;
using DevForge.Application.Contracts;
using DevForge.Application.Contracts.Persistence;
using DevForge.Application.Creation;
using DevForge.Application.Execution;
using DevForge.Application.Planning;
using DevForge.Application.Planning.CompatibilityRules;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Blueprints.BuiltIn;
using DevForge.Domain.Environment;
using DevForge.Infrastructure.Blueprints;
using DevForge.Infrastructure.Creation;
using DevForge.Infrastructure.Execution;
using DevForge.Infrastructure.FileSystem;
using DevForge.Infrastructure.Persistence;
using DevForge.Infrastructure.Persistence.Migrations;
using DevForge.Infrastructure.Persistence.Repositories;
using DevForge.Infrastructure.Security;

namespace DevForge.E2ETests.M9;

internal sealed class WpfBlueprintFixture : IAsyncDisposable
{
    private readonly string _root;

    private WpfBlueprintFixture(
        string root,
        string targetRoot,
        ProjectCreationWorkflow workflow,
        RecordingProcessRunner runner)
    {
        _root = root;
        TargetPath = Path.Combine(targetRoot, "team-tool");
        Workflow = workflow;
        Runner = runner;
        Draft = ProjectCreationDraft.Create(
            "Team Tool",
            targetRoot,
            "team-tool",
            BlueprintReference.Create("desktop.csharp-wpf-tool", "1.0.0").Value,
            new Dictionary<string, DynamicInputValue?>(),
            [],
            "none").Value;
    }

    public ProjectCreationWorkflow Workflow { get; }

    public RecordingProcessRunner Runner { get; }

    public ProjectCreationDraft Draft { get; }

    public string TargetPath { get; }

    public static async Task<WpfBlueprintFixture> CreateAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "DevForge-M9-Wpf-E2E-" + Guid.NewGuid().ToString("N"));
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
                [new EnvironmentTool("dotnet", "10.0.302", true)],
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
            var runner = new RecordingProcessRunner();
            var completion = new ValidatedRunCompletionCoordinator(
                checkpointStore,
                new WorkspaceSecretScanner(),
                new AtomicProjectFinalizer(),
                new CanonicalGenerationReportWriter(),
                TimeProvider.System);
            var orchestrator = new CheckpointedExecutionOrchestrator(
                checkpointStore,
                staging,
                blueprintSource,
                new ClosedExecutionHandlerRegistryProvider(runner),
                completion,
                TimeProvider.System);
            var workflow = new ProjectCreationWorkflow(
                catalog,
                planner,
                targets,
                targets,
                new GuidRunIdentityGenerator(),
                orchestrator,
                TimeProvider.System);
            return new WpfBlueprintFixture(root, targetRoot, workflow, runner);
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
            || !Path.GetFileName(fullPath).StartsWith("DevForge-M9-Wpf-E2E-", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Refusing to clean an unexpected M9 fixture path.");
        }

        if (Directory.Exists(fullPath))
        {
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

    internal sealed class RecordingProcessRunner : IProcessRunner
    {
        private readonly List<CommandSpec> _commands = [];

        public ImmutableArray<CommandSpec> Commands => [.. _commands];

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
            return Task.FromResult(ProcessResult.Create(ProcessTerminationReason.Exited, 0, []).Value);
        }
    }
}
