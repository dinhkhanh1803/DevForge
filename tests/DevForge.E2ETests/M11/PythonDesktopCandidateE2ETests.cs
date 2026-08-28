using System.IO;
using System.Security.Cryptography;
using DevForge.Application.Contracts;
using DevForge.Domain.Runs;
using DevForge.E2ETests.M9;
using DevForge.Infrastructure.FileSystem;
using DevForge.Infrastructure.Processes;
using Xunit.Abstractions;

namespace DevForge.E2ETests.M11;

[Collection(M9ExecutionTestGroup.Name)]
public sealed class PythonDesktopCandidateE2ETests(ITestOutputHelper output)
{
    [Fact]
    public async Task FinalPathNativeToolchainPassesWithProductionEnvironment()
    {
        await using var fixture = await WpfBlueprintFixture.CreatePythonDesktopCandidateAsync();
        var plan = await fixture.Workflow.CreatePlanAsync(fixture.Draft, CancellationToken.None);
        Assert.True(plan.IsValid);
        var generated = await fixture.Workflow.ExecuteAsync(plan.Value, null, CancellationToken.None);
        Assert.True(generated.IsValid);
        Assert.Equal(RunStatus.LocalReady, generated.Value.Checkpoint.Run.Status);
        var workspace = await new WindowsFileSystem().OpenWorkspaceAsync(
            WorkspaceRoot.Create(fixture.TargetPath).Value, CancellationToken.None);
        var runner = new WindowsProcessRunner();
        var commands = fixture.Runner.Commands.DistinctBy(command => string.Join('\n', command.ArgumentList)).ToArray();
        Assert.Equal(8, commands.Length);
        foreach (var original in commands)
        {
            var command = CommandSpec.CreateAtWorkspaceRoot(original.Executable, original.ArgumentList,
                workspace, [],
                original.Timeout, [0], []).Value;
            var result = await runner.RunAsync(command, null, CancellationToken.None);
            output.WriteLine(string.Join(' ', command.ArgumentList) + ": " + result.TerminationReason + "/" + result.ExitCode);
            output.WriteLine(string.Join(Environment.NewLine, result.RetainedLines.Select(line => line.Text.Value)));
            Assert.True(result.TerminationReason == ProcessTerminationReason.Exited && result.ExitCode == 0);
        }
        Assert.True(File.Exists(Path.Combine(fixture.TargetPath, ".venv", "Scripts", "team-desktop.exe")));
        // A developer creates a fresh environment at the final path; no staging environment is shipped.
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Category", "ReleaseAcceptance")]
    public async Task ProductionUvWorkflowMustReachCleanPublicationWithoutEnvironmentInjection(bool desktop)
    {
        await using var fixture = desktop
            ? await WpfBlueprintFixture.CreatePythonDesktopCandidateAsync(new ObservingRunner(output))
            : await WpfBlueprintFixture.CreatePythonAsync(new ObservingRunner(output));
        var plan = await fixture.Workflow.CreatePlanAsync(fixture.Draft, CancellationToken.None);
        Assert.True(plan.IsValid);
        var run = await fixture.Workflow.ExecuteAsync(plan.Value, null, CancellationToken.None);
        Assert.True(run.IsValid);
        Assert.True(run.Value.Checkpoint.Run.Status == RunStatus.LocalReady,
            string.Join("; ", run.Value.Checkpoint.Run.Errors.Select(error => error.Code + ": " + error.Summary)));
        foreach (var directory in new[] { ".venv", ".ruff_cache", ".mypy_cache", ".pytest_cache", "tooling" })
        {
            Assert.False(Directory.Exists(Path.Combine(fixture.TargetPath, directory)), directory);
        }
        Assert.Empty(Directory.EnumerateDirectories(fixture.TargetPath, "__pycache__", SearchOption.AllDirectories));
        Assert.Equal(2, Directory.GetFiles(Path.Combine(fixture.TargetPath, "dist")).Length);
        Assert.True(File.Exists(Path.Combine(fixture.TargetPath, ".devforge", "build-outputs.json")));
        Assert.False(Directory.Exists(Path.Combine(Path.GetDirectoryName(fixture.TargetPath)!, ".devforge-staging", plan.Value.RunId)));
        var published = await fixture.PublishLocalAsync(plan.Value.RunId);
        Assert.True(published.IsSuccessful, published.Error?.Code + ": " + published.Error?.Summary);
        var verified = await fixture.PublishLocalAsync(plan.Value.RunId);
        Assert.True(verified.IsSuccessful, verified.Error?.Summary);
        Assert.Equal(RunStatus.Completed, verified.Value.Run.Status);
        var wheel = Assert.Single(Directory.GetFiles(Path.Combine(fixture.TargetPath, "dist"), "*.whl"));
        var original = await File.ReadAllBytesAsync(wheel);
        await File.WriteAllBytesAsync(wheel, [.. original, 1]);
        Assert.False((await fixture.PublishLocalAsync(plan.Value.RunId)).IsSuccessful);
        await File.WriteAllBytesAsync(wheel, original);
        Assert.True((await fixture.PublishLocalAsync(plan.Value.RunId)).IsSuccessful);
        Assert.Equal(0, fixture.RemoteGitHub.Calls);
    }

    [Fact]
    public async Task CompositionProducesDeterministicSourceAndCleanRecoverableLocalGit()
    {
        await using var first = await WpfBlueprintFixture.CreatePythonDesktopCandidateAsync();
        await using var second = await WpfBlueprintFixture.CreatePythonDesktopCandidateAsync();
        var plan = await first.Workflow.CreatePlanAsync(first.Draft, CancellationToken.None);
        var other = await second.Workflow.CreatePlanAsync(second.Draft, CancellationToken.None);
        Assert.True(plan.IsValid);
        Assert.True(other.IsValid);
        Assert.Equal(plan.Value.PlannedProject.Plan.Id, other.Value.PlannedProject.Plan.Id);
        var run = await first.Workflow.ExecuteAsync(plan.Value, null, CancellationToken.None);
        var otherRun = await second.Workflow.ExecuteAsync(other.Value, null, CancellationToken.None);
        Assert.True(run.IsValid);
        Assert.True(otherRun.IsValid);
        Assert.Equal(RunStatus.LocalReady, run.Value.Checkpoint.Run.Status);
        Assert.Equal(RunStatus.LocalReady, otherRun.Value.Checkpoint.Run.Status);
        Assert.Equal(Tree(first.TargetPath), Tree(second.TargetPath));
        Assert.Equal(15, first.Runner.Commands.Length);
        Assert.Contains(first.Runner.Commands, command => command.ArgumentList.Contains("team-desktop"));
        var published = await first.PublishLocalAsync(plan.Value.RunId);
        Assert.True(published.IsSuccessful, published.Error?.Summary);
        var verified = await first.PublishLocalAsync(plan.Value.RunId);
        Assert.True(verified.IsSuccessful, verified.Error?.Summary);
        Assert.Equal(RunStatus.Completed, verified.Value.Run.Status);
        Assert.Equal(0, first.RemoteGitHub.Calls);
        // These commands are simulated: source-only composition is not real toolchain certification.
    }

    [Theory]
    [InlineData(null, RunStatus.Failed)]
    [InlineData("run", RunStatus.ValidationFailed)]
    public async Task FailurePreservesAbsentTargetAndOwnedCleanup(string? operation, RunStatus expected)
    {
        await using var fixture = await WpfBlueprintFixture.CreatePythonDesktopCandidateAsync();
        var plan = await fixture.Workflow.CreatePlanAsync(fixture.Draft, CancellationToken.None);
        Assert.True(plan.IsValid);
        fixture.Runner.FailNext(operation);
        var run = await fixture.Workflow.ExecuteAsync(plan.Value, null, CancellationToken.None);
        Assert.True(run.IsValid);
        Assert.Equal(expected, run.Value.Checkpoint.Run.Status);
        Assert.False(Directory.Exists(fixture.TargetPath));
        await fixture.CleanupFailedAsync(run.Value.Checkpoint);
        Assert.False(Directory.Exists(fixture.TargetPath));
    }

    [Fact]
    public async Task OccupiedTargetIsPreservedBeforeAnyProcessRuns()
    {
        await using var fixture = await WpfBlueprintFixture.CreatePythonDesktopCandidateAsync();
        Directory.CreateDirectory(fixture.TargetPath);
        var sentinel = Path.Combine(fixture.TargetPath, "keep.txt");
        await File.WriteAllTextAsync(sentinel, "user-data");
        Assert.False((await fixture.Workflow.CreatePlanAsync(fixture.Draft, CancellationToken.None)).IsValid);
        Assert.Equal("user-data", await File.ReadAllTextAsync(sentinel));
        Assert.Empty(fixture.Runner.Commands);
    }

    private static string[] Tree(string path) => Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
        .Where(file => Path.GetFileName(file) is not ("generation-report.json" or "generation-report.md"
            or "devforge.lock.json" or "project.recipe.yaml"))
        .Select(file => Path.GetRelativePath(path, file) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))))
        .Order(StringComparer.Ordinal).ToArray();

    private sealed class ObservingRunner(ITestOutputHelper output) : IProcessRunner
    {
        private readonly WindowsProcessRunner _runner = new();
        public Task CheckPreconditionsAsync(CommandSpec command, CancellationToken cancellationToken) =>
            _runner.CheckPreconditionsAsync(command, cancellationToken);
        public async Task<ProcessResult> RunAsync(CommandSpec command, IProgress<ProcessOutputLine>? progress, CancellationToken cancellationToken)
        {
            var result = await _runner.RunAsync(command, progress, cancellationToken);
            output.WriteLine(string.Join(' ', command.ArgumentList) + ": " + result.TerminationReason + "/" + result.ExitCode);
            output.WriteLine(string.Join(Environment.NewLine, result.RetainedLines.Select(line => line.Text.Value)));
            return result;
        }
    }
}
