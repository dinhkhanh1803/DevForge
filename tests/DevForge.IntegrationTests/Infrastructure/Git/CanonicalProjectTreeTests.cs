using System.Text;
using DevForge.Application.Contracts;
using DevForge.Infrastructure;
using DevForge.Infrastructure.Execution;
using DevForge.Infrastructure.FileSystem;
using DevForge.Infrastructure.Git;

namespace DevForge.IntegrationTests.Infrastructure.Git;

public sealed class CanonicalProjectTreeTests
{
    [Fact]
    public async Task OwnedRootGitIsExcludedWithoutChangingTheFinalTreeDigest()
    {
        await using var fixture = await TreeFixture.CreateAsync();
        await fixture.WriteAsync("README.md", "hello\n");
        await fixture.WriteAsync("src\\App.cs", "class App {}\n");
        var before = await CanonicalProjectTree.CaptureAsync(
            fixture.Workspace,
            allowOwnedRootGit: false,
            CancellationToken.None);
        await fixture.WriteAsync(".git\\config", "[core]\n\trepositoryformatversion = 0\n");

        var after = await CanonicalProjectTree.CaptureAsync(
            fixture.Workspace,
            allowOwnedRootGit: true,
            CancellationToken.None);

        Assert.Equal(before.Digest, after.Digest);
        Assert.Equal(before.SourceFiles.ToArray(), after.SourceFiles.ToArray());
        Assert.True(after.HasRootGit);
    }

    [Theory]
    [InlineData(".git\\config", false)]
    [InlineData("src\\.git\\config", true)]
    public async Task UnexpectedRepositoryMetadataFailsClosed(string path, bool allowOwnedRootGit)
    {
        await using var fixture = await TreeFixture.CreateAsync();
        await fixture.WriteAsync("README.md", "hello\n");
        await fixture.WriteAsync(path, "unexpected\n");

        var exception = await Assert.ThrowsAsync<InfrastructureOperationException>(() =>
            CanonicalProjectTree.CaptureAsync(
                fixture.Workspace,
                allowOwnedRootGit,
                CancellationToken.None));

        Assert.Equal("DF-GIT-004", exception.Code);
        Assert.DoesNotContain(fixture.RootPath, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GuardedEnumerationStopsAtTheConfiguredBound()
    {
        await using var fixture = await TreeFixture.CreateAsync();
        await fixture.WriteAsync("one.txt", "one\n");
        await fixture.WriteAsync("two.txt", "two\n");
        var bounded = Assert.IsAssignableFrom<IBoundedWorkspaceEnumerator>(fixture.Workspace);

        var result = await bounded.EnumerateTreeBoundedAsync(
            excludedRootDirectory: null,
            maximumFiles: 1,
            maximumDirectories: 1,
            maximumDepth: 2,
            CancellationToken.None);

        Assert.True(result.LimitExceeded);
        Assert.Empty(result.Files);
        Assert.Empty(result.Directories);
    }

    [Fact]
    public void GitObjectBoundsCoverExactFinalizerLimits()
    {
        Assert.True(GitTreeVerifier.IsDirectoryCountWithinBounds(
            AtomicProjectFinalizer.MaximumDirectoryCount + 1));
        Assert.False(GitTreeVerifier.IsDirectoryCountWithinBounds(
            AtomicProjectFinalizer.MaximumDirectoryCount + 2));
        Assert.True(
            GitTreeVerifier.MaximumCompressedBlobBytes()
            > AtomicProjectFinalizer.MaximumFileBytes + 128);
    }

    private sealed class TreeFixture : IAsyncDisposable
    {
        private TreeFixture(string rootPath, IWorkspaceFileSystem workspace)
        {
            RootPath = rootPath;
            Workspace = workspace;
        }

        public string RootPath { get; }
        public IWorkspaceFileSystem Workspace { get; }

        public static async Task<TreeFixture> CreateAsync()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "DevForge-M8-GitTree-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            var workspace = await new WindowsFileSystem().OpenWorkspaceAsync(
                WorkspaceRoot.Create(path).Value,
                CancellationToken.None);
            return new TreeFixture(path, workspace);
        }

        public async Task WriteAsync(string path, string content)
        {
            var relative = WorkspaceRelativePath.Create(path).Value;
            var parent = Path.GetDirectoryName(path)?.Replace('/', '\\');
            if (!string.IsNullOrEmpty(parent))
            {
                await Workspace.CreateDirectoryAsync(
                    WorkspaceRelativePath.Create(parent).Value,
                    CancellationToken.None);
            }

            await using var output = await Workspace.OpenWriteAsync(
                relative,
                overwrite: false,
                CancellationToken.None);
            await output.WriteAsync(Encoding.UTF8.GetBytes(content));
        }

        public ValueTask DisposeAsync()
        {
            var fullPath = Path.GetFullPath(RootPath);
            if (!fullPath.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(fullPath).StartsWith(
                    "DevForge-M8-GitTree-",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException();
            }

            Directory.Delete(fullPath, recursive: true);
            return ValueTask.CompletedTask;
        }
    }
}
