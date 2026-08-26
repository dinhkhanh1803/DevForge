using System.Collections.Immutable;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DevForge.Application.Contracts;
using DevForge.Application.Contracts.Persistence;
using DevForge.Application.Creation;
using DevForge.Application.Execution;
using DevForge.Application.Planning;
using DevForge.Application.Planning.CompatibilityRules;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Environment;
using DevForge.Infrastructure.Blueprints;
using DevForge.Infrastructure.Creation;
using DevForge.Infrastructure.Execution;
using DevForge.Infrastructure.FileSystem;
using DevForge.Infrastructure.Persistence;
using DevForge.Infrastructure.Persistence.Migrations;
using DevForge.Infrastructure.Persistence.Repositories;
using DevForge.Infrastructure.Processes;
using DevForge.Infrastructure.Security;

namespace DevForge.E2ETests.M7;

internal sealed class M7BlueprintFixture : IAsyncDisposable
{
    private const string BlueprintId = "m7.test.local";
    private const string BlueprintVersion = "1.0.0";
    private const string PackageDirectoryName = "m7.test.local";

    private readonly string _root;
    private readonly CancellableRegistryProvider _registryProvider;

    private M7BlueprintFixture(
        string root,
        string targetRoot,
        string localDataRoot,
        ProjectCreationWorkflow workflow,
        ProjectRecoveryWorkflow recoveryWorkflow,
        IRunCheckpointStore checkpointStore,
        CancellableRegistryProvider registryProvider)
    {
        _root = root;
        TargetRoot = targetRoot;
        LocalDataRoot = localDataRoot;
        Workflow = workflow;
        RecoveryWorkflow = recoveryWorkflow;
        CheckpointStore = checkpointStore;
        _registryProvider = registryProvider;
        ValidDraft = ProjectCreationDraft.Create(
            "M7 Sample",
            targetRoot,
            "m7-sample",
            BlueprintReference.Create(BlueprintId, BlueprintVersion).Value,
            new Dictionary<string, DynamicInputValue?>
            {
                ["project-title"] = DynamicInputValue.Text("M7 Sample").Value,
                ["framework"] = DynamicInputValue.Text("net10.0").Value,
                ["include-sample"] = DynamicInputValue.Boolean(true).Value,
                ["service-port"] = DynamicInputValue.WholeNumber(5080).Value,
            },
            [],
            "none").Value;
    }

    public ProjectCreationWorkflow Workflow { get; }

    public ProjectRecoveryWorkflow RecoveryWorkflow { get; }

    public IRunCheckpointStore CheckpointStore { get; }

    public ProjectCreationDraft ValidDraft { get; }

    public string TargetRoot { get; }

    public string LocalDataRoot { get; }

    public string TargetPath => Path.Combine(TargetRoot, "m7-sample");

