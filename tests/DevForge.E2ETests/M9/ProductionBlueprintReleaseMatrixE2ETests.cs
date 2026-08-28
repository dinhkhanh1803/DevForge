using System.Collections.Immutable;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DevForge.Application.Contracts;
using DevForge.Application.Contracts.Persistence;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Desktop.Bootstrap;
using DevForge.Domain.Projects;
using DevForge.Domain.Runs;
using DevForge.Infrastructure.Blueprints;
using DevForge.Infrastructure.FileSystem;
using DevForge.Infrastructure.Persistence;
using DevForge.Infrastructure.Persistence.Migrations;
using DevForge.Infrastructure.Persistence.Repositories;

namespace DevForge.E2ETests.M9;

[Collection(M9ExecutionTestGroup.Name)]
public sealed partial class ProductionBlueprintReleaseMatrixE2ETests
{
    private static readonly string[] _controlFileNames =
        ["manifest.yaml", "rules.yaml", "inputs.schema.json"];
    private static readonly string[] _approvedActionHandlers =
        ["copy-overlay", "package-install", "render-template"];
    private static readonly ProductionBlueprintCase[] _cases =
    [
        new(
            "desktop.csharp-wpf-tool",
            WpfBlueprintFixture.CreateAsync,
            "1965e539bf77cf214b5bac0031b42de2cf06684bfacbc19c7a668b1944ed6669",
            [
                ".devforge/project.recipe.yaml",
                ".editorconfig",
                ".gitignore",
                "ARCHITECTURE.md",
                "CONTRIBUTING.md",
                "DEPLOYMENT.md",
                "DEVELOPMENT.md",
                "Directory.Build.props",
                "Directory.Packages.props",
                "README.md",
                "TEAM_START_HERE.md",
                "TESTING.md",
                "TeamTool.slnx",
                "devforge.lock.json",
                "generation-report.json",
                "global.json",
                "policy.snapshot.json",
                "src/TeamTool.Application/IStatusService.cs",
                "src/TeamTool.Application/TeamTool.Application.csproj",
                "src/TeamTool.Application/packages.lock.json",
                "src/TeamTool.Desktop/App.xaml",
                "src/TeamTool.Desktop/App.xaml.cs",
                "src/TeamTool.Desktop/MainViewModel.cs",
                "src/TeamTool.Desktop/MainWindow.xaml",
                "src/TeamTool.Desktop/MainWindow.xaml.cs",
                "src/TeamTool.Desktop/Properties/PublishProfiles/WindowsSmoke.pubxml",
                "src/TeamTool.Desktop/TeamTool.Desktop.csproj",
                "src/TeamTool.Desktop/appsettings.json",
                "src/TeamTool.Desktop/packages.lock.json",
                "src/TeamTool.Domain/TeamTool.Domain.csproj",
                "src/TeamTool.Domain/ToolStatus.cs",
                "src/TeamTool.Domain/packages.lock.json",
                "src/TeamTool.Infrastructure/StatusService.cs",
                "src/TeamTool.Infrastructure/TeamTool.Infrastructure.csproj",
                "src/TeamTool.Infrastructure/packages.lock.json",
                "tests/TeamTool.UnitTests/StatusContractTests.cs",
                "tests/TeamTool.UnitTests/TeamTool.UnitTests.csproj",
                "tests/TeamTool.UnitTests/packages.lock.json",
            ],
            [
                Operation("restore", "package-install", "dotnet", 300, false,
                    "restore", "TeamTool.slnx", "--locked-mode"),
                Operation("format", "validate-command", "dotnet", 180, true,
                    "format", "TeamTool.slnx", "--verify-no-changes", "--no-restore"),
                Operation("build", "validate-command", "dotnet", 300, true,
                    "build", "TeamTool.slnx", "--configuration", "Release", "--no-restore"),
                Operation("test", "validate-command", "dotnet", 300, true,
                    "test", "TeamTool.slnx", "--configuration", "Release", "--no-build", "--no-restore"),
                Operation("publish-smoke", "validate-command", "dotnet", 300, true,
                    "publish", "src\\TeamTool.Desktop\\TeamTool.Desktop.csproj", "--configuration",
                    "Release", "--no-restore", "--property:PublishProfile=WindowsSmoke"),
            ]),
        new(
            "tool.python-cli",
            WpfBlueprintFixture.CreatePythonAsync,
            "b7ed822c6e08f3ca3aca60df63f92dffd6d4d822a4f10e4cc2268639f7694b4b",
            [
                ".devforge/project.recipe.yaml",
                ".editorconfig",
                ".env.example",
                ".gitignore",
                ".python-version",
                "ARCHITECTURE.md",
                "CONTRIBUTING.md",
                "DEPLOYMENT.md",
                "DEVELOPMENT.md",
                "README.md",
                "TEAM_START_HERE.md",
                "TESTING.md",
                "devforge.lock.json",
                "generation-report.json",
                "policy.snapshot.json",
                "pyproject.toml",
                "src/team_tool/__init__.py",
                "src/team_tool/__main__.py",
                "src/team_tool/cli.py",
                "src/team_tool/config.py",
                "src/team_tool/logging_config.py",
                "src/team_tool/py.typed",
                "tests/test_cli.py",
                "tests/test_config.py",
                "uv.lock",
            ],
            [
                Operation("install", "package-install", "uv", 900, false,
                    "sync", "--frozen", "--no-config"),
                Operation("format", "validate-command", "uv", 300, true,
                    "run", "--frozen", "--no-sync", "--no-config", "ruff", "format", "--check", "."),
                Operation("lint", "validate-command", "uv", 300, true,
                    "run", "--frozen", "--no-sync", "--no-config", "ruff", "check", "."),
                Operation("typecheck", "validate-command", "uv", 300, true,
                    "run", "--frozen", "--no-sync", "--no-config", "mypy", "src", "tests"),
                Operation("test", "validate-command", "uv", 300, true,
                    "run", "--frozen", "--no-sync", "--no-config", "pytest"),
                Operation("build", "validate-command", "uv", 300, true,
                    "run", "--frozen", "--no-sync", "--no-config", "pyproject-build", "--no-isolation"),
                Operation("cli-smoke", "validate-command", "uv", 300, true,
                    "run", "--frozen", "--no-sync", "--no-config", "team-tool", "--help"),
            ]),
        new(
            "web.react-vite-ts",
            WpfBlueprintFixture.CreateReactAsync,
            "d271288038f0e7fa5f794872a6192788850e060d134a1a320ecbc270e85f42fc",
            [
                ".devforge/project.recipe.yaml",
                ".editorconfig",
                ".env.example",
                ".gitignore",
                ".prettierignore",
                ".prettierrc.json",
                "ARCHITECTURE.md",
                "CONTRIBUTING.md",
                "DEPLOYMENT.md",
                "DEVELOPMENT.md",
                "README.md",
                "TEAM_START_HERE.md",
                "TESTING.md",
                "devforge.lock.json",
                "dist/index.html",
                "eslint.config.js",
                "generation-report.json",
                "index.html",
                "package.json",
                "pnpm-lock.yaml",
                "policy.snapshot.json",
                "src/app/App.test.tsx",
                "src/app/App.tsx",
                "src/app/index.css",
                "src/config/env.test.ts",
                "src/config/env.ts",
                "src/main.tsx",
                "src/services/apiClient.ts",
                "src/test/setup.ts",
                "src/vite-env.d.ts",
                "tsconfig.app.json",
                "tsconfig.json",
                "tsconfig.node.json",
                "vite.config.ts",
            ],
            [
                Operation("install", "package-install", "pnpm", 900, false,
                    "install", "--frozen-lockfile", "--ignore-scripts"),
                Operation("lint", "validate-command", "pnpm", 300, true, "run", "lint"),
                Operation("typecheck", "validate-command", "pnpm", 300, true, "run", "typecheck"),
                Operation("test", "validate-command", "pnpm", 300, true, "run", "test"),
                Operation("build", "validate-command", "pnpm", 300, true, "run", "build"),
            ]),
    ];

