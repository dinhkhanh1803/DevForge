using System.Reflection;
using System.Text;
using System.Text.Json;
using DevForge.Application.Contracts;
using DevForge.Infrastructure;
using DevForge.Infrastructure.Execution;
using DevForge.Infrastructure.FileSystem;
using DevForge.Infrastructure.Git;

namespace DevForge.IntegrationTests.Infrastructure.Git;

public sealed class CanonicalProjectTreeTests
{
    private static readonly JsonSerializerOptions _markerOptions = new() { WriteIndented = true };
    private static readonly string _pythonOutputMarker = CanonicalMarker("{\"schema\":\"devforge-python-build-outputs-v1\",\"projects\":[\"pyproject.toml\",\"uv.lock\"],\"publish\":false,\"outputs\":[\"dist/app.whl\"]}");

    [Theory]
    [InlineData(".venv\\unsafe.py")]
    [InlineData(".ruff_cache\\cache.bin")]
    [InlineData(".mypy_cache\\cache.json")]
    [InlineData(".pytest_cache\\cache.txt")]
    [InlineData("dist\\extra.whl")]
    public async Task PythonMembershipNeverHidesUnrecordedOutputsOrCaches(string extra)
    {
        await using var fixture = await TreeFixture.CreateAsync();
        await fixture.WriteAsync("pyproject.toml", "sample\n");
        await fixture.WriteAsync("uv.lock", "sample\n");
        await fixture.WriteAsync("dist\\app.whl", "sample\n");
        await fixture.WriteAsync(".devforge\\build-outputs.json", _pythonOutputMarker);
        var before = await CanonicalProjectTree.CaptureAsync(fixture.Workspace, false, default);
        Assert.DoesNotContain(before.SourceFiles, path => path.Value == "dist\\app.whl");
        await fixture.WriteAsync(extra, "unrecorded\n");
        var after = await CanonicalProjectTree.CaptureAsync(fixture.Workspace, false, default);
        Assert.NotEqual(before.Digest, after.Digest);
        Assert.Contains(after.SourceFiles, path => path.Value == extra);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("schema")]
    [InlineData("publish")]
    public async Task PythonMembershipCorruptionFailsClosed(string mutation)
    {
        await using var fixture = await TreeFixture.CreateAsync();
        await fixture.WriteAsync("pyproject.toml", "sample\n");
        await fixture.WriteAsync("uv.lock", "sample\n");
        await fixture.WriteAsync("dist\\app.whl", "sample\n");
        var marker = mutation switch
        {
            "source" => _pythonOutputMarker.Replace("dist/app.whl", "pyproject.toml", StringComparison.Ordinal),
            "missing" => _pythonOutputMarker.Replace("app.whl", "missing.whl", StringComparison.Ordinal),
            "duplicate" => _pythonOutputMarker.Replace("\"dist/app.whl\"", "\"dist/app.whl\",\"dist/app.whl\"", StringComparison.Ordinal),
            "schema" => _pythonOutputMarker.Replace("devforge-python-build-outputs-v1", "devforge-build-outputs-v1", StringComparison.Ordinal),
            _ => _pythonOutputMarker.Replace("false", "true", StringComparison.Ordinal),
        };
        await fixture.WriteAsync(".devforge\\build-outputs.json", marker);
        var error = await Assert.ThrowsAsync<InfrastructureOperationException>(() => CanonicalProjectTree.CaptureAsync(fixture.Workspace, false, default));
        Assert.Equal("DF-GIT-004", error.Code);
    }

    private static readonly string _outputMarker = CanonicalMarker("{\"schema\":\"devforge-build-outputs-v1\",\"projects\":[\"src/App/App.csproj\"],\"publish\":false,\"outputs\":[\"src/App/obj/cache.bin\"]}");

    private static string CanonicalMarker(string text)
    {
        using var json = JsonDocument.Parse(text);
        return JsonSerializer.Serialize(json.RootElement, _markerOptions) + "\n";
    }

