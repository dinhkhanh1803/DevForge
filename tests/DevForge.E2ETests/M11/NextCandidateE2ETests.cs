using System.IO;
using System.Security.Cryptography;
using DevForge.Application.Contracts;
using DevForge.Domain.Runs;
using DevForge.E2ETests.M9;
using DevForge.Infrastructure.Processes;
using Xunit.Abstractions;

namespace DevForge.E2ETests.M11;

[Collection(M9ExecutionTestGroup.Name)]
public sealed class NextCandidateE2ETests(ITestOutputHelper output)
{
    [Fact]
    public async Task CompositionIsDeterministicSourceOnlyAndPublicationIsRecoverable()
    {
        await using var first = await WpfBlueprintFixture.CreateNextCandidateAsync();
        await using var second = await WpfBlueprintFixture.CreateNextCandidateAsync();
        var plan = await first.Workflow.CreatePlanAsync(first.Draft, default);
        var other = await second.Workflow.CreatePlanAsync(second.Draft, default);
        Assert.True(plan.IsValid);
        Assert.True(other.IsValid);
        Assert.Equal(plan.Value.PlannedProject.Plan.Id, other.Value.PlannedProject.Plan.Id);
        var run = await first.Workflow.ExecuteAsync(plan.Value, null, default);
        var otherRun = await second.Workflow.ExecuteAsync(other.Value, null, default);
        Assert.True(run.IsValid);
        Assert.True(otherRun.IsValid);
        Assert.Equal(RunStatus.LocalReady, run.Value.Checkpoint.Run.Status);
        Assert.Equal(RunStatus.LocalReady, otherRun.Value.Checkpoint.Run.Status);
        Assert.Equal(SourceTree(first.TargetPath), SourceTree(second.TargetPath));
        Assert.Equal(13, first.Runner.Commands.Length);
        Assert.True((await first.PublishLocalAsync(plan.Value.RunId)).IsSuccessful);
        Assert.True((await first.PublishLocalAsync(plan.Value.RunId)).IsSuccessful);
        Assert.Equal(0, first.RemoteGitHub.Calls);
        AssertSourceOnly(first.TargetPath);
        // Simulated validators prove composition, never native toolchain acceptance.
    }

    [Theory]
    [InlineData(null, RunStatus.Failed)]
    [InlineData("run", RunStatus.ValidationFailed)]
    public async Task FailureDoesNotFinalizeAndCleanupRemainsOwned(string? command, RunStatus expected)
    {
        await using var fixture = await WpfBlueprintFixture.CreateNextCandidateAsync();
        var plan = await fixture.Workflow.CreatePlanAsync(fixture.Draft, default);
        Assert.True(plan.IsValid);
        fixture.Runner.FailNext(command);
        var run = await fixture.Workflow.ExecuteAsync(plan.Value, null, default);
        Assert.True(run.IsValid);
        Assert.Equal(expected, run.Value.Checkpoint.Run.Status);
        Assert.False(Directory.Exists(fixture.TargetPath));
        await fixture.CleanupFailedAsync(run.Value.Checkpoint);
        Assert.False(Directory.Exists(fixture.TargetPath));
    }

    [Fact]
    public async Task OccupiedTargetIsNeverOverwritten()
    {
        await using var fixture = await WpfBlueprintFixture.CreateNextCandidateAsync();
        Directory.CreateDirectory(fixture.TargetPath);
        var sentinel = Path.Combine(fixture.TargetPath, "keep.txt");
        await File.WriteAllTextAsync(sentinel, "user-data");
        Assert.False((await fixture.Workflow.CreatePlanAsync(fixture.Draft, default)).IsValid);
        Assert.Equal("user-data", await File.ReadAllTextAsync(sentinel));
        Assert.Empty(fixture.Runner.Commands);
    }

