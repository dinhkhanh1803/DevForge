using System.IO;
using System.Security.Cryptography;
using System.Text;
using DevForge.Domain.Runs;

namespace DevForge.E2ETests.M9;

[Collection(M9ExecutionTestGroup.Name)]
public sealed class PythonCliBlueprintE2ETests
{
    private const string ExpectedTreeDigest =
        "5a44e56c34c1c20fa068368120d4352dbd0e62cb39e9678169c7c8d82cc534f3";
    private static readonly string[] _expectedPaths =
    [
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
    ];

    [Fact]
    public async Task ReviewedPythonBlueprintGeneratesTheSameLockedTreeTwiceThroughProductionComposition()
    {
        await using var firstFixture = await WpfBlueprintFixture.CreatePythonAsync();
        await using var secondFixture = await WpfBlueprintFixture.CreatePythonAsync();

        var firstPlan = await firstFixture.Workflow.CreatePlanAsync(firstFixture.Draft, CancellationToken.None);
        var secondPlan = await secondFixture.Workflow.CreatePlanAsync(secondFixture.Draft, CancellationToken.None);
        Assert.True(firstPlan.IsValid);
        Assert.True(secondPlan.IsValid);
        Assert.Equal(firstPlan.Value.PlannedProject.Plan.Id, secondPlan.Value.PlannedProject.Plan.Id);

        var firstRun = await firstFixture.Workflow.ExecuteAsync(firstPlan.Value, null, CancellationToken.None);
        var secondRun = await secondFixture.Workflow.ExecuteAsync(secondPlan.Value, null, CancellationToken.None);

        Assert.True(firstRun.IsValid);
        Assert.True(secondRun.IsValid);
        Assert.Equal(RunStatus.LocalReady, firstRun.Value.Checkpoint.Run.Status);
        var firstDigest = TreeDigest(firstFixture.TargetPath);
        Assert.Equal(firstDigest, TreeDigest(secondFixture.TargetPath));
        var observedPaths = Directory.EnumerateFiles(
                firstFixture.TargetPath,
                "*",
                SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(firstFixture.TargetPath, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(_expectedPaths, observedPaths);
        Assert.Equal(ExpectedTreeDigest, firstDigest);

        var reviewedValidators = new[]
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
        Assert.Equal(
            ["sync", "--frozen", "--no-config"],
            firstFixture.Runner.Commands[0].ArgumentList.ToArray());
        Assert.Equal(1 + (reviewedValidators.Length * 2), firstFixture.Runner.Commands.Length);
        for (var index = 0; index < reviewedValidators.Length; index++)
        {
            Assert.Equal(
                reviewedValidators[index],
                firstFixture.Runner.Commands[(index * 2) + 1].ArgumentList.ToArray());
            Assert.Equal(
                reviewedValidators[index],
                firstFixture.Runner.Commands[(index * 2) + 2].ArgumentList.ToArray());
        }

        Assert.True(File.Exists(Path.Combine(firstFixture.TargetPath, "uv.lock")));
        Assert.True(File.Exists(Path.Combine(firstFixture.TargetPath, ".env.example")));
        Assert.False(File.Exists(Path.Combine(firstFixture.TargetPath, ".env")));
        Assert.False(Directory.Exists(Path.Combine(firstFixture.TargetPath, ".git")));
        var pyproject = await File.ReadAllTextAsync(Path.Combine(firstFixture.TargetPath, "pyproject.toml"));
        Assert.Contains("name = \"team-tool\"", pyproject, StringComparison.Ordinal);
    }

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
}