    [Fact]
    public async Task SourceProjectionUsesTheSameMarkerBytesAsTheDigest()
    {
        await using var fixture = await TreeFixture.CreateAsync();
        await fixture.WriteAsync("src\\App\\App.csproj", "<Project />\n");
        await fixture.WriteAsync("src\\App\\obj\\cache.bin", "output\n");
        await fixture.WriteAsync("src\\App\\obj\\Reviewed.cs", "reviewed source\n");
        await fixture.WriteAsync(".devforge\\build-outputs.json", _outputMarker);
        var expected = await CanonicalProjectTree.CaptureAsync(fixture.Workspace, false, default);
        var proxy = DispatchProxy.Create<IWorkspaceFileSystem, SwappingMarkerReads>();
        var interceptor = (SwappingMarkerReads)proxy;
        interceptor.Inner = fixture.Workspace;
        interceptor.Replacement = Encoding.UTF8.GetBytes(CanonicalMarker(_outputMarker.Replace("\"src/App/obj/cache.bin\"",
            "\"src/App/obj/Reviewed.cs\",\"src/App/obj/cache.bin\"", StringComparison.Ordinal)));
        var actual = await CanonicalProjectTree.CaptureAsync(proxy, false, default);
        Assert.Equal(expected.Digest, actual.Digest);
        Assert.Equal(expected.SourceFiles.ToArray(), actual.SourceFiles.ToArray());
    }

    public class SwappingMarkerReads : DispatchProxy
    {
        public IWorkspaceFileSystem Inner { get; set; } = null!;
        public byte[] Replacement { get; set; } = [];
        private int _markerReads;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name == nameof(IWorkspaceFileSystem.OpenReadAsync)
                && args![0] is WorkspaceRelativePath path && path.Value == ".devforge\\build-outputs.json"
                && ++_markerReads > 1)
            {
                return Task.FromResult<Stream>(new MemoryStream(Replacement, writable: false));
            }
            return targetMethod.Invoke(Inner, args);
        }
    }

    [Fact]
    public async Task RecordedBuildOutputsRemainInDigestButNotGitSourceSet()
    {
        await using var fixture = await TreeFixture.CreateAsync();
        await fixture.WriteAsync("src\\App\\App.csproj", "<Project />\n");
        await fixture.WriteAsync("src\\App\\obj\\cache.bin", "first\n");
        await fixture.WriteAsync(".devforge\\build-outputs.json", _outputMarker);
        var before = await CanonicalProjectTree.CaptureAsync(fixture.Workspace, false, default);
        Assert.DoesNotContain(before.SourceFiles, path => path.Value.EndsWith("cache.bin", StringComparison.Ordinal));
        Assert.Contains(before.SourceFiles, path => path.Value == ".devforge\\build-outputs.json");
        await using (var stream = await fixture.Workspace.OpenWriteAsync(
            WorkspaceRelativePath.Create("src\\App\\obj\\cache.bin").Value, true, default))
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes("changed\n"));
        }
        var changed = await CanonicalProjectTree.CaptureAsync(fixture.Workspace, false, default);
        Assert.NotEqual(before.Digest, changed.Digest);
    }

    [Theory]
    [InlineData("traversal")]
    [InlineData("source")]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("missing")]
    public async Task InvalidOutputMembershipFailsClosed(string mutation)
    {
        await using var fixture = await TreeFixture.CreateAsync();
        await fixture.WriteAsync("src\\App\\App.csproj", "<Project />\n");
        await fixture.WriteAsync("src\\App\\obj\\cache.bin", "output\n");
        var marker = mutation switch
        {
            "traversal" => _outputMarker.Replace("src/App/obj/cache.bin", "../outside", StringComparison.Ordinal),
            "source" => _outputMarker.Replace("src/App/obj/cache.bin", "src/App/App.csproj", StringComparison.Ordinal),
            "duplicate" => _outputMarker.Replace("\"src/App/obj/cache.bin\"", "\"src/App/obj/cache.bin\",\"src/App/obj/cache.bin\"", StringComparison.Ordinal),
            "unknown" => _outputMarker.Replace("\"publish\": false", "\"publish\": false,\"unknown\":true", StringComparison.Ordinal),
            _ => _outputMarker.Replace("cache.bin", "missing.bin", StringComparison.Ordinal),
        };
        await fixture.WriteAsync(".devforge\\build-outputs.json", marker);
        var exception = await Assert.ThrowsAsync<InfrastructureOperationException>(() =>
            CanonicalProjectTree.CaptureAsync(fixture.Workspace, false, default));
        Assert.Equal("DF-GIT-004", exception.Code);
    }

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