    public static async Task<M7BlueprintFixture> CreateAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "DevForge-M7-E2E-" + Guid.NewGuid().ToString("N"));
        var sourceRoot = Path.Combine(root, "blueprints");
        var targetRoot = Path.Combine(root, "projects");
        var localDataRoot = Path.Combine(root, "local-data");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(targetRoot);
        Directory.CreateDirectory(localDataRoot);

        try
        {
            var fileSystem = new WindowsFileSystem();
            var sourceWorkspace = await fileSystem.OpenWorkspaceAsync(
            WorkspaceRoot.Create(sourceRoot).Value,
            CancellationToken.None);
            var source = BlueprintPackageSource.Create(
                "m7-e2e-local",
                sourceWorkspace,
                BlueprintSourceProvenance.Local).Value;
            var packageDirectory = WorkspaceRelativePath.Create(PackageDirectoryName).Value;
            var aggregateChecksum = await WritePackageAsync(sourceWorkspace, packageDirectory);

            var location = DatabaseLocation.Create(localDataRoot, "devforge.db").Value;
            await new EfDatabaseMigrationExecutor().MigrateAsync(location, CancellationToken.None);
            var dbFactory = new DevForgeDbContextFactory(location);
            var metadata = new BlueprintMetadataStore(dbFactory);
            await metadata.UpsertAsync(
                BlueprintMetadataRecord.Create(
                    BlueprintId,
                    BlueprintVersion,
                    BlueprintSource.Local,
                    BlueprintTrust.TrustedLocal,
                    aggregateChecksum,
                    isDisabled: false,
                    DateTimeOffset.UtcNow).Value,
                CancellationToken.None);

            var catalog = new DevForge.Infrastructure.Blueprints.BlueprintCatalog([source], metadata);
            await catalog.RefreshAsync(CancellationToken.None);
            var catalogSnapshot = await catalog.InspectAsync(CancellationToken.None);
            if (catalogSnapshot.ExecutableBlueprints.Length != 1)
            {
                var details = string.Join(
                    "; ",
                    catalogSnapshot.Inspections.Select(item =>
                        $"{item.Trust}:{string.Join(',', item.Issues.Select(issue => issue.Code))}"));
                throw new InvalidOperationException($"The M7 test blueprint is unavailable: {details}");
            }
            var environment = new FixedEnvironmentDoctor(EnvironmentSnapshot.Create(
                DateTimeOffset.UtcNow,
                [],
                []).Value);
            var runtime = new FixedRuntimeProvider(
                PlanningRuntimeContext.Create("1.0.0", "windows", "x64").Value);
            var planner = new ProjectPlanner(
                catalog,
                environment,
                runtime,
                new InputSchemaValidator(),
                new CompatibilityRuleEvaluator(),
                new VariableTemplateResolver());
            var localDataWorkspaceRoot = WorkspaceRoot.Create(localDataRoot).Value;
            var targets = new WindowsProjectTargetService(fileSystem, localDataWorkspaceRoot);
            var checkpointStore = new SqliteRunCheckpointStore(dbFactory);
            var staging = new OwnedStagingWorkspaceManager(fileSystem);
            var blueprintSource = new BlueprintExecutionSource([source], metadata);
            var registryProvider = new CancellableRegistryProvider(
                new ClosedExecutionHandlerRegistryProvider(new WindowsProcessRunner()));
            var completion = new ValidatedRunCompletionCoordinator(
                checkpointStore,
                new WorkspaceSecretScanner(),
                new AtomicProjectFinalizer(),
                new CanonicalProjectEvidenceWriter(),
                new CanonicalGenerationReportWriter(),
                TimeProvider.System);
            var orchestrator = new CheckpointedExecutionOrchestrator(
                checkpointStore,
                staging,
                blueprintSource,
                registryProvider,
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
            var recovery = new RunRecoveryService(
                checkpointStore,
                orchestrator,
                staging,
                TimeProvider.System);
            var recoveryWorkflow = new ProjectRecoveryWorkflow(
                checkpointStore,
                targets,
                staging,
                recovery,
                new BlueprintRecoveryInspector(blueprintSource));
            return new M7BlueprintFixture(
                root,
                targetRoot,
                localDataRoot,
                workflow,
                recoveryWorkflow,
                checkpointStore,
                registryProvider);
        }
        catch
        {
            DeleteFixtureRoot(root);
            throw;
        }
    }

    public void CancelBeforeHandler(string handlerId, CancellationTokenSource source) =>
        _registryProvider.Arm(handlerId, source);

    public string JsonReportPath(string runId) => Path.Combine(
        LocalDataRoot,
        "runs",
        runId,
        "reports",
        $"{runId}.json");

    public string MarkdownReportPath(string runId) => Path.Combine(
        LocalDataRoot,
        "runs",
        runId,
        "reports",
        $"{runId}.md");

    public async ValueTask DisposeAsync()
    {
        await Task.Yield();
        DeleteFixtureRoot(_root);
    }

    private static void DeleteFixtureRoot(string root)
    {
        var fullPath = Path.GetFullPath(root);
        if (!fullPath.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(fullPath).StartsWith("DevForge-M7-E2E-", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Refusing to clean an unexpected M7 fixture path.");
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

    private static async Task<string> WritePackageAsync(
        IWorkspaceFileSystem workspace,
        WorkspaceRelativePath packageDirectory)
    {
        await workspace.CreateDirectoryAsync(packageDirectory, CancellationToken.None);
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["manifest.yaml"] = Encoding.UTF8.GetBytes(Manifest),
            ["inputs.schema.json"] = Encoding.UTF8.GetBytes(InputSchema),
            ["rules.yaml"] = Encoding.UTF8.GetBytes("[]\n"),
            ["templates/README.md"] = Encoding.UTF8.GetBytes(
                "# {{ project.name }}\n\nFramework: {{ recipe.input.framework }}\n"),
            ["overlays/base/src/Program.txt"] = Encoding.UTF8.GetBytes("safe local content\n"),
        };
        foreach (var file in files)
        {
            await WriteAsync(workspace, packageDirectory, file.Key, file.Value);
        }

        var checksums = files.ToDictionary(
            item => item.Key,
            item => Convert.ToHexStringLower(SHA256.HashData(item.Value)),
            StringComparer.Ordinal);
        await WriteAsync(
            workspace,
            packageDirectory,
            "checksums.json",
            JsonSerializer.SerializeToUtf8Bytes(checksums));
        using var aggregate = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var checksum in checksums.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            aggregate.AppendData(Encoding.UTF8.GetBytes(checksum.Key));
            aggregate.AppendData([0]);
            aggregate.AppendData(Encoding.UTF8.GetBytes(checksum.Value));
            aggregate.AppendData([(byte)'\n']);
        }

        return Convert.ToHexStringLower(aggregate.GetHashAndReset());
    }

    private static async Task WriteAsync(
        IWorkspaceFileSystem workspace,
        WorkspaceRelativePath packageDirectory,
        string path,
        byte[] content)
    {
        var relative = WorkspaceRelativePath.Create(
            packageDirectory.Value + "\\" + path.Replace('/', '\\')).Value;
        var separator = relative.Value.LastIndexOf('\\');
        if (separator > 0)
        {
            await workspace.CreateDirectoryAsync(
                WorkspaceRelativePath.Create(relative.Value[..separator]).Value,
                CancellationToken.None);
        }

        await using var stream = await workspace.OpenWriteAsync(
            relative,
            overwrite: false,
            CancellationToken.None);
        await stream.WriteAsync(content);
    }

    private const string Manifest = """
        id: m7.test.local
        name: M7 Test Blueprint
        version: 1.0.0
        engineVersion: ">=1.0.0 <2.0.0"
        tools: []
        features: []
        actions:
          - id: create-source
            handler: create-directory
            timeoutSeconds: 30
            parameters:
              path: src
          - id: render-readme
            handler: render-template
            timeoutSeconds: 30
            parameters:
              source: templates\README.md
              target: README.md
          - id: copy-source
            handler: copy-overlay
            timeoutSeconds: 30
            parameters:
              source: overlays\base\src
              target: src
        validators:
          - id: readme-exists
            handler: validate-file-exists
            timeoutSeconds: 30
            required: true
            parameters:
              path: README.md
          - id: readme-content
            handler: validate-file-content
            timeoutSeconds: 30
            required: true
            parameters:
              path: README.md
              contains: "Framework: net10.0"
        artifacts:
          - path: README.md
          - path: src\Program.txt
        dependencies: []
        """;

    private const string InputSchema = """
        {
          "type": "object",
          "properties": {
            "project-title": { "type": "string", "minLength": 1, "maxLength": 80 },
            "framework": { "type": "string", "enum": ["net10.0", "net11.0"] },
            "include-sample": { "type": "boolean" },
            "service-port": { "type": "integer", "minimum": 1024, "maximum": 65535 }
          },
          "required": ["project-title", "framework", "include-sample", "service-port"],
          "additionalProperties": false
        }
        """;

    private sealed class FixedEnvironmentDoctor(EnvironmentSnapshot snapshot) : IEnvironmentDoctor
    {
        public Task<EnvironmentSnapshot> InspectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(snapshot);
        }
    }

    private sealed class FixedRuntimeProvider(PlanningRuntimeContext context)
        : IPlanningRuntimeContextProvider
    {
        public PlanningRuntimeContext GetCurrent() => context;
    }

    private sealed class CancellableRegistryProvider(IExecutionHandlerRegistryProvider inner)
        : IExecutionHandlerRegistryProvider
    {
        private readonly object _sync = new();
        private string? _handlerId;
        private CancellationTokenSource? _source;

        public void Arm(string handlerId, CancellationTokenSource source)
        {
            lock (_sync)
            {
                _handlerId = handlerId;
                _source = source;
            }
        }

        public ExecutionOperationResult<IExecutionHandlerRegistry> Create(BlueprintTrust trust)
        {
            var result = inner.Create(trust);
            return result.IsSuccessful
                ? ExecutionOperationResult.Success<IExecutionHandlerRegistry>(
                    new CancellableRegistry(result.Value, this))
                : ExecutionOperationResult.Failure<IExecutionHandlerRegistry>(result.Error!);
        }

        private bool TryCancel(string handlerId)
        {
            CancellationTokenSource? source;
            lock (_sync)
            {
                if (!StringComparer.Ordinal.Equals(_handlerId, handlerId) || _source is null)
                {
                    return false;
                }

                source = _source;
                _handlerId = null;
                _source = null;
            }

            source.Cancel();
            return true;
        }

        private sealed class CancellableRegistry(
            IExecutionHandlerRegistry inner,
            CancellableRegistryProvider owner) : IExecutionHandlerRegistry
        {
            public IExecutionHandler? Resolve(string handlerId)
            {
                var handler = inner.Resolve(handlerId);
                return handler is null ? null : new CancellableHandler(handler, owner);
            }
        }

        private sealed class CancellableHandler(
            IExecutionHandler inner,
            CancellableRegistryProvider owner) : IExecutionHandler
        {
            public string Id => inner.Id;

            public ExecutionResumeBehavior ResumeBehavior => inner.ResumeBehavior;

            public Task<ExecutionHandlerResult> PrepareAsync(
                ExecutionHandlerRequest request,
                CancellationToken cancellationToken)
            {
                if (owner.TryCancel(Id))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                return inner.PrepareAsync(request, cancellationToken);
            }

            public Task<ExecutionHandlerResult> CheckPreconditionsAsync(
                ExecutionHandlerRequest request,
                CancellationToken cancellationToken) =>
                inner.CheckPreconditionsAsync(request, cancellationToken);

            public Task<ExecutionHandlerResult> ExecuteAsync(
                ExecutionHandlerRequest request,
                IProgress<ExecutionProgressLine>? progress,
                CancellationToken cancellationToken) =>
                inner.ExecuteAsync(request, progress, cancellationToken);

            public Task<ExecutionHandlerResult> CheckPostconditionsAsync(
                ExecutionHandlerRequest request,
                CancellationToken cancellationToken) =>
                inner.CheckPostconditionsAsync(request, cancellationToken);

            public Task<ExecutionHandlerResult> CleanupForRetryAsync(
                ExecutionHandlerRequest request,
                CancellationToken cancellationToken) =>
                inner.CleanupForRetryAsync(request, cancellationToken);
        }
    }
}
