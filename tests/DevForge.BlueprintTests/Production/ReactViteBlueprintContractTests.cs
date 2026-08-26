using DevForge.Application.Contracts;
using DevForge.Application.Planning;
using DevForge.Application.Planning.CompatibilityRules;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Environment;
using DevForge.Domain.Projects;

namespace DevForge.BlueprintTests.Production;

public sealed class ReactViteBlueprintContractTests
{
    private const string BlueprintId = "web.react-vite-ts";

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
            ["node@>=22.0.0 <25.0.0", "pnpm@>=10.0.0 <11.0.0"],
            blueprint.Manifest.Tools.Select(tool => $"{tool.Id}@{tool.VersionRange}"));
        Assert.Empty(blueprint.InputSchema);
        Assert.Equal(
            [
                "copy-source", "render-package", "render-lock", "render-index", "render-tsconfig",
                "render-tsconfig-app", "render-tsconfig-node", "render-vite", "render-eslint",
                "render-prettier", "render-prettier-ignore", "render-editor", "render-gitignore",
                "render-env-example", "render-readme", "render-architecture", "render-contributing",
                "render-development", "render-testing", "render-deployment", "render-team-start", "install",
            ],
            blueprint.Manifest.Actions.Select(action => action.Id));
        Assert.Equal(
            ["lint", "typecheck", "test", "build"],
            blueprint.Manifest.Validators.Select(validator => validator.Id));
    }

    [Fact]
    public async Task PackageContainsStrictTypeScriptEnvApiTestBuildAndHandoffAssets()
    {
        using var fixture = await ProductionBlueprintCatalogFixture.CreateAsync();
        var requiredFiles = new[]
        {
            "manifest.yaml",
            "inputs.schema.json",
            "rules.yaml",
            "checksums.json",
            "templates\\package.json",
            "templates\\pnpm-lock.yaml",
            "templates\\tsconfig.app.json",
            "templates\\vite.config.ts",
            "templates\\eslint.config.js",
            "templates\\.prettierrc.json",
            "templates\\.env.example",
            "overlays\\base\\src\\config\\env.ts",
            "overlays\\base\\src\\services\\apiClient.ts",
            "overlays\\base\\src\\app\\App.test.tsx",
        };

        foreach (var file in requiredFiles)
        {
            Assert.True(
                await fixture.Source.Workspace.FileExistsAsync(
                    WorkspaceRelativePath.Create($"{BlueprintId}\\{file}").Value,
                    CancellationToken.None),
                file);
        }
    }

    [Fact]
    public async Task ProductionPlanIsDeterministicAndUsesOnlyReviewedPnpmCommands()
    {
        using var fixture = await ProductionBlueprintCatalogFixture.CreateAsync();
        await fixture.Catalog.RefreshAsync(CancellationToken.None);
        var environment = EnvironmentSnapshot.Create(
            DateTimeOffset.Parse("2026-08-25T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            [
                new EnvironmentTool("node", "22.21.1", true),
                new EnvironmentTool("pnpm", "10.24.0", true),
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
        Assert.Equal("pnpm", install.Inputs["packageManager"].StringValue);
        Assert.Equal(
            ["install", "--frozen-lockfile", "--ignore-scripts"],
            install.Inputs["arguments"].ArrayValue.Select(value => value.StringValue));

        Assert.Equal(["lint", "typecheck", "test", "build"], first.Value.Plan.Validators.Select(item => item.Id));
        foreach (var validator in first.Value.Plan.Validators)
        {
            Assert.Equal("validate-command", validator.Handler);
            Assert.Equal("pnpm", validator.Inputs["executable"].StringValue);
            Assert.Equal(
                ["run", validator.Id],
                validator.Inputs["arguments"].ArrayValue.Select(value => value.StringValue));
        }
    }

    [Fact]
    public async Task PackagePinsCompleteDependencySetAndLockfile()
    {
        using var fixture = await ProductionBlueprintCatalogFixture.CreateAsync();
        await fixture.Catalog.RefreshAsync(CancellationToken.None);
        var blueprint = Assert.Single(
            await fixture.Catalog.ListAsync(CancellationToken.None),
            candidate => candidate.Manifest.Id == BlueprintId);

        Assert.Equal(
            [
                "eslint-plugin-react-hooks@7.1.1", "eslint-plugin-react-refresh@0.5.4", "eslint.js@10.0.1",
                "eslint@10.9.1", "jsdom@29.1.1", "prettier@3.9.6", "react-dom@19.2.8",
                "react@19.2.8", "testing-library.jest-dom@7.0.1", "testing-library.react@16.3.2",
                "types.node@22.20.1", "types.react-dom@19.2.5", "types.react@19.2.18",
                "typescript-eslint@8.68.0", "typescript@6.0.3", "vite@8.2.2",
                "vitejs.plugin-react@6.1.0", "vitest@4.1.11", "zod@4.4.3",
            ],
            blueprint.Manifest.Dependencies
                .Select(dependency => $"{dependency.Id}@{dependency.Version}")
                .Order(StringComparer.Ordinal));

        var package = await ReadTextAsync(fixture, $"{BlueprintId}\\templates\\package.json");
        Assert.Contains("\"packageManager\": \"pnpm@10.24.0\"", package, StringComparison.Ordinal);
        Assert.DoesNotContain("\": \"^", package, StringComparison.Ordinal);
        Assert.DoesNotContain("\": \"~", package, StringComparison.Ordinal);
        Assert.DoesNotContain("\": \"*", package, StringComparison.Ordinal);

        var lockfile = await ReadTextAsync(fixture, $"{BlueprintId}\\templates\\pnpm-lock.yaml");
        Assert.Contains("lockfileVersion: '9.0'", lockfile, StringComparison.Ordinal);
        Assert.Contains("react:\n        specifier: 19.2.8\n        version: 19.2.8", lockfile, StringComparison.Ordinal);
        Assert.Contains("vite:\n        specifier: 8.2.2", lockfile, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GeneratedSkeletonHasStrictAliasValidatedPublicEnvAndSecretSafeHandoff()
    {
        using var fixture = await ProductionBlueprintCatalogFixture.CreateAsync();
        var tsconfig = await ReadTextAsync(fixture, $"{BlueprintId}\\templates\\tsconfig.app.json");
        Assert.Contains("\"strict\": true", tsconfig, StringComparison.Ordinal);
        Assert.Contains("\"noUncheckedIndexedAccess\": true", tsconfig, StringComparison.Ordinal);
        Assert.Contains("\"@/*\": [\"./src/*\"]", tsconfig, StringComparison.Ordinal);

        var vite = await ReadTextAsync(fixture, $"{BlueprintId}\\templates\\vite.config.ts");
        Assert.Contains("'@': path.resolve(rootDirectory, 'src')", vite, StringComparison.Ordinal);
        Assert.Contains("environment: 'jsdom'", vite, StringComparison.Ordinal);

        var envExample = await ReadTextAsync(fixture, $"{BlueprintId}\\templates\\.env.example");
        Assert.Equal("VITE_API_BASE_URL=\n", envExample.Replace("\r\n", "\n", StringComparison.Ordinal));
        var gitignore = await ReadTextAsync(fixture, $"{BlueprintId}\\templates\\.gitignore");
        Assert.Contains(".env*", gitignore, StringComparison.Ordinal);
        Assert.Contains("!.env.example", gitignore, StringComparison.Ordinal);

        var env = await ReadTextAsync(fixture, $"{BlueprintId}\\overlays\\base\\src\\config\\env.ts");
        Assert.Contains("publicEnvironmentSchema", env, StringComparison.Ordinal);
        Assert.Contains("z.url()", env, StringComparison.Ordinal);
        var api = await ReadTextAsync(fixture, $"{BlueprintId}\\overlays\\base\\src\\services\\apiClient.ts");
        Assert.Contains("publicEnvironment.VITE_API_BASE_URL", api, StringComparison.Ordinal);
        Assert.Contains("response.ok", api, StringComparison.Ordinal);

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
            Assert.DoesNotContain("--registry", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("pnpm dlx", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("pnpm exec", content, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ReviewedProductionDistIsCommittedAndEngineEvidenceIsExcludedFromFormatting()
    {
        using var fixture = await ProductionBlueprintCatalogFixture.CreateAsync();
        await fixture.Catalog.RefreshAsync(CancellationToken.None);
        var blueprint = Assert.Single(
            await fixture.Catalog.ListAsync(CancellationToken.None),
            candidate => candidate.Manifest.Id == BlueprintId);
        Assert.Contains(blueprint.Manifest.Artifacts, artifact => artifact.Path == "dist\\index.html");

        var package = await ReadTextAsync(fixture, $"{BlueprintId}\\templates\\package.json");
        Assert.Contains("\"build\": \"vite build\"", package, StringComparison.Ordinal);
        var gitignore = await ReadTextAsync(fixture, $"{BlueprintId}\\templates\\.gitignore");
        Assert.DoesNotContain("dist/", gitignore, StringComparison.Ordinal);
        var contributing = await ReadTextAsync(fixture, $"{BlueprintId}\\templates\\CONTRIBUTING.md");
        Assert.Contains("Commit the reviewed `dist` production output", contributing, StringComparison.Ordinal);
        Assert.DoesNotContain("Never commit `.env`, credentials, generated `dist`", contributing,
            StringComparison.OrdinalIgnoreCase);
        var deployment = await ReadTextAsync(fixture, $"{BlueprintId}\\templates\\DEPLOYMENT.md");
        Assert.Contains("commit the complete reviewed `dist` output", deployment, StringComparison.Ordinal);

        var prettierIgnore = (await ReadTextAsync(
                fixture,
                $"{BlueprintId}\\templates\\.prettierignore"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Equal(
            "coverage\ndist\nnode_modules\npnpm-lock.yaml\n.devforge/\ndevforge.lock.json\n" +
            "generation-report.json\npolicy.snapshot.json\n",
            prettierIgnore);
    }

    private static ProjectRecipe Recipe(string targetPath) => ProjectRecipe.Create(new ProjectRecipeDraft(
        "Team Portal",
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
