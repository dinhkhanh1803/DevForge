using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DevForge.Application.Contracts;
using DevForge.Domain.Runs;

namespace DevForge.E2ETests.M9;

[Collection(M9ExecutionTestGroup.Name)]
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
        Assert.True(File.Exists(Path.Combine(
            firstFixture.TargetPath,
            ".devforge",
            "project.recipe.yaml")));
        var lockPath = Path.Combine(firstFixture.TargetPath, "devforge.lock.json");
        var reportPath = Path.Combine(firstFixture.TargetPath, "generation-report.json");
        var policyPath = Path.Combine(firstFixture.TargetPath, "policy.snapshot.json");
        Assert.True(File.Exists(lockPath));
        Assert.True(File.Exists(reportPath));
        Assert.True(File.Exists(policyPath));
        using var lockDocument = JsonDocument.Parse(await File.ReadAllBytesAsync(lockPath));
        Assert.Equal("desktop.csharp-wpf-tool", lockDocument.RootElement
            .GetProperty("blueprint").GetProperty("id").GetString());
        Assert.Equal("1.0.0", lockDocument.RootElement
            .GetProperty("blueprint").GetProperty("version").GetString());
        Assert.Equal(firstPlan.Value.PlannedProject.Plan.Id, lockDocument.RootElement
            .GetProperty("planHash").GetString());
        Assert.StartsWith("sha256:", lockDocument.RootElement
            .GetProperty("blueprint").GetProperty("checksum").GetString(), StringComparison.Ordinal);
        using var reportDocument = JsonDocument.Parse(await File.ReadAllBytesAsync(reportPath));
        Assert.Contains(
            reportDocument.RootElement.GetProperty("validations").EnumerateArray(),
            item => item.GetProperty("id").GetString() == "whole-payload-secret-scan"
                && item.GetProperty("status").GetString() == "Passed");
        Assert.Equal(
            firstPlan.Value.PlannedProject.Preview.Artifacts
                .Select(item => item.Path.Replace('\\', '/'))
                .Order(StringComparer.Ordinal),
            reportDocument.RootElement.GetProperty("artifacts").EnumerateArray()
                .Select(item => item.GetString())
                .Order(StringComparer.Ordinal));
        Assert.DoesNotContain(
            reportDocument.RootElement.GetProperty("artifacts").EnumerateArray(),
            item => ProjectEvidencePathPolicy.CanonicalPaths.Any(path =>
                StringComparer.Ordinal.Equals(
                    path.Value.Replace('\\', '/'),
                    item.GetString())));
        using var policyDocument = JsonDocument.Parse(await File.ReadAllBytesAsync(policyPath));
        Assert.Equal(
            firstPlan.Value.PlannedProject.Preview.RequiredTools.Length,
            policyDocument.RootElement.GetProperty("tools").GetArrayLength());
        Assert.Equal(
            firstPlan.Value.PlannedProject.Preview.Dependencies.Length,
            policyDocument.RootElement.GetProperty("dependencies").GetArrayLength());
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

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class M9ExecutionTestGroup
{
    public const string Name = "M9 execution E2E";
}
