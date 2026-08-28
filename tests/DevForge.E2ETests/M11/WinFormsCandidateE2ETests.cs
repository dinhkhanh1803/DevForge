using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Automation;
using DevForge.Application.Contracts;
using DevForge.Domain.Runs;
using DevForge.E2ETests.M9;
using DevForge.Infrastructure.Processes;
using Xunit.Abstractions;

namespace DevForge.E2ETests.M11;

[Collection(M9ExecutionTestGroup.Name)]
public sealed class WinFormsCandidateE2ETests(ITestOutputHelper output)
{
    [Fact]
    public async Task CompositionProducesDeterministicCandidateTreeAndEngineEvidence()
    {
        await using var first = await WpfBlueprintFixture.CreateWinFormsCandidateAsync();
        await using var second = await WpfBlueprintFixture.CreateWinFormsCandidateAsync();
        var firstPlan = await first.Workflow.CreatePlanAsync(first.Draft, CancellationToken.None);
        var secondPlan = await second.Workflow.CreatePlanAsync(second.Draft, CancellationToken.None);
        Assert.True(firstPlan.IsValid);
        Assert.True(secondPlan.IsValid);
        Assert.Equal(firstPlan.Value.PlannedProject.Plan.Id, secondPlan.Value.PlannedProject.Plan.Id);
        var firstRun = await first.Workflow.ExecuteAsync(firstPlan.Value, null, CancellationToken.None);
        var secondRun = await second.Workflow.ExecuteAsync(secondPlan.Value, null, CancellationToken.None);
        Assert.True(firstRun.IsValid);
        Assert.True(secondRun.IsValid);
        Assert.Equal(RunStatus.LocalReady, firstRun.Value.Checkpoint.Run.Status);
        Assert.Equal(RunStatus.LocalReady, secondRun.Value.Checkpoint.Run.Status);
        Assert.Equal(Tree(first.TargetPath), Tree(second.TargetPath));
        Assert.True(File.Exists(Path.Combine(first.TargetPath, "devforge.lock.json")));
        Assert.True(File.Exists(Path.Combine(first.TargetPath, "src", "TeamTool.Desktop", "MainForm.cs")));
        Assert.Equal(9, first.Runner.Commands.Length);
        Assert.All(first.Runner.Commands, command => Assert.Equal(ExecutableTool.DotNet, command.Executable.Tool));
        // This proves source-only composition and real Git, not a real toolchain artifact tree.
        var published = await first.PublishLocalAsync(firstPlan.Value.RunId);
        Assert.True(published.IsSuccessful, published.Error?.Code + ": " + published.Error?.Summary);
        var verified = await first.PublishLocalAsync(firstPlan.Value.RunId);
        Assert.True(verified.IsSuccessful, verified.Error?.Code + ": " + verified.Error?.Summary);
        Assert.Equal(RunStatus.Completed, verified.Value.Run.Status);
    }

    [Theory]
    [InlineData(null, RunStatus.Failed)]
    [InlineData("build", RunStatus.ValidationFailed)]
    public async Task FailedCandidateKeepsTargetAbsentAndCleanupIsOwned(string? operation, RunStatus expectedStatus)
    {
        await using var fixture = await WpfBlueprintFixture.CreateWinFormsCandidateAsync();
        var plan = await fixture.Workflow.CreatePlanAsync(fixture.Draft, CancellationToken.None);
        Assert.True(plan.IsValid);
        fixture.Runner.FailNext(operation);
        var failed = await fixture.Workflow.ExecuteAsync(plan.Value, null, CancellationToken.None);
        Assert.True(failed.IsValid);
        Assert.Equal(expectedStatus, failed.Value.Checkpoint.Run.Status);
        Assert.False(Directory.Exists(fixture.TargetPath));
        var cleanup = await fixture.CleanupFailedAsync(failed.Value.Checkpoint);
        Assert.Equal(plan.Value.RunId, cleanup.RunId);
        Assert.False(Directory.Exists(fixture.TargetPath));
    }

    [Fact]
    public async Task CandidateRefusesAnOccupiedUserTarget()
    {
        await using var fixture = await WpfBlueprintFixture.CreateWinFormsCandidateAsync();
        Directory.CreateDirectory(fixture.TargetPath);
        var sentinel = Path.Combine(fixture.TargetPath, "keep.txt");
        await File.WriteAllTextAsync(sentinel, "user-data");
        var plan = await fixture.Workflow.CreatePlanAsync(fixture.Draft, CancellationToken.None);
        Assert.False(plan.IsValid);
        Assert.Equal("user-data", await File.ReadAllTextAsync(sentinel));
        Assert.Empty(fixture.Runner.Commands);
    }