    [Fact]
    [Trait("Category", "ReleaseAcceptance")]
    public async Task RealNextProductionWorkflowPassesEveryGateAndSourcePublicationRecovery()
    {
        var runner = new ObservingRunner(output);
        await using var fixture = await WpfBlueprintFixture.CreateNextCandidateAsync(runner);
        var plan = await fixture.Workflow.CreatePlanAsync(fixture.Draft, default);
        Assert.True(plan.IsValid);
        var run = await fixture.Workflow.ExecuteAsync(plan.Value, null, default);
        Assert.True(run.IsValid);
        Assert.True(run.Value.Checkpoint.Run.Status == RunStatus.LocalReady,
            string.Join("; ", run.Value.Checkpoint.Run.Errors.Select(error => error.Code + ": " + error.Summary)));
        Assert.Equal(13, runner.SuccessfulCommands);
        AssertSourceOnly(fixture.TargetPath);
        Assert.False(Directory.Exists(Path.Combine(Path.GetDirectoryName(fixture.TargetPath)!, ".devforge-staging", plan.Value.RunId)));
        Assert.True((await fixture.PublishLocalAsync(plan.Value.RunId)).IsSuccessful);
        Assert.True((await fixture.PublishLocalAsync(plan.Value.RunId)).IsSuccessful);
        foreach (var relative in new[] { "package.json", "pnpm-lock.yaml", "src/app/page.tsx", "scripts/smoke.mjs" })
        {
            var path = Path.Combine(fixture.TargetPath, relative);
            var original = await File.ReadAllBytesAsync(path);
            await File.WriteAllBytesAsync(path, [.. original, 32]);
            Assert.False((await fixture.PublishLocalAsync(plan.Value.RunId)).IsSuccessful);
            await File.WriteAllBytesAsync(path, original);
            Assert.True((await fixture.PublishLocalAsync(plan.Value.RunId)).IsSuccessful);
        }
        Assert.Equal(0, fixture.RemoteGitHub.Calls);
    }

    private static void AssertSourceOnly(string path)
    {
        foreach (var directory in new[] { "node_modules", ".next", ".devforge-node", "dist", "tooling" })
        {
            Assert.False(Directory.Exists(Path.Combine(path, directory)), directory);
        }
    }

    private static string[] SourceTree(string path) => Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
        .Where(file => Path.GetFileName(file) is not ("generation-report.json" or "generation-report.md"
            or "devforge.lock.json" or "project.recipe.yaml"))
        .Select(file => Path.GetRelativePath(path, file) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))))
        .Order(StringComparer.Ordinal).ToArray();

    private sealed class ObservingRunner(ITestOutputHelper output) : IProcessRunner
    {
        private readonly WindowsProcessRunner _runner = new();
        public int SuccessfulCommands { get; private set; }
        public Task CheckPreconditionsAsync(CommandSpec command, CancellationToken cancellationToken) =>
            _runner.CheckPreconditionsAsync(command, cancellationToken);

        public async Task<ProcessResult> RunAsync(CommandSpec command, IProgress<ProcessOutputLine>? progress, CancellationToken cancellationToken)
        {
            if (SuccessfulCommands == 0)
            {
                foreach (var (tool, version) in new[] { ("node", "v22.23.2"), ("pnpm", "10.24.0") })
                {
                    var probe = CommandSpec.CreateAtWorkspaceRoot(ExecutableIdentity.Create(tool).Value,
                        ["--version"], command.Workspace, [], TimeSpan.FromSeconds(30), [0], []).Value;
                    var observed = await _runner.RunAsync(probe, null, cancellationToken);
                    Assert.Equal(0, observed.ExitCode);
                    Assert.Contains(observed.RetainedLines, line => line.Text.Value.Trim() == version);
                }
            }
            var result = await _runner.RunAsync(command, progress, cancellationToken);
            output.WriteLine(string.Join(' ', command.ArgumentList) + ": " + result.TerminationReason + "/" + result.ExitCode);
            output.WriteLine(string.Join(Environment.NewLine, result.RetainedLines.Select(line => line.Text.Value)));
            if (result.ExitCode == 0) { SuccessfulCommands++; }
            return result;
        }
    }
}
