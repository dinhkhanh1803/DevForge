using DevForge.Application.Contracts;
using DevForge.Application.Planning;
using DevForge.Application.Planning.CompatibilityRules;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Environment;
using DevForge.Domain.Projects;

namespace DevForge.BlueprintTests.Production;

public sealed class PythonCliBlueprintContractTests
{
    private const string BlueprintId = "tool.python-cli";

    [Fact]
    public async Task PackageLoadsWithExactIdentityToolsActionsAndValidators()
    {
        using var fixture = await ProductionBlueprintCatalogFixture.CreateAsync();
        await fixture.Catalog.RefreshAsync(CancellationToken.None);

        var snapshot = await fixture.Catalog.InspectAsync(CancellationToken.None);
        var matching = snapshot.ExecutableBlueprints
            .Where(candidate => candidate.Manifest.Id == BlueprintId)
            .ToArray();
        Assert.True(
            matching.Length == 1,
            string.Join(
                "; ",
                snapshot.Inspections
                    .Where(item => item.PackageDirectory.Value == BlueprintId)
                    .SelectMany(item => item.Issues)
                    .Select(issue => $"{issue.Code}: {issue.Summary}")));
        var blueprint = matching[0];
        Assert.Equal("1.0.0", blueprint.Manifest.Version);
        Assert.Equal(BlueprintTrust.BuiltIn, blueprint.Manifest.Trust);
        Assert.Equal(
            ["python@>=3.14.0 <3.15.0", "uv@>=0.12.0 <0.13.0"],
            blueprint.Manifest.Tools.Select(tool => $"{tool.Id}@{tool.VersionRange}"));
        Assert.Empty(blueprint.InputSchema);
        Assert.Equal(
            [
                "copy-source", "copy-tests", "render-pyproject", "render-lock",
                "render-python-version", "render-editor", "render-gitignore", "render-env-example",
                "render-readme", "render-architecture", "render-contributing", "render-development",
                "render-testing", "render-deployment", "render-team-start", "install",
            ],
            blueprint.Manifest.Actions.Select(action => action.Id));
        Assert.Equal(
            ["format", "lint", "typecheck", "test", "build", "cli-smoke"],
            blueprint.Manifest.Validators.Select(validator => validator.Id));
    }

    [Fact]
    public async Task ProductionPlanIsDeterministicAndUsesOnlyReviewedUvCommands()
    {
        using var fixture = await ProductionBlueprintCatalogFixture.CreateAsync();
        await fixture.Catalog.RefreshAsync(CancellationToken.None);
        var environment = EnvironmentSnapshot.Create(
            DateTimeOffset.Parse("2026-08-26T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            [
                new EnvironmentTool("python", "3.14.6", true),
                new EnvironmentTool("uv", "0.12.1", true),
            ],
            []).Value;
        var planner = new ProjectPlanner(
            fixture.Catalog,
            new FixedEnvironmentDoctor(environment),
            new FixedRuntimeProvider(PlanningRuntimeContext.Create("1.0.0", "windows", "x64").Value),
            new InputSchemaValidator(),
            new CompatibilityRuleEvaluator(),
            new VariableTemplateResolver());

        var first = await planner.CreatePlanAsync(Recipe("C:\\generated-one"), CancellationToken.None);
        var second = await planner.CreatePlanAsync(Recipe("D:\\generated-two"), CancellationToken.None);

        Assert.True(first.IsValid, string.Join(Environment.NewLine, first.Issues.Select(issue => issue.Message)));
        Assert.True(second.IsValid, string.Join(Environment.NewLine, second.Issues.Select(issue => issue.Message)));
        Assert.Equal(first.Value.Preview.PlanHash, second.Value.Preview.PlanHash);
        Assert.Equal(first.Value.Plan.Id, first.Value.Preview.PlanHash);
        Assert.StartsWith("sha256:", first.Value.Preview.PlanHash, StringComparison.Ordinal);

        var install = Assert.Single(first.Value.Plan.Steps, step => step.Id == "install");
        Assert.Equal("package-install", install.Handler);
        Assert.Equal("uv", install.Inputs["packageManager"].StringValue);
        Assert.Equal(
            ["sync", "--frozen", "--no-config"],
            install.Inputs["arguments"].ArrayValue.Select(value => value.StringValue));

        var reviewed = new[]
        {
            new[] { "run", "--frozen", "--no-sync", "--no-config", "ruff", "format", "--check", "." },
            new[] { "run", "--frozen", "--no-sync", "--no-config", "ruff", "check", "." },
            new[] { "run", "--frozen", "--no-sync", "--no-config", "mypy", "src", "tests" },
            new[] { "run", "--frozen", "--no-sync", "--no-config", "pytest" },
            new[]
            {
                "run", "--frozen", "--no-sync", "--no-config",
                "pyproject-build", "--no-isolation",
            },
            new[] { "run", "--frozen", "--no-sync", "--no-config", "team-tool", "--help" },
        };
        Assert.Equal(reviewed.Length, first.Value.Plan.Validators.Length);
        for (var index = 0; index < reviewed.Length; index++)
        {
            var validator = first.Value.Plan.Validators[index];
            Assert.Equal("validate-command", validator.Handler);
            Assert.Equal("uv", validator.Inputs["executable"].StringValue);
            Assert.Equal(
                reviewed[index],
                validator.Inputs["arguments"].ArrayValue.Select(value => value.StringValue));
        }
    }

