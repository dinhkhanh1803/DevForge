using System.Collections.Immutable;
using System.Runtime.InteropServices;
using DevForge.Application.Contracts;
using DevForge.Domain.Privacy;
using DevForge.Infrastructure;
using DevForge.Infrastructure.Environment;
using DevForge.Infrastructure.FileSystem;
using DevForge.Infrastructure.Processes;

namespace DevForge.IntegrationTests.Infrastructure.Environment;

public sealed class WindowsEnvironmentDoctorTests
{
    [Fact]
    public async Task DoctorUsesFixedTypedProbesAndNormalizesVersions()
    {
        await using var fixture = await EnvironmentFixture.CreateAsync();
        var runner = new RecordingProcessRunner();
        var doctor = new WindowsEnvironmentDoctor(
            runner,
            fixture.Workspace,
            TimeProvider.System);

        var snapshot = await doctor.InspectAsync(CancellationToken.None);

        Assert.Equal(
            ["dotnet", "git", "gh", "node"],
            snapshot.Tools.Select(tool => tool.Name));
        Assert.All(snapshot.Tools, tool => Assert.True(tool.IsAvailable));
        Assert.All(snapshot.Tools, tool => Assert.Equal("1.2.3", tool.Version));
        Assert.Equal(
            [ExecutableTool.DotNet, ExecutableTool.Git, ExecutableTool.GitHubCli, ExecutableTool.Node],
            runner.Commands.Select(command => command.Executable.Tool));
        Assert.All(runner.Commands, command =>
        {
            Assert.Same(fixture.Workspace, command.Workspace);
            Assert.Empty(command.EnvironmentVariables);
            Assert.Equal([0], command.AllowedExitCodes);
            Assert.InRange(command.Timeout, TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(5));
        });
    }

    [Fact]
    public async Task MissingOptionalToolBecomesUnavailableWithoutRawFailure()
    {
        await using var fixture = await EnvironmentFixture.CreateAsync();
        var runner = new RecordingProcessRunner(ExecutableTool.Git);
        var doctor = new WindowsEnvironmentDoctor(runner, fixture.Workspace, TimeProvider.System);

        var snapshot = await doctor.InspectAsync(CancellationToken.None);

        var git = Assert.Single(snapshot.Tools.Where(tool => tool.Name == "git"));
        Assert.False(git.IsAvailable);
        Assert.Null(git.Version);
    }

    [Fact]
    public async Task PreCancelledInspectionDoesNotRunProbe()
    {
        await using var fixture = await EnvironmentFixture.CreateAsync();
        var runner = new RecordingProcessRunner();
        var doctor = new WindowsEnvironmentDoctor(runner, fixture.Workspace, TimeProvider.System);
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => doctor.InspectAsync(source.Token));

        Assert.Empty(runner.Commands);
    }

    [Fact]
    public async Task RealDoctorDetectsWorkspaceLocalDotNetHost()
    {
        await using var fixture = await EnvironmentFixture.CreateAsync();
        var dotnetHost = FindDotNetHost();
        var doctor = new WindowsEnvironmentDoctor(
            new WindowsProcessRunner(new FixedDotNetResolver(dotnetHost)),
            fixture.Workspace,
            TimeProvider.System,
            probes: EnvironmentProbeCatalog.DotNetOnly);

        var snapshot = await doctor.InspectAsync(CancellationToken.None);

        var dotnet = Assert.Single(snapshot.Tools);
        Assert.Equal("dotnet", dotnet.Name);
        Assert.True(dotnet.IsAvailable);
        Assert.False(string.IsNullOrWhiteSpace(dotnet.Version));
    }

    private static string FindDotNetHost()
    {
        var configured = System.Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return configured;
        }

        var runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
        return Path.GetFullPath(Path.Combine(runtimeDirectory, "..", "..", "..", "dotnet.exe"));
    }

    private sealed class FixedDotNetResolver(string executablePath) : ITrustedExecutableResolver
    {
        public string Resolve(ExecutableIdentity executable)
        {
            return executable.Tool == ExecutableTool.DotNet
                ? executablePath
                : throw new InfrastructureOperationException(
                    "DF-PROC-001",
                    "The trusted executable could not be resolved.");
        }
    }

    private sealed class RecordingProcessRunner(ExecutableTool? missingTool = null) : IProcessRunner
    {
        public List<CommandSpec> Commands { get; } = [];

        public Task<ProcessResult> RunAsync(
            CommandSpec command,
            IProgress<ProcessOutputLine>? progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add(command);
            if (command.Executable.Tool == missingTool)
            {
                throw new InfrastructureOperationException(
                    "DF-PROC-001",
                    "The trusted executable could not be resolved.");
            }

            var text = RedactedText.FromTrustedRedaction("tool version 1.2.3").Value;
            var line = ProcessOutputLine.Create(ProcessOutputChannel.StandardOutput, text).Value;
            return Task.FromResult(
                ProcessResult.Create(ProcessTerminationReason.Exited, 0, [line]).Value);
        }
    }

    private sealed class EnvironmentFixture : IAsyncDisposable
    {
        private EnvironmentFixture(string rootPath, IWorkspaceFileSystem workspace)
        {
            RootPath = rootPath;
            Workspace = workspace;
        }

        public string RootPath { get; }

        public IWorkspaceFileSystem Workspace { get; }

        public static async Task<EnvironmentFixture> CreateAsync()
        {
            var rootPath = Path.Combine(Path.GetTempPath(), "DevForge-M3-Doctor-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootPath);
            var root = WorkspaceRoot.Create(rootPath).Value;
            var workspace = await new WindowsFileSystem().OpenWorkspaceAsync(root, CancellationToken.None);
            return new EnvironmentFixture(rootPath, workspace);
        }

        public ValueTask DisposeAsync()
        {
            var fullPath = Path.GetFullPath(RootPath);
            if (!fullPath.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(fullPath).StartsWith("DevForge-M3-Doctor-", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Refusing to clean an unexpected doctor test directory.");
            }

            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
