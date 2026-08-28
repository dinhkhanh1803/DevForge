using DevForge.Application.Contracts;
using DevForge.Application.Planning;
using DevForge.Application.Planning.CompatibilityRules;
using DevForge.Domain.Environment;
using DevForge.Domain.Projects;

namespace DevForge.BlueprintTests.Production;

public sealed class WinFormsCandidateContractTests
{
    private const string BlueprintId = "desktop.csharp-winforms-tool";
    private static readonly string[] _handlers = ["copy-overlay", "render-template", "package-install"];

    [Fact]
    public async Task CandidateLoadsWithChecksumsAndExistingClosedDotnetVocabulary()
    {
        using var fixture = await ProductionBlueprintCatalogFixture.CreateCandidatesAsync();
        await fixture.Catalog.RefreshAsync(CancellationToken.None);
        var package = Assert.Single(await fixture.Catalog.ListAsync(CancellationToken.None), item => item.Manifest.Id == BlueprintId);
        Assert.Equal(BlueprintId, package.Manifest.Id);
        Assert.Equal("1.0.0", package.Manifest.Version);
        Assert.Equal("dotnet", Assert.Single(package.Manifest.Tools).Id);
        Assert.Empty(package.InputSchema);
        Assert.Equal(["format", "build", "test", "publish-smoke"],
            package.Manifest.Validators.Select(item => item.Id));
        Assert.All(package.Manifest.Validators, validator => Assert.True(validator.Required));
        Assert.All(package.Manifest.Actions, action =>
            Assert.Contains(action.HandlerId, _handlers));
        var restore = Assert.Single(package.Manifest.Actions, action => action.HandlerId == "package-install");
        Assert.Equal(["restore", "TeamTool.slnx", "--locked-mode"],
            restore.Parameters["arguments"].ArrayValue.Select(item => item.StringValue));
    }

    [Fact]
    public async Task DefaultCatalogDoesNotContainAnyV1Candidate()
    {
        using var fixture = await ProductionBlueprintCatalogFixture.CreateAsync();
        await fixture.Catalog.RefreshAsync(CancellationToken.None);
        Assert.Equal(["desktop.csharp-wpf-tool", "tool.python-cli", "web.react-vite-ts"],
            (await fixture.Catalog.ListAsync(CancellationToken.None))
                .Select(package => package.Manifest.Id).Order(StringComparer.Ordinal));
        var files = await fixture.Source.Workspace.EnumerateAllFilesAsync(CancellationToken.None);
        Assert.DoesNotContain(files, path => path.Value.Contains("candidates", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CandidateIsNativeWinFormsWithFiveLocksAndCompleteHandoff()
    {
        using var fixture = await ProductionBlueprintCatalogFixture.CreateCandidatesAsync();
        var desktop = await ReadAsync(fixture, "overlays/base/src/TeamTool.Desktop/TeamTool.Desktop.csproj");
        Assert.Contains("<UseWindowsForms>true</UseWindowsForms>", desktop, StringComparison.Ordinal);
        Assert.DoesNotContain("UseWPF", desktop, StringComparison.Ordinal);
        Assert.Contains("TeamTool.Application.csproj", desktop, StringComparison.Ordinal);
        Assert.Contains("TeamTool.Infrastructure.csproj", desktop, StringComparison.Ordinal);
        var files = await fixture.Source.Workspace.EnumerateFilesAsync(
            WorkspaceRelativePath.Create(BlueprintId).Value, true, CancellationToken.None);
        Assert.Equal(5, files.Count(path => path.Value.EndsWith("packages.lock.json", StringComparison.Ordinal)));
        Assert.DoesNotContain(files, path => path.Value.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(files, path => path.Value.EndsWith(".env", StringComparison.OrdinalIgnoreCase));
        foreach (var document in new[]
        {
            "README.md", "ARCHITECTURE.md", "CONTRIBUTING.md", "DEVELOPMENT.md",
            "TESTING.md", "DEPLOYMENT.md", "TEAM_START_HERE.md",
        })
        {
            var text = await ReadAsync(fixture, "templates/" + document);
            Assert.Contains("## ", text, StringComparison.Ordinal);
            Assert.DoesNotContain("TODO", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("placeholder", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("WPF", text, StringComparison.Ordinal);
        }

        var packages = await ReadAsync(fixture, "templates/Directory.Packages.props");
        Assert.Contains("CommunityToolkit.Mvvm\" Version=\"8.4.2", packages, StringComparison.Ordinal);
        Assert.Contains("Microsoft.Extensions.Hosting\" Version=\"10.0.10", packages, StringComparison.Ordinal);
        Assert.DoesNotContain("Version=\"*", packages, StringComparison.Ordinal);
        var form = await ReadAsync(fixture, "overlays/base/src/TeamTool.Desktop/MainForm.cs");
        Assert.Contains("AutoScaleMode.Dpi", form, StringComparison.Ordinal);
        Assert.Contains("AccessibleName = \"Refresh status\"", form, StringComparison.Ordinal);
        Assert.Contains("DataSourceUpdateMode.Never", form, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CandidatePlanIsTargetIndependentAndRefusesNonWindowsRuntime()
    {
        using var fixture = await ProductionBlueprintCatalogFixture.CreateCandidatesAsync();
        await fixture.Catalog.RefreshAsync(CancellationToken.None);
        var planner = Planner(fixture, "windows");
        var first = await planner.CreatePlanAsync(Recipe("C:\\one"), CancellationToken.None);
        var second = await planner.CreatePlanAsync(Recipe("D:\\two"), CancellationToken.None);
        Assert.True(first.IsValid);
        Assert.True(second.IsValid);
        Assert.Equal(first.Value.Preview.PlanHash, second.Value.Preview.PlanHash);
        var unsupported = await Planner(fixture, "linux").CreatePlanAsync(Recipe("C:\\one"), CancellationToken.None);
        Assert.False(unsupported.IsValid);
    }

    private static ProjectPlanner Planner(ProductionBlueprintCatalogFixture fixture, string os) => new(
        fixture.Catalog, new FixedDoctor(),
        new FixedRuntime(PlanningRuntimeContext.Create("1.0.0", os, "x64").Value),
        new InputSchemaValidator(), new CompatibilityRuleEvaluator(), new VariableTemplateResolver());

    private static ProjectRecipe Recipe(string path) => ProjectRecipe.Create(new ProjectRecipeDraft(
        "Team Tool", path, BlueprintId, "1.0.0", new Dictionary<string, string?>(), [],
        Git: GitOptions.Create(initializeRepository: false).Value)).Value;

    private static async Task<string> ReadAsync(ProductionBlueprintCatalogFixture fixture, string relative)
    {
        await using var stream = await fixture.Source.Workspace.OpenReadAsync(
            WorkspaceRelativePath.Create(BlueprintId + "\\" + relative.Replace('/', '\\')).Value,
            CancellationToken.None);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(CancellationToken.None);
    }

    private sealed class FixedRuntime(PlanningRuntimeContext context) : IPlanningRuntimeContextProvider
    {
        public PlanningRuntimeContext GetCurrent() => context;
    }

    private sealed class FixedDoctor : IEnvironmentDoctor
    {
        public Task<EnvironmentSnapshot> InspectAsync(CancellationToken cancellationToken) => Task.FromResult(
            EnvironmentSnapshot.Create(DateTimeOffset.UnixEpoch,
                [new EnvironmentTool("dotnet", "10.0.302", true)], []).Value);
    }
}