    public static TheoryData<string> ProductionBlueprintIds
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var item in _cases)
            {
                data.Add(item.Id);
            }

            return data;
        }
    }

    [Fact]
    public async Task DesktopProductionCatalogDiscoversExactlyThreeBuiltInBlueprints()
    {
        var localDataRoot = Path.Combine(
            Path.GetTempPath(),
            "DevForge-M9-Desktop-Catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(localDataRoot);
        try
        {
            var location = DatabaseLocation.Create(localDataRoot, "devforge.db").Value;
            await new EfDatabaseMigrationExecutor().MigrateAsync(location, CancellationToken.None);
            var fileSystem = new WindowsFileSystem();
            var registry = new DesktopBlueprintSourceRegistry(
                location,
                fileSystem,
                BuiltInBlueprintPackageLocation.Create(AppContext.BaseDirectory));
            await registry.InitializeAsync(CancellationToken.None);
            using var catalog = new BlueprintCatalog(
                registry,
                new BlueprintMetadataStore(new DevForgeDbContextFactory(location)));
            await catalog.RefreshAsync(CancellationToken.None);

            var blueprints = await catalog.ListAsync(CancellationToken.None);

            Assert.Equal(_cases.Select(item => item.Id),
                blueprints.Select(item => item.Manifest.Id).Order(StringComparer.Ordinal));
            Assert.All(blueprints, blueprint =>
            {
                Assert.Equal("1.0.0", blueprint.Manifest.Version);
                Assert.Equal(BlueprintTrust.BuiltIn, blueprint.Manifest.Trust);
                Assert.Equal("built-in", blueprint.Fingerprint.SourceId);
            });
            Assert.Collection(
                registry,
                source => Assert.Equal(BlueprintSourceProvenance.BuiltIn, source.Provenance),
                source => Assert.Equal(BlueprintSourceProvenance.Local, source.Provenance));
        }
        finally
        {
            Directory.Delete(localDataRoot, recursive: true);
        }
    }

    [Theory]
    [MemberData(nameof(ProductionBlueprintIds))]
    public async Task ReviewedBlueprintCompletesWithAnExactDeterministicPlanAndFinalTree(
        string blueprintId)
    {
        var blueprintCase = Case(blueprintId);
        await using var first = await CreateFixtureAsync(blueprintId);
        await using var second = await CreateFixtureAsync(blueprintId);
        var firstPlan = await first.Workflow.CreatePlanAsync(first.Draft, CancellationToken.None);
        var secondPlan = await second.Workflow.CreatePlanAsync(second.Draft, CancellationToken.None);
        Assert.True(firstPlan.IsValid);
        Assert.True(secondPlan.IsValid);
        Assert.Equal(firstPlan.Value.PlannedProject.Plan.Id, secondPlan.Value.PlannedProject.Plan.Id);

        var firstRun = await first.Workflow.ExecuteAsync(firstPlan.Value, null, CancellationToken.None);
        var secondRun = await second.Workflow.ExecuteAsync(secondPlan.Value, null, CancellationToken.None);

        Assert.True(firstRun.IsValid);
        Assert.True(secondRun.IsValid);
        Assert.Equal(RunStatus.LocalReady, firstRun.Value.Checkpoint.Run.Status);
        Assert.Equal(RunStatus.LocalReady, secondRun.Value.Checkpoint.Run.Status);
        Assert.Equal(blueprintCase.Paths, RelativePaths(first.TargetPath));
        Assert.Equal(blueprintCase.Paths, RelativePaths(second.TargetPath));
        Assert.True(blueprintCase.Digest == TreeDigest(first.TargetPath), "Actual reviewed tree digest: " + TreeDigest(first.TargetPath));
        Assert.Equal(blueprintCase.Digest, TreeDigest(second.TargetPath));
        AssertCommands(blueprintCase, first.Runner.Commands);
        AssertCommands(blueprintCase, second.Runner.Commands);
        Assert.False(Directory.Exists(Path.Combine(first.TargetPath, ".git")));
    }

    [Theory]
    [MemberData(nameof(ProductionBlueprintIds))]
    public async Task DifferentReviewedProjectNameChangesPlanHashAndRenderedOutput(string blueprintId)
    {
        await using var baseline = await CreateFixtureAsync(blueprintId);
        await using var changed = await CreateFixtureAsync(blueprintId);
        var changedDraft = changed.CreateDraft("Alternate Project", changed.Draft.OutputFolder);
        Assert.NotEqual(baseline.Draft.RootPath, changedDraft.RootPath);
        Assert.Equal(baseline.Draft.OutputFolder, changedDraft.OutputFolder);
        Assert.Equal(baseline.Draft.Blueprint, changedDraft.Blueprint);
        Assert.Equal(baseline.Draft.Inputs, changedDraft.Inputs);
        Assert.Equal(baseline.Draft.Features, changedDraft.Features);
        Assert.Equal(baseline.Draft.IdeId, changedDraft.IdeId);
        AssertGitIntentEqual(baseline.Draft.Git, changedDraft.Git);
        var baselinePlan = await baseline.Workflow.CreatePlanAsync(baseline.Draft, CancellationToken.None);
        var changedPlan = await changed.Workflow.CreatePlanAsync(changedDraft, CancellationToken.None);
        Assert.True(baselinePlan.IsValid);
        Assert.True(changedPlan.IsValid);
        Assert.NotEqual(
            baselinePlan.Value.PlannedProject.Plan.Id,
            changedPlan.Value.PlannedProject.Plan.Id);

        var baselineRun = await baseline.Workflow.ExecuteAsync(
            baselinePlan.Value,
            null,
            CancellationToken.None);
        var changedRun = await changed.Workflow.ExecuteAsync(
            changedPlan.Value,
            null,
            CancellationToken.None);

        Assert.True(baselineRun.IsValid);
        Assert.True(changedRun.IsValid);
        Assert.NotEqual(TreeDigest(baseline.TargetPath), TreeDigest(changed.TargetPath));
    }

    [Fact]
    public async Task OccupiedFinalTargetIsNotOverwritten()
    {
        await using var fixture = await WpfBlueprintFixture.CreateAsync();
        var sentinel = Path.Combine(fixture.TargetPath, "keep.txt");
        Directory.CreateDirectory(fixture.TargetPath);
        await File.WriteAllTextAsync(sentinel, "owned-by-user");

        var plan = await fixture.Workflow.CreatePlanAsync(fixture.Draft, CancellationToken.None);

        Assert.False(plan.IsValid);
        Assert.Equal("owned-by-user", await File.ReadAllTextAsync(sentinel));
        Assert.Equal(["keep.txt"], RelativePaths(fixture.TargetPath));
    }

    [Fact]
    public async Task FailedExecutionKeepsFinalTargetAbsentAndOwnedStagingRecoverable()
    {
        await using var fixture = await WpfBlueprintFixture.CreateAsync();
        var plan = await fixture.Workflow.CreatePlanAsync(fixture.Draft, CancellationToken.None);
        Assert.True(plan.IsValid);
        fixture.Runner.FailNext();

        var failed = await fixture.Workflow.ExecuteAsync(plan.Value, null, CancellationToken.None);

        Assert.True(failed.IsValid);
        Assert.Equal(RunStatus.Failed, failed.Value.Checkpoint.Run.Status);
        Assert.False(Directory.Exists(fixture.TargetPath));

        var cleanup = await fixture.CleanupFailedAsync(failed.Value.Checkpoint);

        Assert.Equal(plan.Value.RunId, cleanup.RunId);
        Assert.False(Directory.Exists(fixture.TargetPath));
    }

    [Theory]
    [MemberData(nameof(ProductionBlueprintIds))]
    public async Task OptionalLocalGitPublishesTheExactGeneratedTreeCleanWithoutRemote(
        string blueprintId)
    {
        await using var fixture = await CreateFixtureAsync(blueprintId);
        var plan = await fixture.Workflow.CreatePlanAsync(fixture.Draft, CancellationToken.None);
        Assert.True(plan.IsValid);
        var generated = await fixture.Workflow.ExecuteAsync(plan.Value, null, CancellationToken.None);
        Assert.True(generated.IsValid);
        var expectedDigest = generated.Value.Checkpoint.Publication.FinalTreeDigest;

        var published = await fixture.PublishLocalAsync(plan.Value.RunId);
        var verified = await fixture.PublishLocalAsync(plan.Value.RunId);

        Assert.True(published.IsSuccessful);
        Assert.True(verified.IsSuccessful);
        Assert.Equal(RunStatus.Completed, verified.Value.Run.Status);
        Assert.Equal(expectedDigest, verified.Value.Publication.FinalTreeDigest);
        Assert.Equal(GitPublicationState.Succeeded, verified.Value.Publication.GitState);
        Assert.Equal(GitHubPublicationState.NotRequested, verified.Value.Publication.GitHubState);
        Assert.Equal(["main"], verified.Value.Publication.Branches.ToArray());
        Assert.Equal(0, fixture.RemoteGitHub.Calls);
        Assert.True(Directory.Exists(Path.Combine(fixture.TargetPath, ".git")));
    }

    [Fact]
    public async Task ProductionControlSurfacesContainNoForbiddenExecutionOrDependencyEscape()
    {
        await using var fixture = await WpfBlueprintFixture.CreateAsync();
        var blueprints = await fixture.Catalog.ListAsync(CancellationToken.None);
        Assert.All(blueprints, blueprint =>
        {
            var blueprintCase = Case(blueprint.Manifest.Id);
            Assert.All(blueprint.Manifest.Actions, action =>
            {
                Assert.Contains(action.HandlerId, _approvedActionHandlers);
                Assert.DoesNotMatch(ForbiddenSurface(), action.HandlerId);
                if (action.HandlerId is "copy-overlay" or "render-template")
                {
                    Assert.Equal(["source", "target"], action.Parameters.Keys.Order(StringComparer.Ordinal));
                }
            });
            var expectedAction = Assert.Single(blueprintCase.Operations, operation => !operation.IsValidator);
            var actualAction = Assert.Single(
                blueprint.Manifest.Actions,
                action => action.HandlerId == "package-install");
            AssertOperation(expectedAction, actualAction.Id, actualAction.HandlerId,
                actualAction.Parameters, actualAction.Timeout, isValidator: false);

            Assert.Equal(
                blueprintCase.Operations.Where(operation => operation.IsValidator).Select(operation => operation.Id),
                blueprint.Manifest.Validators.Select(validator => validator.Id));
            foreach (var expectedValidator in blueprintCase.Operations.Where(operation => operation.IsValidator))
            {
                var validator = Assert.Single(
                    blueprint.Manifest.Validators,
                    candidate => candidate.Id == expectedValidator.Id);
                AssertOperation(expectedValidator, validator.Id, validator.HandlerId,
                    validator.Parameters, validator.Timeout, isValidator: true);
                Assert.True(validator.Required);
            }

            Assert.All(blueprint.Manifest.Dependencies, dependency =>
            {
                Assert.DoesNotContain("*", dependency.Version, StringComparison.Ordinal);
                Assert.DoesNotContain("latest", dependency.Version, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotMatch(ForbiddenSurface(), dependency.Id);
            });
            Assert.DoesNotContain(blueprint.Manifest.Inputs, input =>
                ForbiddenInput().IsMatch(input.Id));

            var typedControlText = blueprint.Manifest.Actions
                .SelectMany(action => action.Parameters.SelectMany(parameter =>
                    Flatten(parameter.Key, parameter.Value)))
                .Concat(blueprint.Manifest.Validators.SelectMany(validator =>
                    validator.Parameters.SelectMany(parameter => Flatten(parameter.Key, parameter.Value))));
            Assert.All(typedControlText, value => Assert.DoesNotMatch(ForbiddenSurface(), value));
        });

        var root = FindRepositoryRoot();
        var controlFiles = _cases.Select(item => item.Id)
            .SelectMany(id => _controlFileNames
                .Select(name => Path.Combine(root, "blueprints", id, name)));
        foreach (var path in controlFiles)
        {
            Assert.DoesNotMatch(ForbiddenControlText(), await File.ReadAllTextAsync(path));
        }
    }

    private static Task<WpfBlueprintFixture> CreateFixtureAsync(string blueprintId) =>
        Case(blueprintId).CreateFixture();

    private static ProductionBlueprintCase Case(string blueprintId) => Assert.Single(
        _cases,
        item => StringComparer.Ordinal.Equals(item.Id, blueprintId));

    private static void AssertCommands(
        ProductionBlueprintCase blueprintCase,
        ImmutableArray<CommandSpec> actualCommands)
    {
        var expectedCommands = blueprintCase.Operations
            .SelectMany(operation => Enumerable.Repeat(operation, operation.IsValidator ? 2 : 1))
            .ToArray();
        Assert.Equal(expectedCommands.Length, actualCommands.Length);
        for (var index = 0; index < expectedCommands.Length; index++)
        {
            var expected = expectedCommands[index];
            var actual = actualCommands[index];
            Assert.Equal(Tool(expected.Tool), actual.Executable.Tool);
            Assert.Equal(expected.Arguments, actual.ArgumentList);
            Assert.True(actual.UsesWorkspaceRoot);
            Assert.Null(actual.WorkingDirectory);
            Assert.Equal(TimeSpan.FromSeconds(expected.TimeoutSeconds), actual.Timeout);
            Assert.Equal([0], actual.AllowedExitCodes.Order());
            if (actual.Executable.Tool == ExecutableTool.Uv)
            {
                Assert.Equal(["MYPY_CACHE_DIR", "PYTEST_ADDOPTS", "RUFF_NO_CACHE", "UV_PROJECT_ENVIRONMENT"],
                    actual.EnvironmentVariables.Keys.Order(StringComparer.Ordinal));
                Assert.All(actual.EnvironmentVariables.Values, value => Assert.Equal(ProcessValueSensitivity.Safe, value.Sensitivity));
            }
            else
            {
                Assert.Empty(actual.EnvironmentVariables);
            }
            Assert.Empty(actual.RedactionNeedles);
        }
    }

    private static void AssertOperation(
        ExpectedOperation expected,
        string actualId,
        string actualHandler,
        ImmutableDictionary<string, BlueprintValue> parameters,
        TimeSpan actualTimeout,
        bool isValidator)
    {
        Assert.Equal(expected.Id, actualId);
        Assert.Equal(expected.Handler, actualHandler);
        Assert.Equal(TimeSpan.FromSeconds(expected.TimeoutSeconds), actualTimeout);
        Assert.Equal(
            isValidator
                ? ["allowedExitCodes", "arguments", "executable", "required", "workingDirectory"]
                : ["arguments", "packageManager", "workingDirectory"],
            parameters.Keys.Order(StringComparer.Ordinal));
        var toolKey = isValidator ? "executable" : "packageManager";
        Assert.Equal(expected.Tool, parameters[toolKey].StringValue);
        Assert.Equal(
            expected.Arguments,
            parameters["arguments"].ArrayValue.Select(value => value.StringValue));
        Assert.Equal(".", parameters["workingDirectory"].StringValue);
        if (isValidator)
        {
            Assert.Equal([0L], parameters["allowedExitCodes"].ArrayValue.Select(value => value.IntegerValue));
            Assert.True(parameters["required"].BooleanValue);
        }
    }

    private static IEnumerable<string> Flatten(string key, BlueprintValue value)
    {
        yield return key;
        if (value.StringValue is not null)
        {
            yield return value.StringValue;
        }

        foreach (var item in value.ArrayValue)
        {
            foreach (var nested in Flatten(key, item))
            {
                yield return nested;
            }
        }

        foreach (var item in value.ObjectValue)
        {
            foreach (var nested in Flatten(item.Key, item.Value))
            {
                yield return nested;
            }
        }
    }

    private static void AssertGitIntentEqual(GitOptions expected, GitOptions actual)
    {
        Assert.Equal(expected.InitializeRepository, actual.InitializeRepository);
        Assert.Equal(expected.PrimaryBranch, actual.PrimaryBranch);
        Assert.Equal(expected.BranchPolicy, actual.BranchPolicy);
        Assert.Equal(expected.PublishToGitHub, actual.PublishToGitHub);
        Assert.Equal(expected.IsPrivate, actual.IsPrivate);
        Assert.Equal(expected.GitHubAccount, actual.GitHubAccount);
        Assert.Equal(expected.GitHubRepository, actual.GitHubRepository);
    }

    private static ExpectedOperation Operation(
        string id,
        string handler,
        string tool,
        int timeoutSeconds,
        bool isValidator,
        params string[] arguments) =>
        new(id, handler, tool, arguments, timeoutSeconds, isValidator);

    private static ExecutableTool Tool(string tool) => tool switch
    {
        "dotnet" => ExecutableTool.DotNet,
        "pnpm" => ExecutableTool.Pnpm,
        "uv" => ExecutableTool.Uv,
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, null),
    };

    private static string[] RelativePaths(string root) => Directory
        .EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static string TreeDigest(string root)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            hash.AppendData(Encoding.UTF8.GetBytes(relative));
            hash.AppendData([0]);
            hash.AppendData(File.ReadAllBytes(file));
            hash.AppendData([(byte)'\n']);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "DevForge.sln")))
        {
            current = current.Parent;
        }

        return current?.FullName
            ?? throw new InvalidOperationException("The DevForge repository root was not found.");
    }

    [GeneratedRegex("(?i)(token|secret|password|credential)")]
    private static partial Regex ForbiddenInput();

    [GeneratedRegex("(?i)(?:^|[^a-z0-9])(shell|cmd(?:\\.exe)?|powershell(?:\\.exe)?|bash|sh|sudo|runas|admin(?:istrator)?|latest|token|secret|password|credential|registry|index-url|extra-index-url|webview2|electron|aspnetcore|kestrel)(?:$|[^a-z0-9])|(?:^|[^a-z0-9])\\*(?:$|[^a-z0-9])")]
    private static partial Regex ForbiddenSurface();

    [GeneratedRegex("(?im)(cmd(?:\\.exe)?|powershell(?:\\.exe)?|\\bbash\\b|(?:^|\\s)sh(?:$|\\s)|\\bsudo\\b|\\brunas\\b|\\badmin(?:istrator)?\\b|--registry|--index-url|--extra-index-url|--token|\\bsecret\\b|--password|\\bcredential\\b|webview2|electron|aspnetcore|kestrel|(?:^|[^a-z])latest(?:[^a-z]|$)|(?:^|\\s)\\*(?:$|\\s))")]
    private static partial Regex ForbiddenControlText();

    private sealed record ProductionBlueprintCase(
        string Id,
        Func<Task<WpfBlueprintFixture>> CreateFixture,
        string Digest,
        string[] Paths,
        ExpectedOperation[] Operations);

    private sealed record ExpectedOperation(
        string Id,
        string Handler,
        string Tool,
        string[] Arguments,
        int TimeoutSeconds,
        bool IsValidator);
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class M9ExecutionTestGroup
{
    public const string Name = "M9 execution E2E";
}
