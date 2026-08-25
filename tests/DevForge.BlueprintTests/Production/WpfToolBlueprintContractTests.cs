using DevForge.Application.Contracts;
using DevForge.Application.Planning;
using DevForge.Application.Planning.CompatibilityRules;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Environment;
using DevForge.Domain.Projects;

namespace DevForge.BlueprintTests.Production;

public sealed class WpfToolBlueprintContractTests
{
    private const string BlueprintId = "desktop.csharp-wpf-tool";

    [Fact]
    public async Task PackageLoadsThroughProductionCatalogWithExactIdentityAndPolicy()
    {
        using var fixture = await ProductionBlueprintCatalogFixture.CreateAsync();

        await fixture.Catalog.RefreshAsync(CancellationToken.None);

        var blueprint = Assert.Single(
            await fixture.Catalog.ListAsync(CancellationToken.None),
            candidate => candidate.Manifest.Id == BlueprintId);
        Assert.Equal("1.0.0", blueprint.Manifest.Version);
        Assert.Equal(BlueprintTrust.BuiltIn, blueprint.Manifest.Trust);
        var tool = Assert.Single(blueprint.Manifest.Tools);
        Assert.Equal("dotnet", tool.Id);
        Assert.Equal(">=10.0.0 <11.0.0", tool.VersionRange);
        Assert.Empty(blueprint.InputSchema);
        Assert.Equal(
            ["copy-source", "copy-tests", "render-solution", "render-build", "render-packages", "render-sdk", "render-editor", "render-gitignore", "render-readme", "render-architecture", "render-contributing", "render-development", "render-testing", "render-deployment", "render-team-start", "restore"],
            blueprint.Manifest.Actions.Select(action => action.Id));
        Assert.Equal(
            ["format", "build", "test", "publish-smoke"],
            blueprint.Manifest.Validators.Select(validator => validator.Id));
        Assert.Equal(
            [
                "communitytoolkit.mvvm@8.4.2",
                "microsoft.extensions.hosting@10.0.10",
                "microsoft.net.test.sdk@17.14.1",
                "xunit.runner.visualstudio@3.1.4",
                "xunit@2.9.3",
            ],
            blueprint.Manifest.Dependencies
                .Select(dependency => $"{dependency.Id}@{dependency.Version}")
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task PackageHasTheCompleteChecksummedProductionShape()
    {
        using var fixture = await ProductionBlueprintCatalogFixture.CreateAsync();
        var package = Relative(BlueprintId);
        var requiredFiles = new[]
        {
            "manifest.yaml",
            "inputs.schema.json",
            "rules.yaml",
            "README.md",
            "checksums.json",
        };
        var requiredDirectories = new[]
        {
            "templates",
            "overlays",
            "validators",
            "migrations",
        };

        foreach (var file in requiredFiles)
        {
            Assert.True(await fixture.Source.Workspace.FileExistsAsync(
                Relative($"{BlueprintId}\\{file}"),
                CancellationToken.None));
        }

        foreach (var directory in requiredDirectories)
        {
            Assert.True(await fixture.Source.Workspace.DirectoryExistsAsync(
                Relative($"{BlueprintId}\\{directory}"),
                CancellationToken.None));
        }

        Assert.True(await fixture.Source.Workspace.DirectoryExistsAsync(
            package,
            CancellationToken.None));
    }

    [Fact]
    public async Task PublishProfileKeepsSmokeArtifactsInsideTheGeneratedProject()
    {
        using var fixture = await ProductionBlueprintCatalogFixture.CreateAsync();
        await using var stream = await fixture.Source.Workspace.OpenReadAsync(
            Relative($"{BlueprintId}\\overlays\\base\\src\\TeamTool.Desktop\\Properties\\PublishProfiles\\WindowsSmoke.pubxml"),
            CancellationToken.None);
        using var reader = new StreamReader(stream);
        var profile = await reader.ReadToEndAsync(CancellationToken.None);

        Assert.Contains("<PublishDir>..\\..\\artifacts\\publish\\</PublishDir>", profile, StringComparison.Ordinal);
        Assert.DoesNotContain("..\\..\\..", profile, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProductionPlanIsDeterministicAndCoversTheReviewedWpfMatrix()
    {
        using var fixture = await ProductionBlueprintCatalogFixture.CreateAsync();
        await fixture.Catalog.RefreshAsync(CancellationToken.None);
        var environment = EnvironmentSnapshot.Create(
            DateTimeOffset.Parse("2026-08-25T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            [new EnvironmentTool("dotnet", "10.0.302", true)],
            []).Value;
        var planner = new ProjectPlanner(
            fixture.Catalog,
            new FixedEnvironmentDoctor(environment),
            new FixedRuntimeProvider(PlanningRuntimeContext.Create("1.0.0", "windows", "x64").Value),
            new InputSchemaValidator(),
            new CompatibilityRuleEvaluator(),
            new VariableTemplateResolver());
        var firstRecipe = Recipe("C:\\generated-one");
        var secondRecipe = Recipe("D:\\generated-two");

        var first = await planner.CreatePlanAsync(firstRecipe, CancellationToken.None);
        var second = await planner.CreatePlanAsync(secondRecipe, CancellationToken.None);

        Assert.True(first.IsValid, string.Join(Environment.NewLine, first.Issues.Select(issue => issue.Message)));
        Assert.True(second.IsValid, string.Join(Environment.NewLine, second.Issues.Select(issue => issue.Message)));
        Assert.Equal(first.Value.Preview.PlanHash, second.Value.Preview.PlanHash);
        Assert.Equal(first.Value.Plan.Id, first.Value.Preview.PlanHash);
        Assert.Equal(
            ["copy-source", "copy-tests", "render-solution", "render-build", "render-packages", "render-sdk", "render-editor", "render-gitignore", "render-readme", "render-architecture", "render-contributing", "render-development", "render-testing", "render-deployment", "render-team-start", "restore"],
            first.Value.Plan.Steps.Select(step => step.Id));
        Assert.Equal(["format", "build", "test", "publish-smoke"], first.Value.Plan.Validators.Select(item => item.Id));
        Assert.StartsWith("sha256:", first.Value.Preview.PlanHash, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GeneratedSkeletonDeclaresCleanArchitectureHandoffAndNoWebOrSecretSurface()
    {
        using var fixture = await ProductionBlueprintCatalogFixture.CreateAsync();
        var files = await fixture.Source.Workspace.EnumerateFilesAsync(
            Relative(BlueprintId),
            recursive: true,
            CancellationToken.None);
        var relativeFiles = files
            .Select(path => path.Value[(BlueprintId.Length + 1)..].Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Contains("overlays/base/src/TeamTool.Desktop/App.xaml", relativeFiles);
        Assert.Contains("overlays/base/src/TeamTool.Desktop/MainViewModel.cs", relativeFiles);
        Assert.Contains("overlays/base/src/TeamTool.Application/TeamTool.Application.csproj", relativeFiles);
        Assert.Contains("overlays/base/src/TeamTool.Domain/TeamTool.Domain.csproj", relativeFiles);
        Assert.Contains("overlays/base/src/TeamTool.Infrastructure/TeamTool.Infrastructure.csproj", relativeFiles);
        Assert.Contains("overlays/base/tests/TeamTool.UnitTests/TeamTool.UnitTests.csproj", relativeFiles);
        Assert.DoesNotContain(relativeFiles, path => path.EndsWith(".env", StringComparison.OrdinalIgnoreCase));

        var requiredHandoff = new[]
        {
            "README.md",
            "ARCHITECTURE.md",
            "CONTRIBUTING.md",
            "DEVELOPMENT.md",
            "TESTING.md",
            "DEPLOYMENT.md",
            "TEAM_START_HERE.md",
        };
        foreach (var document in requiredHandoff)
        {
            var content = await ReadTextAsync(fixture, $"{BlueprintId}\\templates\\{document}");
            Assert.Contains("# ", content, StringComparison.Ordinal);
            Assert.DoesNotContain("TODO", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("placeholder", content, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var file in files.Where(path => !path.Value.EndsWith("checksums.json", StringComparison.Ordinal)))
        {
            var content = await ReadTextAsync(fixture, file.Value);
            Assert.DoesNotContain("WebView", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Microsoft.Web.WebView2", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("password=", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("token=", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("connectionstring=", content, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task GeneratedSolutionPinsPackagesLocksAndNativeWpfProjectGraph()
    {
        using var fixture = await ProductionBlueprintCatalogFixture.CreateAsync();
        var packages = await ReadTextAsync(fixture, $"{BlueprintId}\\templates\\Directory.Packages.props");
        Assert.Contains("CommunityToolkit.Mvvm\" Version=\"8.4.2", packages, StringComparison.Ordinal);
        Assert.Contains("Microsoft.Extensions.Hosting\" Version=\"10.0.10", packages, StringComparison.Ordinal);
        Assert.Contains("Microsoft.NET.Test.Sdk\" Version=\"17.14.1", packages, StringComparison.Ordinal);
        Assert.Contains("xunit\" Version=\"2.9.3", packages, StringComparison.Ordinal);
        Assert.Contains("xunit.runner.visualstudio\" Version=\"3.1.4", packages, StringComparison.Ordinal);
        Assert.DoesNotContain("Version=\"*", packages, StringComparison.Ordinal);

        var desktop = await ReadTextAsync(
            fixture,
            $"{BlueprintId}\\overlays\\base\\src\\TeamTool.Desktop\\TeamTool.Desktop.csproj");
        Assert.Contains("<TargetFramework>net10.0-windows</TargetFramework>", desktop, StringComparison.Ordinal);
        Assert.Contains("<UseWPF>true</UseWPF>", desktop, StringComparison.Ordinal);
        Assert.Contains("TeamTool.Application.csproj", desktop, StringComparison.Ordinal);
        Assert.Contains("TeamTool.Infrastructure.csproj", desktop, StringComparison.Ordinal);

        var files = await fixture.Source.Workspace.EnumerateFilesAsync(
            Relative(BlueprintId),
            recursive: true,
            CancellationToken.None);
        Assert.Equal(5, files.Count(path => path.Value.EndsWith("packages.lock.json", StringComparison.Ordinal)));
    }

    private static ProjectRecipe Recipe(string targetPath) => ProjectRecipe.Create(new ProjectRecipeDraft(
        "Team Tool",
        targetPath,
        BlueprintId,
        "1.0.0",
        new Dictionary<string, string?>(),
        [],
        Git: GitOptions.Create(initializeRepository: false).Value)).Value;

    private static async Task<string> ReadTextAsync(
        ProductionBlueprintCatalogFixture fixture,
        string path)
    {
        await using var stream = await fixture.Source.Workspace.OpenReadAsync(
            Relative(path),
            CancellationToken.None);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(CancellationToken.None);
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

    private static WorkspaceRelativePath Relative(string value) =>
        WorkspaceRelativePath.Create(value).Value;
}