    [Fact]
    public async Task PackagePinsExactBuildAndQualityDependenciesWithFrozenLock()
    {
        using var fixture = await ProductionBlueprintCatalogFixture.CreateAsync();
        await fixture.Catalog.RefreshAsync(CancellationToken.None);
        var blueprint = Assert.Single(
            await fixture.Catalog.ListAsync(CancellationToken.None),
            candidate => candidate.Manifest.Id == BlueprintId);

        Assert.Equal(
            ["build@1.5.0", "hatchling@1.32.0", "mypy@2.3.1", "pytest@9.1.1", "ruff@0.16.3"],
            blueprint.Manifest.Dependencies
                .Select(dependency => $"{dependency.Id}@{dependency.Version}")
                .Order(StringComparer.Ordinal));

        var project = await ReadTextAsync(fixture, $"{BlueprintId}\\templates\\pyproject.toml");
        Assert.Contains("requires-python = \">=3.14,<3.15\"", project, StringComparison.Ordinal);
        foreach (var pin in new[]
                 {
                     "build==1.5.0", "hatchling==1.32.0", "mypy==2.3.1",
                     "pytest==9.1.1", "ruff==0.16.3",
                 })
        {
            Assert.Contains(pin, project, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(">=", project[(project.IndexOf("[dependency-groups]", StringComparison.Ordinal))..], StringComparison.Ordinal);
        Assert.DoesNotContain("~=", project, StringComparison.Ordinal);
        Assert.DoesNotContain("*", project, StringComparison.Ordinal);

        var lockfile = await ReadTextAsync(fixture, $"{BlueprintId}\\templates\\uv.lock");
        Assert.Contains("version = 1", lockfile, StringComparison.Ordinal);
        Assert.Contains("name = \"ruff\"", lockfile, StringComparison.Ordinal);
        Assert.Contains("version = \"0.16.3\"", lockfile, StringComparison.Ordinal);
        Assert.Contains("requires-python = \"==3.14.*\"", lockfile, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PackageHasSrcLayoutTypedConfigLoggingTestsAndSecretSafeHandoff()
    {
        using var fixture = await ProductionBlueprintCatalogFixture.CreateAsync();
        var requiredFiles = new[]
        {
            "manifest.yaml",
            "inputs.schema.json",
            "rules.yaml",
            "checksums.json",
            "templates\\pyproject.toml",
            "templates\\uv.lock",
            "templates\\.python-version",
            "templates\\.env.example",
            "overlays\\base\\src\\team_tool\\__init__.py",
            "overlays\\base\\src\\team_tool\\__main__.py",
            "overlays\\base\\src\\team_tool\\cli.py",
            "overlays\\base\\src\\team_tool\\config.py",
            "overlays\\base\\src\\team_tool\\logging_config.py",
            "overlays\\base\\tests\\test_cli.py",
            "overlays\\base\\tests\\test_config.py",
        };
        foreach (var file in requiredFiles)
        {
            Assert.True(
                await fixture.Source.Workspace.FileExistsAsync(
                    Relative($"{BlueprintId}\\{file}"),
                    CancellationToken.None),
                file);
        }

        var config = await ReadTextAsync(fixture, $"{BlueprintId}\\overlays\\base\\src\\team_tool\\config.py");
        Assert.Contains("@dataclass(frozen=True, slots=True)", config, StringComparison.Ordinal);
        Assert.Contains("Mapping[str, str]", config, StringComparison.Ordinal);
        Assert.Contains("TEAM_TOOL_LOG_LEVEL", config, StringComparison.Ordinal);
        var logging = await ReadTextAsync(
            fixture,
            $"{BlueprintId}\\overlays\\base\\src\\team_tool\\logging_config.py");
        Assert.Contains("logging.config.dictConfig", logging, StringComparison.Ordinal);

        var envExample = await ReadTextAsync(fixture, $"{BlueprintId}\\templates\\.env.example");
        Assert.Equal("TEAM_TOOL_LOG_LEVEL=\n", envExample.Replace("\r\n", "\n", StringComparison.Ordinal));
        var gitignore = await ReadTextAsync(fixture, $"{BlueprintId}\\templates\\.gitignore");
        Assert.Contains(".env*", gitignore, StringComparison.Ordinal);
        Assert.Contains("!.env.example", gitignore, StringComparison.Ordinal);

        foreach (var document in new[]
                 {
                     "README.md", "ARCHITECTURE.md", "CONTRIBUTING.md", "DEVELOPMENT.md",
                     "TESTING.md", "DEPLOYMENT.md", "TEAM_START_HERE.md",
                 })
        {
            var content = await ReadTextAsync(fixture, $"{BlueprintId}\\templates\\{document}");
            Assert.Contains("# ", content, StringComparison.Ordinal);
            Assert.DoesNotContain("TODO", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("placeholder", content, StringComparison.OrdinalIgnoreCase);
        }

        var files = await fixture.Source.Workspace.EnumerateFilesAsync(
            Relative(BlueprintId),
            recursive: true,
            CancellationToken.None);
        Assert.DoesNotContain(files, file => file.Value.EndsWith(".env", StringComparison.OrdinalIgnoreCase));
        foreach (var file in files.Where(path => !path.Value.EndsWith("checksums.json", StringComparison.Ordinal)))
        {
            var content = await ReadTextAsync(fixture, file.Value);
            Assert.DoesNotContain("password=", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("token=", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("connectionstring=", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("--index", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("python -c", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("python -m", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("uv add", content, StringComparison.OrdinalIgnoreCase);
        }
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
