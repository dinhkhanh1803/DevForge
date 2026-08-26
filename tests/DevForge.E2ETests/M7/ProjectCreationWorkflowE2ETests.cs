using System.IO;
using System.Text.Json;
using DevForge.Application.Contracts;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Runs;

namespace DevForge.E2ETests.M7;

[Collection(M7ExecutionTestGroup.Name)]
public sealed class ProjectCreationWorkflowE2ETests
{
    [Fact]
    public async Task TrustedLocalBlueprintReachesLocalReadyWithoutTerminalOrGitSideEffects()
    {
        await using var fixture = await M7BlueprintFixture.CreateAsync();
        var catalog = await fixture.Workflow.LoadCatalogAsync(
            forceRefresh: false,
            CancellationToken.None);
        var inputKinds = Assert.Single(catalog.ExecutableBlueprints).InputSchema
            .Select(item => item.Kind)
            .ToHashSet();
        Assert.Equal(4, inputKinds.Count);
        Assert.Contains(BlueprintInputKind.Text, inputKinds);
        Assert.Contains(BlueprintInputKind.Choice, inputKinds);
        Assert.Contains(BlueprintInputKind.Boolean, inputKinds);
        Assert.Contains(BlueprintInputKind.WholeNumber, inputKinds);

        var plan = await fixture.Workflow.CreatePlanAsync(
            fixture.ValidDraft,
            CancellationToken.None);

        Assert.True(plan.IsValid);
        Assert.False(Directory.Exists(fixture.TargetPath));
        Assert.Equal(4, plan.Value.PlannedProject.Preview.EffectiveInputs.Count);
        Assert.DoesNotContain(
            plan.Value.PlannedProject.Plan.Steps,
            step => step.Handler is "run-process" or "package-install" or "validate-command");
        Assert.Equal(
            ["validate-file-exists", "validate-file-content"],
            plan.Value.PlannedProject.Plan.Validators.Select(item => item.Handler));

        var result = await fixture.Workflow.ExecuteAsync(
            plan.Value,
            progress: null,
            CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal(RunStatus.LocalReady, result.Value.Checkpoint.Run.Status);
        Assert.Equal(
            ["readme-exists", "readme-content"],
            result.Value.Checkpoint.Evidence
                .Where(item => item.Kind == ExecutionEvidenceKind.Validator)
                .Select(item => item.Id));
        Assert.All(
            result.Value.Checkpoint.Evidence.Where(item => item.Kind == ExecutionEvidenceKind.Validator),
            item => Assert.Equal(ExecutionEvidenceStatus.Passed, item.Status));
        Assert.Equal(
            [
                ".devforge\\project.recipe.yaml",
                "README.md",
                "devforge.lock.json",
                "generation-report.json",
                "policy.snapshot.json",
                "src\\Program.txt",
            ],
            Directory.EnumerateFiles(fixture.TargetPath, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(fixture.TargetPath, path))
                .Order(StringComparer.Ordinal));
        Assert.Equal(
            "# M7 Sample\n\nFramework: net10.0\n",
            (await File.ReadAllTextAsync(Path.Combine(fixture.TargetPath, "README.md")))
                .Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.Equal(
            "safe local content\n",
            (await File.ReadAllTextAsync(Path.Combine(fixture.TargetPath, "src", "Program.txt")))
                .Replace("\r\n", "\n", StringComparison.Ordinal));
        using var projectLock = JsonDocument.Parse(await File.ReadAllBytesAsync(
            Path.Combine(fixture.TargetPath, "devforge.lock.json")));
        Assert.Equal(plan.Value.PlannedProject.Plan.Id, projectLock.RootElement
            .GetProperty("planHash").GetString());
        Assert.Equal("m7.test.local", projectLock.RootElement
            .GetProperty("blueprint").GetProperty("id").GetString());
        Assert.Equal(3, projectLock.RootElement.GetProperty("evidenceDigests").GetArrayLength());
        var reportDirectory = Path.GetDirectoryName(fixture.JsonReportPath(plan.Value.RunId))!;
        Assert.Equal(
            [$"{plan.Value.RunId}.json", $"{plan.Value.RunId}.md"],
            Directory.EnumerateFiles(reportDirectory)
                .Select(Path.GetFileName)
                .Order(StringComparer.Ordinal));
        using var json = JsonDocument.Parse(await File.ReadAllBytesAsync(
            fixture.JsonReportPath(plan.Value.RunId)));
        var report = json.RootElement;
        Assert.Equal("devforge-generation-report-v1", report.GetProperty("schema").GetString());
        Assert.Equal(plan.Value.RunId, report.GetProperty("runId").GetString());
        Assert.Equal(plan.Value.PlannedProject.Plan.Id, report.GetProperty("planHash").GetString());
        Assert.Equal("m7.test.local", report.GetProperty("blueprintId").GetString());
        Assert.Equal("1.0.0", report.GetProperty("blueprintVersion").GetString());
        Assert.Equal(3, report.GetProperty("attempts").GetArrayLength());
        Assert.All(
            report.GetProperty("attempts").EnumerateArray(),
            attempt => Assert.Equal("Succeeded", attempt.GetProperty("outcome").GetString()));
        var validations = report.GetProperty("validations").EnumerateArray().ToArray();
        Assert.Equal(
            ["readme-exists", "readme-content", "whole-payload-secret-scan"],
            validations.Select(item => item.GetProperty("id").GetString()));
        Assert.All(validations, item => Assert.Equal("Passed", item.GetProperty("status").GetString()));
        Assert.Equal(
            ["README.md", "src\\Program.txt"],
            report.GetProperty("artifacts").EnumerateArray().Select(item => item.GetString()));
        var markdown = await File.ReadAllTextAsync(fixture.MarkdownReportPath(plan.Value.RunId));
        Assert.Contains(plan.Value.RunId, markdown, StringComparison.Ordinal);
        Assert.Contains(plan.Value.PlannedProject.Plan.Id, markdown, StringComparison.Ordinal);
        Assert.Contains("m7.test.local@1.0.0", markdown, StringComparison.Ordinal);
        Assert.Contains("readme-exists", markdown, StringComparison.Ordinal);
        Assert.Contains("readme-content", markdown, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(fixture.TargetPath, ".git")));
    }

    [Fact]
    public async Task CancellationIsDurableAndResumeDoesNotDuplicatePassedEvidence()
    {
        await using var fixture = await M7BlueprintFixture.CreateAsync();
        var plan = await fixture.Workflow.CreatePlanAsync(
            fixture.ValidDraft,
            CancellationToken.None);
        Assert.True(plan.IsValid);
        using var cancellation = new CancellationTokenSource();
        fixture.CancelBeforeHandler("copy-overlay", cancellation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Workflow.ExecuteAsync(plan.Value, null, cancellation.Token));

        var cancelled = await fixture.CheckpointStore.FindAsync(
            plan.Value.RunId,
            CancellationToken.None);
        Assert.NotNull(cancelled);
        Assert.Equal(RunStatus.Cancelled, cancelled.Run.Status);
        Assert.Equal(2, cancelled.Evidence.Count(item => item.Status == ExecutionEvidenceStatus.Passed));
        Assert.False(Directory.Exists(fixture.TargetPath));

        var resumed = await fixture.RecoveryWorkflow.ContinueAsync(
            plan.Value.RunId,
            ExecutionMode.Resume,
            CancellationToken.None);

        Assert.Equal(RunStatus.LocalReady, resumed.Checkpoint.Run.Status);
        Assert.Equal(3, resumed.Checkpoint.Evidence.Count(item => item.Kind == ExecutionEvidenceKind.Step));
        Assert.Equal(
            resumed.Checkpoint.Evidence.Length,
            resumed.Checkpoint.Evidence
                .Select(item => (item.Kind, item.Id))
                .Distinct()
                .Count());
        Assert.True(File.Exists(Path.Combine(fixture.TargetPath, "README.md")));
    }

    [Fact]
    public async Task ExistingTargetBytesAreRejectedWithoutMutation()
    {
        await using var fixture = await M7BlueprintFixture.CreateAsync();
        Directory.CreateDirectory(fixture.TargetPath);
        var existingPath = Path.Combine(fixture.TargetPath, "owned.txt");
        var original = new byte[] { 0, 1, 2, 3, 255 };
        await File.WriteAllBytesAsync(existingPath, original);

        var plan = await fixture.Workflow.CreatePlanAsync(
            fixture.ValidDraft,
            CancellationToken.None);

        Assert.False(plan.IsValid);
        Assert.Contains(plan.Issues, issue => issue.Code == "project.target.not-empty");
        Assert.Equal(original, await File.ReadAllBytesAsync(existingPath));
        Assert.Equal(["owned.txt"], Directory.EnumerateFiles(fixture.TargetPath).Select(Path.GetFileName));
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class M7ExecutionTestGroup
{
    public const string Name = "M7 execution E2E";
}
