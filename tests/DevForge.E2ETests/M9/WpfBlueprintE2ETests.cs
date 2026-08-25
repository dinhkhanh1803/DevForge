using System.IO;
using System.Security.Cryptography;
using System.Text;
using DevForge.Domain.Runs;

namespace DevForge.E2ETests.M9;

public sealed class WpfBlueprintE2ETests
{
    [Fact]
    public async Task ReviewedWpfBlueprintGeneratesTheSameTreeTwiceThroughProductionComposition()
    {
        await using var firstFixture = await WpfBlueprintFixture.CreateAsync();
        await using var secondFixture = await WpfBlueprintFixture.CreateAsync();

        var firstPlan = await firstFixture.Workflow.CreatePlanAsync(firstFixture.Draft, CancellationToken.None);
        var secondPlan = await secondFixture.Workflow.CreatePlanAsync(secondFixture.Draft, CancellationToken.None);
        Assert.True(firstPlan.IsValid);
        Assert.True(secondPlan.IsValid);
        Assert.Equal(firstPlan.Value.PlannedProject.Plan.Id, secondPlan.Value.PlannedProject.Plan.Id);

        var firstRun = await firstFixture.Workflow.ExecuteAsync(firstPlan.Value, null, CancellationToken.None);
        var secondRun = await secondFixture.Workflow.ExecuteAsync(secondPlan.Value, null, CancellationToken.None);

        Assert.True(firstRun.IsValid);
        Assert.True(secondRun.IsValid);
        Assert.True(
            firstRun.Value.Checkpoint.Run.Status == RunStatus.LocalReady,
            string.Join(
                ", ",
                firstRun.Value.Checkpoint.Evidence.Select(item => $"{item.Kind}:{item.Id}:{item.Status}"))
            + " | commands: "
            + string.Join("; ", firstFixture.Runner.Commands.Select(command => string.Join(" ", command.ArgumentList))));
        Assert.Equal(TreeDigest(firstFixture.TargetPath), TreeDigest(secondFixture.TargetPath));
        Assert.Equal(
            ["restore", "format", "format", "build", "build", "test", "test", "publish", "publish"],
            firstFixture.Runner.Commands.Select(command => command.ArgumentList[0]));
        Assert.All(firstFixture.Runner.Commands, command => Assert.Equal(0, Assert.Single(command.AllowedExitCodes)));
        Assert.True(File.Exists(Path.Combine(firstFixture.TargetPath, "src", "TeamTool.Desktop", "App.xaml")));
        Assert.True(File.Exists(Path.Combine(firstFixture.TargetPath, "Directory.Packages.props")));
        Assert.True(File.Exists(Path.Combine(firstFixture.TargetPath, "TEAM_START_HERE.md")));
        Assert.False(Directory.Exists(Path.Combine(firstFixture.TargetPath, ".git")));
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
