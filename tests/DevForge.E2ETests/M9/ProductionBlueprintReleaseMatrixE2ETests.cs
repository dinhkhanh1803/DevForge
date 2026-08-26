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
    private static readonly string[] _blueprintIds =
    [
        "desktop.csharp-wpf-tool",
        "tool.python-cli",
        "web.react-vite-ts",
    ];
    private static readonly string[] _controlFileNames =
        ["manifest.yaml", "rules.yaml", "inputs.schema.json"];
    private static readonly Dictionary<string, int> _expectedCommandCounts = new(StringComparer.Ordinal)
    {
        ["desktop.csharp-wpf-tool"] = 9,
        ["web.react-vite-ts"] = 9,
        ["tool.python-cli"] = 13,
    };
    private static readonly Dictionary<string, ExpectedTree> _expectedTrees =
        new Dictionary<string, ExpectedTree>(StringComparer.Ordinal)
        {
            ["desktop.csharp-wpf-tool"] = new(
                "1965e539bf77cf214b5bac0031b42de2cf06684bfacbc19c7a668b1944ed6669",
                Split(".devforge/project.recipe.yaml,.editorconfig,.gitignore,ARCHITECTURE.md,CONTRIBUTING.md,DEPLOYMENT.md,DEVELOPMENT.md,Directory.Build.props,Directory.Packages.props,README.md,TEAM_START_HERE.md,TESTING.md,TeamTool.slnx,devforge.lock.json,generation-report.json,global.json,policy.snapshot.json,src/TeamTool.Application/IStatusService.cs,src/TeamTool.Application/TeamTool.Application.csproj,src/TeamTool.Application/packages.lock.json,src/TeamTool.Desktop/App.xaml,src/TeamTool.Desktop/App.xaml.cs,src/TeamTool.Desktop/MainViewModel.cs,src/TeamTool.Desktop/MainWindow.xaml,src/TeamTool.Desktop/MainWindow.xaml.cs,src/TeamTool.Desktop/Properties/PublishProfiles/WindowsSmoke.pubxml,src/TeamTool.Desktop/TeamTool.Desktop.csproj,src/TeamTool.Desktop/appsettings.json,src/TeamTool.Desktop/packages.lock.json,src/TeamTool.Domain/TeamTool.Domain.csproj,src/TeamTool.Domain/ToolStatus.cs,src/TeamTool.Domain/packages.lock.json,src/TeamTool.Infrastructure/StatusService.cs,src/TeamTool.Infrastructure/TeamTool.Infrastructure.csproj,src/TeamTool.Infrastructure/packages.lock.json,tests/TeamTool.UnitTests/StatusContractTests.cs,tests/TeamTool.UnitTests/TeamTool.UnitTests.csproj,tests/TeamTool.UnitTests/packages.lock.json")),
            ["web.react-vite-ts"] = new(
                "392f85ace28fb15e1bc1815e2b1b55adf8ab406f99d09399b2e2f79adc4e7173",
                Split(".devforge/project.recipe.yaml,.editorconfig,.env.example,.gitignore,.prettierignore,.prettierrc.json,ARCHITECTURE.md,CONTRIBUTING.md,DEPLOYMENT.md,DEVELOPMENT.md,README.md,TEAM_START_HERE.md,TESTING.md,devforge.lock.json,dist/index.html,eslint.config.js,generation-report.json,index.html,package.json,pnpm-lock.yaml,policy.snapshot.json,src/app/App.test.tsx,src/app/App.tsx,src/app/index.css,src/config/env.test.ts,src/config/env.ts,src/main.tsx,src/services/apiClient.ts,src/test/setup.ts,src/vite-env.d.ts,tsconfig.app.json,tsconfig.json,tsconfig.node.json,vite.config.ts")),
            ["tool.python-cli"] = new(
                "b7ed822c6e08f3ca3aca60df63f92dffd6d4d822a4f10e4cc2268639f7694b4b",
                Split(".devforge/project.recipe.yaml,.editorconfig,.env.example,.gitignore,.python-version,ARCHITECTURE.md,CONTRIBUTING.md,DEPLOYMENT.md,DEVELOPMENT.md,README.md,TEAM_START_HERE.md,TESTING.md,devforge.lock.json,generation-report.json,policy.snapshot.json,pyproject.toml,src/team_tool/__init__.py,src/team_tool/__main__.py,src/team_tool/cli.py,src/team_tool/config.py,src/team_tool/logging_config.py,src/team_tool/py.typed,tests/test_cli.py,tests/test_config.py,uv.lock")),
        };

    public static TheoryData<string> ProductionBlueprintIds => new()
    {
        "desktop.csharp-wpf-tool",
        "web.react-vite-ts",
        "tool.python-cli",
    };

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

            Assert.Equal(_blueprintIds,
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
        var expected = _expectedTrees[blueprintId];
        Assert.Equal(expected.Paths, RelativePaths(first.TargetPath));
        Assert.Equal(expected.Paths, RelativePaths(second.TargetPath));
        Assert.Equal(expected.Digest, TreeDigest(first.TargetPath));
        Assert.Equal(expected.Digest, TreeDigest(second.TargetPath));
        Assert.Equal(_expectedCommandCounts[blueprintId], first.Runner.Commands.Length);
        Assert.All(first.Runner.Commands, command =>
            Assert.Equal(0, Assert.Single(command.AllowedExitCodes)));
        Assert.False(Directory.Exists(Path.Combine(first.TargetPath, ".git")));
    }

    [Theory]
    [MemberData(nameof(ProductionBlueprintIds))]
    public async Task DifferentReviewedProjectNameChangesPlanHashAndRenderedOutput(string blueprintId)
    {
        await using var baseline = await CreateFixtureAsync(blueprintId);
        await using var changed = await CreateFixtureAsync(blueprintId);
        var changedDraft = changed.CreateDraft("Alternate Project", "alternate-project");
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
        Assert.NotEqual(TreeDigest(baseline.TargetPath), TreeDigest(
            Path.Combine(Path.GetDirectoryName(changed.TargetPath)!, "alternate-project")));
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
            Assert.All(blueprint.Manifest.Actions,
                action => Assert.DoesNotMatch(ForbiddenHandler(), action.HandlerId));
            Assert.All(blueprint.Manifest.Validators,
                validator => Assert.DoesNotMatch(ForbiddenHandler(), validator.HandlerId));
            Assert.All(blueprint.Manifest.Dependencies, dependency =>
            {
                Assert.DoesNotContain("*", dependency.Version, StringComparison.Ordinal);
                Assert.DoesNotContain("latest", dependency.Version, StringComparison.OrdinalIgnoreCase);
            });
            Assert.DoesNotContain(blueprint.Manifest.Inputs, input =>
                ForbiddenInput().IsMatch(input.Id));
        });

        var root = FindRepositoryRoot();
        var controlFiles = _blueprintIds
            .SelectMany(id => _controlFileNames
                .Select(name => Path.Combine(root, "blueprints", id, name)));
        foreach (var path in controlFiles)
        {
            Assert.DoesNotMatch(ForbiddenControlText(), await File.ReadAllTextAsync(path));
        }
    }

    private static Task<WpfBlueprintFixture> CreateFixtureAsync(string blueprintId) => blueprintId switch
    {
        "desktop.csharp-wpf-tool" => WpfBlueprintFixture.CreateAsync(),
        "web.react-vite-ts" => WpfBlueprintFixture.CreateReactAsync(),
        "tool.python-cli" => WpfBlueprintFixture.CreatePythonAsync(),
        _ => throw new ArgumentOutOfRangeException(nameof(blueprintId), blueprintId, null),
    };

    private static string[] Split(string paths) => paths.Split(',');

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

    [GeneratedRegex("(?i)(shell|cmd(?:\\.exe)?|powershell(?:\\.exe)?|runas|administrator)")]
    private static partial Regex ForbiddenHandler();

    [GeneratedRegex("(?i)(token|secret|password|credential)")]
    private static partial Regex ForbiddenInput();

    [GeneratedRegex("(?im)(cmd\\.exe|powershell\\.exe|\\brunas\\b|--registry|--index-url|--extra-index-url|--token|--password|webview2|electron|(?:^|[^a-z])latest(?:[^a-z]|$))")]
    private static partial Regex ForbiddenControlText();

    private sealed record ExpectedTree(string Digest, string[] Paths);
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class M9ExecutionTestGroup
{
    public const string Name = "M9 execution E2E";
}
