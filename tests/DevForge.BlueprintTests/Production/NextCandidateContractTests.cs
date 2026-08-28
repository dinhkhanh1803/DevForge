using DevForge.Application.Contracts;
using DevForge.Application.Planning;
using DevForge.Application.Planning.CompatibilityRules;
using DevForge.Domain.Environment;
using DevForge.Domain.Projects;

namespace DevForge.BlueprintTests.Production;

public sealed class NextCandidateContractTests
{
    [Theory]
    [InlineData("windows", "22.23.2", "10.24.0", true)]
    [InlineData("windows", "22.21.1", "10.24.0", false)]
    [InlineData("windows", "23.0.0", "10.24.0", false)]
    [InlineData("windows", "22.23.2", "10.25.0", false)]
    [InlineData("linux", "22.23.2", "10.24.0", false)]
    public async Task CandidatePlanningRequiresReviewedRuntime(string os, string node, string pnpm, bool expected)
    {
        using var fixture = await ProductionBlueprintCatalogFixture.CreateCandidatesAsync();
        await fixture.Catalog.RefreshAsync(default);
        var planner = new ProjectPlanner(fixture.Catalog, new FixedDoctor(node, pnpm),
            new FixedRuntime(PlanningRuntimeContext.Create("1.0.0", os, "x64").Value),
            new InputSchemaValidator(), new CompatibilityRuleEvaluator(), new VariableTemplateResolver());
        var recipe = ProjectRecipe.Create(new ProjectRecipeDraft("Team Portal", "C:\\projects\\team-portal",
            "web.next-ts", "1.0.0", new Dictionary<string, string?>(), [],
            Git: GitOptions.Create(initializeRepository: false).Value)).Value;
        var plan = await planner.CreatePlanAsync(recipe, default);
        Assert.Equal(expected, plan.IsValid);
    }

    [Fact]
    public async Task CandidateHasPinnedRuntimeAndEveryIndependentQualityGate()
    {
        using var fixture = await ProductionBlueprintCatalogFixture.CreateCandidatesAsync();
        await fixture.Catalog.RefreshAsync(default);
        var package = await fixture.Catalog.FindAsync(BlueprintReference.Create("web.next-ts", "1.0.0").Value, default);
        Assert.NotNull(package);
        Assert.Equal(["node@>=22.23.2 <23.0.0", "pnpm@>=10.24.0 <10.25.0"],
            package.Manifest.Tools.Select(tool => tool.Id + "@" + tool.VersionRange));
        Assert.Equal(["format", "lint", "typecheck", "test", "build", "smoke"],
            package.Manifest.Validators.Select(item => item.Id));
        Assert.All(package.Manifest.Validators, item => Assert.True(item.Required));
        Assert.DoesNotContain(package.Manifest.Artifacts, artifact => artifact.Path.StartsWith(".next", StringComparison.Ordinal));
        Assert.Contains(package.Manifest.Dependencies, dependency => dependency.Id == "next" && dependency.Version == "16.3.3");
    }

    [Fact]
    public async Task SourceContractIncludesStrictTypesBoundedSmokeAndCompleteHandoff()
    {
        using var fixture = await ProductionBlueprintCatalogFixture.CreateCandidatesAsync();
        foreach (var document in new[] { "README", "ARCHITECTURE", "CONTRIBUTING", "DEVELOPMENT", "TESTING", "DEPLOYMENT", "TEAM_START_HERE" })
        {
            var text = await ReadAsync(fixture, "templates/" + document + ".md");
            Assert.Contains("## ", text, StringComparison.Ordinal);
            Assert.DoesNotContain("TODO", text, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Contains("\"strict\": true", await ReadAsync(fixture, "templates/tsconfig.json"), StringComparison.Ordinal);
        var smoke = await ReadAsync(fixture, "overlays/base/scripts/smoke.mjs");
        Assert.Contains("127.0.0.1", smoke, StringComparison.Ordinal);
        Assert.Contains("finally", smoke, StringComparison.Ordinal);
        Assert.Contains("AbortSignal.timeout", smoke, StringComparison.Ordinal);
        var ignore = await ReadAsync(fixture, "templates/.gitignore");
        Assert.Contains("node_modules/", ignore, StringComparison.Ordinal);
        Assert.Contains(".next/", ignore, StringComparison.Ordinal);
        Assert.Contains("!.env.example", ignore, StringComparison.Ordinal);
        var package = await ReadAsync(fixture, "templates/package.json");
        Assert.DoesNotContain("\": \"^", package, StringComparison.Ordinal);
        Assert.DoesNotContain("\": \"~", package, StringComparison.Ordinal);
        Assert.Contains("pnpm@10.24.0", package, StringComparison.Ordinal);
    }

    private static async Task<string> ReadAsync(ProductionBlueprintCatalogFixture fixture, string path)
    {
        await using var stream = await fixture.Source.Workspace.OpenReadAsync(
            WorkspaceRelativePath.Create("web.next-ts\\" + path.Replace('/', '\\')).Value, default);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private sealed class FixedRuntime(PlanningRuntimeContext context) : IPlanningRuntimeContextProvider
    {
        public PlanningRuntimeContext GetCurrent() => context;
    }

    private sealed class FixedDoctor(string node, string pnpm) : IEnvironmentDoctor
    {
        public Task<EnvironmentSnapshot> InspectAsync(CancellationToken cancellationToken) => Task.FromResult(
            EnvironmentSnapshot.Create(DateTimeOffset.UnixEpoch,
                [new EnvironmentTool("node", node, true), new EnvironmentTool("pnpm", pnpm, true)], []).Value);
    }
}
