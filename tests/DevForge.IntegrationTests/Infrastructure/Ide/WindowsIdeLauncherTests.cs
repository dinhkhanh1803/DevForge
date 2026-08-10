using System.Diagnostics;
using DevForge.Application.Contracts;
using DevForge.Infrastructure;
using DevForge.Infrastructure.FileSystem;
using DevForge.Infrastructure.Ide;
using DevForge.Infrastructure.Processes;

namespace DevForge.IntegrationTests.Infrastructure.Ide;

public sealed class WindowsIdeLauncherTests
{
    [Theory]
    [InlineData("vscode", ExecutableTool.VisualStudioCode)]
    [InlineData("visual-studio", ExecutableTool.VisualStudio)]
    public async Task SupportedIdeUsesClosedTrustedIdentity(string ideId, ExecutableTool expectedTool)
    {
        await using var fixture = await IdeFixture.CreateAsync();
        var recordingLauncher = new RecordingInteractiveLauncher();
        var launcher = new WindowsIdeLauncher(recordingLauncher);
        var request = IdeLaunchRequest.Create(fixture.Workspace, ideId).Value;

        await launcher.LaunchAsync(request, CancellationToken.None);

        var launch = Assert.Single(recordingLauncher.Launches);
        Assert.Equal(expectedTool, launch.Executable.Tool);
        Assert.Same(fixture.Workspace, launch.Workspace);
    }

    [Fact]
    public async Task UnsupportedIdeFailsClosedWithoutEchoingIdentifier()
    {
        await using var fixture = await IdeFixture.CreateAsync();
        var launcher = new WindowsIdeLauncher(new RecordingInteractiveLauncher());
        var request = IdeLaunchRequest.Create(fixture.Workspace, "untrusted-ide-path").Value;

        var exception = await Assert.ThrowsAsync<InfrastructureOperationException>(() =>
            launcher.LaunchAsync(request, CancellationToken.None));

        Assert.Equal("DF-IDE-001", exception.Code);
        Assert.DoesNotContain("untrusted-ide-path", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PreCancelledLaunchDoesNotCallInteractiveBoundary()
    {
        await using var fixture = await IdeFixture.CreateAsync();
        var recordingLauncher = new RecordingInteractiveLauncher();
        var launcher = new WindowsIdeLauncher(recordingLauncher);
        var request = IdeLaunchRequest.Create(fixture.Workspace, "vscode").Value;
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            launcher.LaunchAsync(request, source.Token));

        Assert.Empty(recordingLauncher.Launches);
    }

    [Fact]
    public async Task WindowsHandoffPassesWorkspaceAsOneArgumentWithoutShellOrElevation()
    {
        await using var fixture = await IdeFixture.CreateAsync();
        var starter = new RecordingProcessStarter();
        var launcher = new WindowsInteractiveProcessLauncher(
            new FixedExecutableResolver("C:\\Tools\\code.exe"),
            starter);

        await launcher.LaunchAsync(
            ExecutableIdentity.Create("code").Value,
            fixture.Workspace,
            CancellationToken.None);

        var startInfo = Assert.Single(starter.StartInfos);
        Assert.Equal("C:\\Tools\\code.exe", startInfo.FileName);
        Assert.False(startInfo.UseShellExecute);
        Assert.False(startInfo.CreateNoWindow);
        Assert.True(string.IsNullOrEmpty(startInfo.Verb));
        Assert.Equal(fixture.RootPath, Assert.Single(startInfo.ArgumentList));
        Assert.Equal(fixture.RootPath, startInfo.WorkingDirectory);
    }

    private sealed class FixedExecutableResolver(string executablePath) : ITrustedExecutableResolver
    {
        public string Resolve(ExecutableIdentity executable)
        {
            return executablePath;
        }
    }

    private sealed class RecordingInteractiveLauncher : IInteractiveProcessLauncher
    {
        public List<(ExecutableIdentity Executable, IWorkspaceFileSystem Workspace)> Launches { get; } = [];

        public Task LaunchAsync(
            ExecutableIdentity executable,
            IWorkspaceFileSystem workspace,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Launches.Add((executable, workspace));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingProcessStarter : IInteractiveProcessStarter
    {
        public List<ProcessStartInfo> StartInfos { get; } = [];

        public Process Start(ProcessStartInfo startInfo)
        {
            StartInfos.Add(startInfo);
            return new Process();
        }
    }

    private sealed class IdeFixture : IAsyncDisposable
    {
        private IdeFixture(string rootPath, IWorkspaceFileSystem workspace)
        {
            RootPath = rootPath;
            Workspace = workspace;
        }

        public string RootPath { get; }

        public IWorkspaceFileSystem Workspace { get; }

        public static async Task<IdeFixture> CreateAsync()
        {
            var rootPath = Path.Combine(Path.GetTempPath(), "DevForge-M3-Ide-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootPath);
            var root = WorkspaceRoot.Create(rootPath).Value;
            var workspace = await new WindowsFileSystem().OpenWorkspaceAsync(root, CancellationToken.None);
            return new IdeFixture(rootPath, workspace);
        }

        public ValueTask DisposeAsync()
        {
            var fullPath = Path.GetFullPath(RootPath);
            if (!fullPath.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(fullPath).StartsWith("DevForge-M3-Ide-", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Refusing to clean an unexpected IDE test directory.");
            }

            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
