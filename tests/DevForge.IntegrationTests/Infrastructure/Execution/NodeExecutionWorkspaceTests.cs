using System.Text;
using DevForge.Application.Contracts;
using DevForge.Infrastructure;
using DevForge.Infrastructure.Execution;
using DevForge.Infrastructure.FileSystem;

namespace DevForge.IntegrationTests.Infrastructure.Execution;

public sealed class NodeExecutionWorkspaceTests
{
    [Fact]
    public async Task ReactStaticDistExportRecoversPartialCopiesWithoutOverwrite()
    {
        await using var fixture = await Fixture.CreateAsync();
        var node = await NodeExecutionWorkspace.OpenAsync(fixture.Staging, default, true);
        await WriteAsync(node.Project, "dist/index.html", "compiled");
        await WriteAsync(node.Project, "dist/assets/app.js", "script");
        await WriteAsync(fixture.Staging.PayloadWorkspace, "dist/index.html", "compiled");
        var resumed = await NodeExecutionWorkspace.OpenAsync(fixture.Staging, default, true);
        await resumed.ExportStaticDistAsync(default);
        Assert.True(await fixture.Staging.PayloadWorkspace.FileExistsAsync(Relative("dist/assets/app.js"), default));
        await WriteAsync(fixture.Staging.PayloadWorkspace, "dist/index.html", "conflict", true);
        await Assert.ThrowsAsync<InfrastructureOperationException>(() => NodeExecutionWorkspace.OpenAsync(fixture.Staging, default, true));
    }

    [Theory]
    [InlineData("node_modules/a.js")]
    [InlineData(".next/server/app.js")]
    [InlineData("dist/app.js")]
    [InlineData(".devforge-node/store/data")]
    [InlineData(".NPMRC")]
    [InlineData(".env.local")]
    public async Task ReservedSourceNamespacesCannotBecomeToolingExceptions(string path)
    {
        await using var fixture = await Fixture.CreateAsync();
        await WriteAsync(fixture.Staging.PayloadWorkspace, path, "source");
        await Assert.ThrowsAsync<InfrastructureOperationException>(() => NodeExecutionWorkspace.OpenAsync(fixture.Staging, default));
    }

    [Theory]
    [InlineData("unexpected.js")]
    [InlineData(".env")]
    [InlineData(".npmrc")]
    public async Task UnexpectedToolOutputFailsClosed(string path)
    {
        await using var fixture = await Fixture.CreateAsync();
        var node = await NodeExecutionWorkspace.OpenAsync(fixture.Staging, default);
        await WriteAsync(node.Project, path, "unexpected");
        await Assert.ThrowsAsync<InfrastructureOperationException>(() => node.VerifyAsync(default));
    }

    [Fact]
    public async Task ArtifactsChangeEvidenceButNeverSourceAndReplayKeepsIdentity()
    {
        await using var fixture = await Fixture.CreateAsync();
        var node = await NodeExecutionWorkspace.OpenAsync(fixture.Staging, default);
        var original = await node.VerifyAsync(default);
        await WriteAsync(node.Project, ".next/server/app.js", "compiled");
        var changed = await node.VerifyAsync(default);
        Assert.NotEqual(original, changed);
        var reopened = await NodeExecutionWorkspace.OpenAsync(fixture.Staging, default);
        Assert.Equal(changed, await reopened.VerifyAsync(default));
        Assert.Single(await fixture.Staging.PayloadWorkspace.EnumerateAllFilesAsync(default));
    }

    [Fact]
    public async Task MatchingTamperOfPayloadAndCopyStillViolatesPersistedSourceIdentity()
    {
        await using var fixture = await Fixture.CreateAsync();
        var node = await NodeExecutionWorkspace.OpenAsync(fixture.Staging, default);
        await WriteAsync(node.Project, "package.json", "changed", true);
        await WriteAsync(fixture.Staging.PayloadWorkspace, "package.json", "changed", true);
        await Assert.ThrowsAsync<InfrastructureOperationException>(() => NodeExecutionWorkspace.OpenAsync(fixture.Staging, default));
    }

    [Fact]
    public async Task IncompleteCopyIsResumedOnlyWhenBytesMatch()
    {
        await using var fixture = await Fixture.CreateAsync();
        var node = await NodeExecutionWorkspace.OpenAsync(fixture.Staging, default);
        await fixture.Staging.ContainerWorkspace!.DeleteFileAsync(Relative("tooling/node/source.sha256"), default);
        await WriteAsync(node.Project, "package.json", "changed", true);
        await Assert.ThrowsAsync<InfrastructureOperationException>(() => NodeExecutionWorkspace.OpenAsync(fixture.Staging, default));
    }

    private static async Task WriteAsync(IWorkspaceFileSystem workspace, string path, string text, bool overwrite = false)
    {
        var relative = Relative(path);
        var parent = Path.GetDirectoryName(relative.Value);
        if (!string.IsNullOrEmpty(parent)) { await workspace.CreateDirectoryAsync(Relative(parent), default); }
        await ((IAtomicFileWorkspaceFileSystem)workspace).WriteFileAtomicallyAsync(relative, Encoding.UTF8.GetBytes(text), overwrite, default);
    }

    private static WorkspaceRelativePath Relative(string path) => WorkspaceRelativePath.Create(path.Replace('/', '\\')).Value;

    private sealed class Fixture(string root, StagingWorkspace staging) : IAsyncDisposable
    {
        public StagingWorkspace Staging { get; } = staging;
        public static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "DevForge-NodeBoundary-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "payload"));
            var fs = new WindowsFileSystem();
            var container = await fs.OpenWorkspaceAsync(WorkspaceRoot.Create(root).Value, default);
            var payload = await fs.OpenWorkspaceAsync(WorkspaceRoot.Create(Path.Combine(root, "payload")).Value, default);
            await WriteAsync(payload, "package.json", "{\"private\":true}");
            var descriptor = StagingDescriptor.Create(Relative(".devforge-staging/run-1"),
                Relative(".devforge-staging/run-1/payload"), Relative(".devforge-staging/run-1/ownership.json"), "marker-1").Value;
            return new Fixture(root, StagingWorkspace.Create(descriptor, payload, container).Value);
        }
        public ValueTask DisposeAsync()
        {
            Assert.StartsWith(Path.GetTempPath() + "DevForge-NodeBoundary-", root, StringComparison.OrdinalIgnoreCase);
            Directory.Delete(root, true);
            return ValueTask.CompletedTask;
        }
    }
}
