using System.Collections.Immutable;
using System.Reflection;
using DevForge.Application.Contracts;

namespace DevForge.UnitTests.Application;

public sealed class WorkspaceSecurityReviewTests
{
    [Theory]
    [InlineData("\\\\server\\share")]
    [InlineData("\\\\?\\C:\\work")]
    [InlineData("\\\\.\\C:\\work")]
    [InlineData("\\??\\C:\\work")]
    [InlineData("C:\\work\\CON")]
    [InlineData("C:\\work\\aux.txt")]
    [InlineData("C:\\work. ")]
    public void WorkspaceRootRejectsWindowsNamespaceReservedAndAmbiguousPaths(string path)
    {
        Assert.False(WorkspaceRoot.Create(path).IsValid);
    }

    [Fact]
    public void WorkspaceRootCatchesPathExceptionsAndDoesNotExposeRawRootString()
    {
        var result = WorkspaceRoot.Create("C:\\work\0invalid");

        Assert.False(result.IsValid);
        Assert.DoesNotContain(
            typeof(WorkspaceRoot).GetProperties(BindingFlags.Public | BindingFlags.Instance),
            property => property.PropertyType == typeof(string));
        Assert.DoesNotContain("C:\\work", WorkspaceRoot.Create("C:\\work").Value.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WorkspaceRootUsesWindowsCaseInsensitiveNormalizedEquality()
    {
        var first = WorkspaceRoot.Create("C:\\Work\\Project\\").Value;
        var same = WorkspaceRoot.Create("c:\\work\\project").Value;

        Assert.Equal(first, same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("con.txt")]
    [InlineData("folder\\AUX.json")]
    [InlineData("folder\\COM1.log")]
    [InlineData("folder\\LPT9")]
    [InlineData("folder\\trailing.")]
    [InlineData("folder\\trailing ")]
    [InlineData("folder\\file.txt:stream")]
    [InlineData("folder/file.txt")]
    [InlineData("\\\\?\\C:\\work")]
    public void RelativePathRejectsReservedAmbiguousAndAlternateForms(string path)
    {
        Assert.False(WorkspaceRelativePath.Create(path).IsValid);
    }

    [Fact]
    public void RelativePathRejectsControlCharactersAndUsesCaseInsensitiveEquality()
    {
        Assert.False(WorkspaceRelativePath.Create("folder\\bad\u0001name.txt").IsValid);

        var first = WorkspaceRelativePath.Create("Src\\Program.cs").Value;
        var same = WorkspaceRelativePath.Create("src\\program.cs").Value;
        Assert.Equal(first, same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
    }

    [Fact]
    public void ScopedFileSystemExposesOpaqueRootAndExplicitGuardedLifecycleOperations()
    {
        var rootProperty = typeof(IWorkspaceFileSystem).GetProperty(nameof(IWorkspaceFileSystem.Root));
        Assert.NotNull(rootProperty);
        Assert.Equal(typeof(WorkspaceRoot), rootProperty.PropertyType);

        AssertMethod(
            nameof(IWorkspaceFileSystem.EnumerateFilesAsync),
            typeof(WorkspaceRelativePath),
            typeof(bool),
            typeof(CancellationToken));
        AssertMethod(
            nameof(IWorkspaceFileSystem.DeleteDirectoryAsync),
            typeof(WorkspaceRelativePath),
            typeof(DirectoryCleanupIntent),
            typeof(CancellationToken));
        AssertMethod(
            nameof(IWorkspaceFileSystem.MoveDirectoryAsync),
            typeof(WorkspaceRelativePath),
            typeof(WorkspaceRelativePath),
            typeof(WorkspaceMoveIntent),
            typeof(CancellationToken));

        Assert.False(Enum.IsDefined((DirectoryCleanupIntent)0));
        Assert.False(Enum.IsDefined((WorkspaceMoveIntent)0));
    }

    [Fact]
    public void WholeWorkspaceSecretScanHasNoSyntheticPath()
    {
        var result = SecretScanRequest.WholeWorkspace(new StubWorkspaceFileSystem());

        Assert.True(result.IsValid);
        Assert.Equal(SecretScanScope.WholeWorkspace, result.Value.Scope);
        Assert.Empty(result.Value.Paths);
    }

    [Fact]
    public void ExplicitSecretScanRejectsEmptyAndDeduplicatesPathsCaseInsensitively()
    {
        var workspace = new StubWorkspaceFileSystem();
        Assert.False(SecretScanRequest.ExplicitPaths(workspace, []).IsValid);

        var first = WorkspaceRelativePath.Create("src\\Program.cs").Value;
        var duplicate = WorkspaceRelativePath.Create("SRC\\PROGRAM.CS").Value;
        var paths = new SingleUseEnumerable<WorkspaceRelativePath?>([first, duplicate]);
        var result = SecretScanRequest.ExplicitPaths(workspace, paths);

        Assert.True(result.IsValid);
        Assert.Equal(1, paths.EnumerationCount);
        Assert.Equal(SecretScanScope.ExplicitPaths, result.Value.Scope);
        Assert.Equal(first, Assert.Single(result.Value.Paths));
    }

    private static void AssertMethod(string name, params Type[] parameters)
    {
        Assert.NotNull(typeof(IWorkspaceFileSystem).GetMethod(name, parameters));
    }

    private sealed class SingleUseEnumerable<T>(IEnumerable<T> values) : IEnumerable<T>
    {
        private readonly IEnumerable<T> _values = values;

        public int EnumerationCount { get; private set; }

        public IEnumerator<T> GetEnumerator()
        {
            EnumerationCount++;
            if (EnumerationCount > 1)
            {
                throw new InvalidOperationException("The sequence was enumerated more than once.");
            }

            return _values.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class StubWorkspaceFileSystem : IWorkspaceFileSystem
    {
        public WorkspaceRoot Root { get; } = WorkspaceRoot.Create("C:\\work").Value;

        public Task<bool> FileExistsAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> DirectoryExistsAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task CreateDirectoryAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Stream> OpenReadAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Stream> OpenWriteAsync(
            WorkspaceRelativePath path,
            bool overwrite,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DeleteFileAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateFilesAsync(
            WorkspaceRelativePath directory,
            bool recursive,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DeleteDirectoryAsync(
            WorkspaceRelativePath path,
            DirectoryCleanupIntent intent,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task MoveDirectoryAsync(
            WorkspaceRelativePath source,
            WorkspaceRelativePath destination,
            WorkspaceMoveIntent intent,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task MoveAsync(
            WorkspaceRelativePath source,
            WorkspaceRelativePath destination,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
