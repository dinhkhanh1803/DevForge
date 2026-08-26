using DevForge.Application.Contracts;

namespace DevForge.BlueprintTests.Production;

public sealed class SharedHandoffContractTests
{
    private static readonly string[] _blueprintIds =
    [
        "desktop.csharp-wpf-tool",
        "web.react-vite-ts",
        "tool.python-cli",
    ];

    private static readonly IReadOnlyDictionary<string, string[]> _requiredSections =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["README.md"] = ["## Repository layout", "## Local setup", "## Quality gates"],
            ["ARCHITECTURE.md"] = ["## Boundaries", "## Repository layout", "## Decision records"],
            ["CONTRIBUTING.md"] =
            [
                "## Workflow", "## Branches and commits", "## Review", "## Quality gates",
            ],
            ["DEVELOPMENT.md"] =
            [
                "## Prerequisites", "## Local setup", "## Environment", "## Database", "## Debugging",
            ],
            ["TESTING.md"] = ["## Test levels", "## Release gate"],
            ["DEPLOYMENT.md"] = ["## Release preparation", "## Rollback"],
            ["TEAM_START_HERE.md"] = ["## First-day checklist"],
        };

    [Fact]
    public async Task EveryProductionBlueprintCarriesTheSharedTruthfulHandoffStandard()
    {
        using var fixture = await ProductionBlueprintCatalogFixture.CreateAsync();

        foreach (var blueprintId in _blueprintIds)
        {
            foreach (var document in _requiredSections)
            {
                var content = await ReadAsync(
                    fixture,
                    $"{blueprintId}\\templates\\{document.Key}");
                foreach (var section in document.Value)
                {
                    Assert.Contains(section, content, StringComparison.Ordinal);
                }

                Assert.DoesNotContain("TODO", content, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("placeholder", content, StringComparison.OrdinalIgnoreCase);
            }

            var testing = await ReadAsync(fixture, $"{blueprintId}\\templates\\TESTING.md");
            Assert.Contains("unit", testing, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                "No dedicated integration test suite exists yet.",
                testing,
                StringComparison.Ordinal);
            foreach (var command in RequiredCommands(blueprintId))
            {
                Assert.Contains(command, testing, StringComparison.Ordinal);
            }

            var architecture = await ReadAsync(
                fixture,
                $"{blueprintId}\\templates\\ARCHITECTURE.md");
            Assert.Contains("No project-specific ADRs exist at generation time.", architecture, StringComparison.Ordinal);
            Assert.Contains(
                "Future accepted ADRs must use repository-relative links",
                architecture,
                StringComparison.Ordinal);
            Assert.DoesNotContain("[Decision records](#decision-records)", architecture, StringComparison.Ordinal);

            var contributing = await ReadAsync(
                fixture,
                $"{blueprintId}\\templates\\CONTRIBUTING.md");
            Assert.Contains("short-lived branch", contributing, StringComparison.Ordinal);
            Assert.Contains("focused commit", contributing, StringComparison.Ordinal);
            Assert.Contains("review", contributing, StringComparison.OrdinalIgnoreCase);

            var development = await ReadAsync(
                fixture,
                $"{blueprintId}\\templates\\DEVELOPMENT.md");
            Assert.Contains("No database is used by this blueprint.", development, StringComparison.Ordinal);
            Assert.Contains("Debug", development, StringComparison.Ordinal);

            var gitignore = await ReadAsync(fixture, $"{blueprintId}\\templates\\.gitignore");
            if (blueprintId != "desktop.csharp-wpf-tool")
            {
                var example = await ReadAsync(fixture, $"{blueprintId}\\templates\\.env.example");
                Assert.All(
                    example.Replace("\r\n", "\n", StringComparison.Ordinal)
                        .Split('\n', StringSplitOptions.RemoveEmptyEntries),
                    line => Assert.EndsWith("=", line, StringComparison.Ordinal));
                Assert.Contains(".env*", gitignore, StringComparison.Ordinal);
                Assert.Contains("!.env.example", gitignore, StringComparison.Ordinal);
            }
        }
    }

    private static string[] RequiredCommands(string blueprintId) => blueprintId switch
    {
        "desktop.csharp-wpf-tool" =>
        [
            "dotnet restore TeamTool.slnx --locked-mode",
            "dotnet format TeamTool.slnx --verify-no-changes --no-restore",
            "dotnet build TeamTool.slnx --configuration Release --no-restore",
            "dotnet test TeamTool.slnx --configuration Release --no-build --no-restore",
            "dotnet publish src/TeamTool.Desktop/TeamTool.Desktop.csproj",
        ],
        "web.react-vite-ts" =>
        [
            "pnpm install --frozen-lockfile --ignore-scripts",
            "pnpm run lint",
            "pnpm run typecheck",
            "pnpm run test",
            "pnpm run build",
        ],
        "tool.python-cli" =>
        [
            "uv sync --frozen --no-config",
            "uv run --frozen --no-sync --no-config ruff format --check .",
            "uv run --frozen --no-sync --no-config ruff check .",
            "uv run --frozen --no-sync --no-config mypy src tests",
            "uv run --frozen --no-sync --no-config pytest",
            "uv run --frozen --no-sync --no-config pyproject-build --no-isolation",
            "uv run --frozen --no-sync --no-config team-tool --help",
        ],
        _ => throw new InvalidOperationException("Unexpected production blueprint."),
    };

    private static async Task<string> ReadAsync(
        ProductionBlueprintCatalogFixture fixture,
        string path)
    {
        await using var stream = await fixture.Source.Workspace.OpenReadAsync(
            WorkspaceRelativePath.Create(path).Value,
            CancellationToken.None);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(CancellationToken.None);
    }
}
