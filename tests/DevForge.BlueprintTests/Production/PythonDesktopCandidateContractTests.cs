using DevForge.Application.Contracts;
using DevForge.Application.Planning;
using DevForge.Application.Planning.CompatibilityRules;
using DevForge.Domain.Environment;
using DevForge.Domain.Projects;

namespace DevForge.BlueprintTests.Production;

public sealed class PythonDesktopCandidateContractTests
{
    private const string Id = "tool.python-desktop";

    [Fact]
    public async Task NativeCandidateHasPinnedToolsAndMandatoryDesktopSmoke()
    {
        using var fixture = await ProductionBlueprintCatalogFixture.CreateCandidatesAsync();
        await fixture.Catalog.RefreshAsync(CancellationToken.None);
        var package = await fixture.Catalog.FindAsync(BlueprintReference.Create(Id, "1.0.0").Value, CancellationToken.None);
        Assert.NotNull(package);
        Assert.Equal(["python", "uv"], package.Manifest.Tools.Select(tool => tool.Id));
        Assert.Equal(["format", "lint", "typecheck", "test", "build", "cli-smoke", "desktop-smoke"],
            package.Manifest.Validators.Select(item => item.Id));
        Assert.All(package.Manifest.Validators, item => Assert.True(item.Required));
        var smoke = Assert.Single(package.Manifest.Validators, item => item.Id == "desktop-smoke");
        Assert.Equal(["run", "--frozen", "--no-sync", "--no-config", "team-desktop", "--smoke-test"],
            smoke.Parameters["arguments"].ArrayValue.Select(item => item.StringValue));
        var view = await ReadAsync(fixture, "overlays/base/src/team_tool/desktop.py");
        Assert.Contains("from tkinter import ttk", view, StringComparison.Ordinal);
        Assert.Contains("StringVar", view, StringComparison.Ordinal);
        var model = await ReadAsync(fixture, "overlays/base/src/team_tool/model.py");
        Assert.DoesNotContain("tkinter", model, StringComparison.Ordinal);
        Assert.DoesNotContain("subprocess", view + model, StringComparison.Ordinal);
        var project = await ReadAsync(fixture, "templates/pyproject.toml");
        Assert.Contains("team-desktop = \"team_tool.desktop_cli:main\"", project, StringComparison.Ordinal);
        Assert.Contains("ruff==0.16.3", project, StringComparison.Ordinal);
        Assert.Contains("dependencies = []", project, StringComparison.Ordinal);
        foreach (var file in new[] { "README", "ARCHITECTURE", "CONTRIBUTING", "DEVELOPMENT", "TESTING", "DEPLOYMENT", "TEAM_START_HERE" })
        {
            var text = await ReadAsync(fixture, "templates/" + file + ".md");
            Assert.Contains("## ", text, StringComparison.Ordinal);
            Assert.DoesNotContain("TODO", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task PlanIsDeterministicAndNonWindowsRuntimeIsBlocked()
    {
        using var fixture = await ProductionBlueprintCatalogFixture.CreateCandidatesAsync();
        await fixture.Catalog.RefreshAsync(CancellationToken.None);
        ProjectPlanner Planner(string os) => new(fixture.Catalog, new Doctor(), new Runtime(os),
            new InputSchemaValidator(), new CompatibilityRuleEvaluator(), new VariableTemplateResolver());
        ProjectRecipe Recipe(string path) => ProjectRecipe.Create(new ProjectRecipeDraft(
            "Team Desktop", path, Id, "1.0.0", new Dictionary<string, string?>(), [],
            Git: GitOptions.Create(initializeRepository: false).Value)).Value;
        var first = await Planner("windows").CreatePlanAsync(Recipe("C:\\first"), CancellationToken.None);
        var second = await Planner("windows").CreatePlanAsync(Recipe("D:\\second"), CancellationToken.None);
        Assert.True(first.IsValid);
        Assert.True(second.IsValid);
        Assert.Equal(first.Value.Preview.PlanHash, second.Value.Preview.PlanHash);
        Assert.False((await Planner("linux").CreatePlanAsync(Recipe("C:\\first"), CancellationToken.None)).IsValid);
    }

    private static async Task<string> ReadAsync(ProductionBlueprintCatalogFixture fixture, string relative)
    {
        await using var stream = await fixture.Source.Workspace.OpenReadAsync(
            WorkspaceRelativePath.Create(Id + "\\" + relative.Replace('/', '\\')).Value, CancellationToken.None);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(CancellationToken.None);
    }

    private sealed class Runtime(string os) : IPlanningRuntimeContextProvider
    {
        public PlanningRuntimeContext GetCurrent() => PlanningRuntimeContext.Create("1.0.0", os, "x64").Value;
    }

    private sealed class Doctor : IEnvironmentDoctor
    {
        public Task<EnvironmentSnapshot> InspectAsync(CancellationToken cancellationToken) => Task.FromResult(
            EnvironmentSnapshot.Create(DateTimeOffset.UnixEpoch,
                [new EnvironmentTool("python", "3.14.6", true), new EnvironmentTool("uv", "0.12.1", true)], []).Value);
    }
}