    [Fact]
    public async Task RealDotnetMatrixPublishesResponsiveNativeFormAndCleanLocalGit()
    {
        var runner = new ReleaseDotnetRunner(output);
        await using var fixture = await WpfBlueprintFixture.CreateWinFormsCandidateAsync(runner);
        var plan = await fixture.Workflow.CreatePlanAsync(fixture.Draft, CancellationToken.None);
        Assert.True(plan.IsValid);
        var run = await fixture.Workflow.ExecuteAsync(plan.Value, null, CancellationToken.None);
        Assert.True(run.IsValid);
        Assert.True(run.Value.Checkpoint.Run.Status == RunStatus.LocalReady,
            string.Join("; ", run.Value.Checkpoint.Run.Errors.Select(error => error.Code + ": " + error.Summary)));
        Assert.Equal(9, runner.SuccessfulCommands);
        await AssertNativeFormAsync(Path.Combine(fixture.TargetPath, "artifacts", "publish", "TeamTool.Desktop.exe"));
        // Production publication must preserve outputs while committing only the engine-verified source set.
        var published = await fixture.PublishLocalAsync(plan.Value.RunId);
        Assert.True(published.IsSuccessful, published.Error?.Code + ": " + published.Error?.Summary);
        var verified = await fixture.PublishLocalAsync(plan.Value.RunId);
        Assert.True(verified.IsSuccessful, verified.Error?.Code + ": " + verified.Error?.Summary);
        Assert.Equal(RunStatus.Completed, verified.Value.Run.Status);
        Assert.Equal(0, fixture.RemoteGitHub.Calls);
        output.WriteLine("Host: " + System.Runtime.InteropServices.RuntimeInformation.OSDescription);
    }

    private static async Task AssertNativeFormAsync(string executable)
    {
        Assert.True(File.Exists(executable), "The fixed publish profile must produce the native app.");
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
        };
        // A framework-dependent generated smoke package uses the SDK under test, not an ambient runtime.
        var hostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        Assert.False(string.IsNullOrWhiteSpace(hostPath));
        startInfo.Environment["DOTNET_ROOT"] = Path.GetDirectoryName(hostPath)!;
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("WinForms did not start.");
        try
        {
            Assert.True(process.WaitForInputIdle(20_000));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (process.MainWindowHandle == IntPtr.Zero && !process.HasExited)
            {
                await Task.Delay(100, timeout.Token);
                process.Refresh();
            }

            Assert.False(process.HasExited);
            Assert.True(process.Responding);
            Assert.NotEqual(IntPtr.Zero, process.MainWindowHandle);
            var root = AutomationElement.FromHandle(process.MainWindowHandle);
            var refresh = root.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.NameProperty, "Refresh status"));
            Assert.NotNull(refresh);
            Assert.True(refresh.Current.IsKeyboardFocusable);
            Assert.True(refresh.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern));
            var status = root.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, "StatusLabel"));
            Assert.NotNull(status);
            var initialStatus = status.Current.Name;
            Assert.Contains("TeamTool is ready", initialStatus, StringComparison.Ordinal);
            // The clock is rendered to seconds. Exercise the binding across a clock tick.
            using var refreshTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (StringComparer.Ordinal.Equals(initialStatus, status.Current.Name))
            {
                ((InvokePattern)pattern).Invoke();
                await Task.Delay(100, refreshTimeout.Token);
            }
            Assert.Contains("TeamTool is ready", status.Current.Name, StringComparison.Ordinal);
            Assert.NotEqual(initialStatus, status.Current.Name);
            Assert.True(process.CloseMainWindow());
            await process.WaitForExitAsync(timeout.Token);
            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    private static string[] Tree(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/') + ":" +
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))))
        .Order(StringComparer.Ordinal).ToArray();

    // Observation only: acceptance exercises the production command/environment unchanged.
    private sealed class ReleaseDotnetRunner(ITestOutputHelper output) : IProcessRunner
    {
        private readonly WindowsProcessRunner _inner = new();
        public int SuccessfulCommands { get; private set; }

        public Task CheckPreconditionsAsync(CommandSpec command, CancellationToken cancellationToken) =>
            _inner.CheckPreconditionsAsync(command, cancellationToken);

        public async Task<ProcessResult> RunAsync(CommandSpec command,
            IProgress<ProcessOutputLine>? progress, CancellationToken cancellationToken)
        {
            Assert.Equal(ExecutableTool.DotNet, command.Executable.Tool);
            Assert.True(command.UsesWorkspaceRoot);
            var result = await _inner.RunAsync(command, progress, cancellationToken);
            output.WriteLine(string.Join(' ', command.ArgumentList) + ": " + result.TerminationReason + "/" + result.ExitCode);
            if (result.TerminationReason != ProcessTerminationReason.Exited || result.ExitCode != 0)
            {
                output.WriteLine(string.Join(Environment.NewLine, result.RetainedLines.Select(line => line.Text.Value)));
            }
            Assert.True(result.TerminationReason == ProcessTerminationReason.Exited && result.ExitCode == 0,
                string.Join(Environment.NewLine, result.RetainedLines.Select(line => line.Text.Value)));
            SuccessfulCommands++;
            return result;
        }
    }
}
