using System.Collections.Immutable;
using DevForge.Application.Contracts;
using DevForge.Domain.Privacy;
using DevForge.Domain.Projects;

namespace DevForge.UnitTests.Application;

public sealed class RequestContractTests
{
    [Fact]
    public void TemplateRenderRequestSnapshotsContextExactlyOnce()
    {
        var context = new SingleUseEnumerable<KeyValuePair<string, string?>>(
            [KeyValuePair.Create<string, string?>("projectName", "Example")]);

        var result = TemplateRenderRequest.Create("Hello {{ projectName }}", context);

        Assert.True(result.IsValid);
        Assert.Equal(1, context.EnumerationCount);
        Assert.Equal("Example", result.Value.Context["projectName"]);
    }

    [Fact]
    public void SecretScanRequestSnapshotsScopedPathsExactlyOnce()
    {
        var workspace = new StubWorkspaceFileSystem();
        var path = WorkspaceRelativePath.Create("src\\Program.cs").Value;
        var paths = new SingleUseEnumerable<WorkspaceRelativePath?>([path]);

        var result = SecretScanRequest.ExplicitPaths(workspace, paths);

        Assert.True(result.IsValid);
        Assert.Equal(1, paths.EnumerationCount);
        Assert.Equal(path, Assert.Single(result.Value.Paths));
    }

    [Fact]
    public void SecretFindingsContainOnlyScopedPathsAndRedactedDescriptions()
    {
        var path = WorkspaceRelativePath.Create("src\\Program.cs").Value;
        var finding = SecretFinding.Create(path, 12, Redacted("Potential credential was redacted.")).Value;

        Assert.Equal(path, finding.Path);
        Assert.Equal(12, finding.LineNumber);
        Assert.Equal(typeof(RedactedText), typeof(SecretFinding).GetProperty(nameof(SecretFinding.Description))?.PropertyType);
    }

    [Fact]
    public void InvalidRequestsReturnValidationIssuesWithoutThrowing()
    {
        Assert.False(TemplateRenderRequest.Create(null, null).IsValid);
        Assert.False(SecretScanRequest.ExplicitPaths(null, null).IsValid);
        Assert.False(GitBootstrapRequest.Create(null, (GitBranchPolicy)999, null).IsValid);
        Assert.False(GitVerificationRequest.Create(null, (GitBranchPolicy)999, null, null).IsValid);
        Assert.False(
            GitHubPublishRequest.Create(
                null,
                null,
                (GitBranchPolicy)999,
                null,
                null,
                isPrivate: true,
                null).IsValid);
        Assert.False(IdeLaunchRequest.Create(null, null).IsValid);
    }

    private static RedactedText Redacted(string value)
    {
        return RedactedText.FromTrustedRedaction(value).Value;
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

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    private sealed class StubWorkspaceFileSystem : IWorkspaceFileSystem
    {
        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateAllFilesAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateRootDirectoriesAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

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

        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateDirectoriesAsync(
            WorkspaceRelativePath directory,
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
    }
}
