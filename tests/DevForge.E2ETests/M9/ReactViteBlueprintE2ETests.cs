using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DevForge.Domain.Runs;

namespace DevForge.E2ETests.M9;

[Collection(M9ExecutionTestGroup.Name)]
public sealed class ReactViteBlueprintE2ETests
{
    [Fact]
    public async Task ReviewedReactBlueprintGeneratesTheSameLockedTreeTwiceThroughProductionComposition()
    {
        await using var firstFixture = await WpfBlueprintFixture.CreateReactAsync();
        await using var secondFixture = await WpfBlueprintFixture.CreateReactAsync();

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
        Assert.Equal(TreeDigest(firstFixture.TargetPath), TreeDigest(secondFixture.TargetPath));
        Assert.Equal(
            ["install", "run", "run", "run", "run", "run", "run", "run", "run"],
            firstFixture.Runner.Commands.Select(command => command.ArgumentList[0]));
        Assert.Equal(
            ["lint", "lint", "typecheck", "typecheck", "test", "test", "build", "build"],
            firstFixture.Runner.Commands.Skip(1).Select(command => command.ArgumentList[1]));
        Assert.True(File.Exists(Path.Combine(firstFixture.TargetPath, "pnpm-lock.yaml")));
        Assert.True(File.Exists(Path.Combine(firstFixture.TargetPath, ".env.example")));
        Assert.False(File.Exists(Path.Combine(firstFixture.TargetPath, ".env")));
        Assert.False(Directory.Exists(Path.Combine(firstFixture.TargetPath, ".git")));
        using var package = JsonDocument.Parse(await File.ReadAllBytesAsync(
            Path.Combine(firstFixture.TargetPath, "package.json")));
        Assert.Equal("team-portal", package.RootElement.GetProperty("name").GetString());
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
