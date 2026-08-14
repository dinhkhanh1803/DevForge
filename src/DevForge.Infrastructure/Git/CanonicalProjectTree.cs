using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using DevForge.Application.Contracts;
using DevForge.Infrastructure.Execution;
using DevForge.Infrastructure.FileSystem;

namespace DevForge.Infrastructure.Git;

internal sealed record CanonicalProjectTreeSnapshot(
    string Digest,
    ImmutableArray<WorkspaceRelativePath> SourceFiles,
    bool HasRootGit);

internal static class CanonicalProjectTree
{
    private const int BufferSize = 81920;
    private static readonly WorkspaceRelativePath _rootGit =
        WorkspaceRelativePath.Create(".git").Value;

    public static async Task<CanonicalProjectTreeSnapshot> CaptureAsync(
        IWorkspaceFileSystem workspace,
        bool allowOwnedRootGit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        cancellationToken.ThrowIfCancellationRequested();
        var rootGitFile = await workspace.FileExistsAsync(
            _rootGit,
            cancellationToken).ConfigureAwait(false);
        var rootGitDirectory = await workspace.DirectoryExistsAsync(
            _rootGit,
            cancellationToken).ConfigureAwait(false);
        if (rootGitFile || rootGitDirectory && !allowOwnedRootGit)
        {
            throw UnsafeTree();
        }

        ImmutableArray<WorkspaceRelativePath> sourceFiles;
        if (workspace is IBoundedWorkspaceEnumerator bounded)
        {
            var enumeration = await bounded.EnumerateTreeBoundedAsync(
                rootGitDirectory ? _rootGit : null,
                AtomicProjectFinalizer.MaximumFileCount,
                AtomicProjectFinalizer.MaximumDirectoryCount,
                AtomicProjectFinalizer.MaximumPathDepth,
                cancellationToken).ConfigureAwait(false);
            if (enumeration.LimitExceeded
                || enumeration.Files.Any(IsNestedGitPath)
                || enumeration.Directories.Any(IsNestedGitPath))
            {
                throw UnsafeTree();
            }

            sourceFiles = enumeration.Files;
        }
        else
        {
            if (rootGitDirectory)
            {
                throw UnsafeTree();
            }

            await ValidateDirectoriesAsync(workspace, rootGitDirectory, cancellationToken)
                .ConfigureAwait(false);
            var allFiles = await workspace.EnumerateAllFilesAsync(cancellationToken)
                .ConfigureAwait(false);
            sourceFiles = allFiles
                .OrderBy(path => path.Value, StringComparer.Ordinal)
                .ToImmutableArray();
        }

        if (sourceFiles.Length > AtomicProjectFinalizer.MaximumFileCount
            || sourceFiles.Any(path => Depth(path) > AtomicProjectFinalizer.MaximumPathDepth))
        {
            throw UnsafeTree();
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long aggregateBytes = 0;
        foreach (var file in sourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hash.AppendData(Encoding.UTF8.GetBytes(file.Value.Replace('\\', '/')));
            hash.AppendData([0]);
            await using var input = await workspace.OpenReadAsync(file, cancellationToken)
                .ConfigureAwait(false);
            if (input.Length < 0
                || input.Length > AtomicProjectFinalizer.MaximumFileBytes
                || aggregateBytes > AtomicProjectFinalizer.MaximumAggregateBytes - input.Length)
            {
                throw UnsafeTree();
            }

            aggregateBytes += input.Length;
            var lengthBytes = new byte[sizeof(long)];
            BinaryPrimitives.WriteInt64BigEndian(lengthBytes, input.Length);
            hash.AppendData(lengthBytes);
            var buffer = new byte[BufferSize];
            while (true)
            {
                var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                hash.AppendData(buffer, 0, read);
            }
        }

        return new CanonicalProjectTreeSnapshot(
            $"sha256:{Convert.ToHexStringLower(hash.GetHashAndReset())}",
            sourceFiles,
            rootGitDirectory);
    }

    private static async Task ValidateDirectoriesAsync(
        IWorkspaceFileSystem workspace,
        bool hasRootGit,
        CancellationToken cancellationToken)
    {
        var roots = await workspace.EnumerateRootDirectoriesAsync(cancellationToken)
            .ConfigureAwait(false);
        var queue = new Queue<WorkspaceRelativePath>(roots.Where(path =>
            !path.Equals(_rootGit)));
        var count = 0;
        while (queue.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = queue.Dequeue();
            count++;
            if (IsNestedGitPath(directory)
                || count > AtomicProjectFinalizer.MaximumDirectoryCount
                || Depth(directory) > AtomicProjectFinalizer.MaximumPathDepth)
            {
                throw UnsafeTree();
            }

            var children = await workspace.EnumerateDirectoriesAsync(
                directory,
                cancellationToken).ConfigureAwait(false);
            foreach (var child in children)
            {
                queue.Enqueue(child);
            }
        }

        if (roots.Any(path => path.Equals(_rootGit)) != hasRootGit)
        {
            throw UnsafeTree();
        }
    }

    private static bool IsNestedGitPath(WorkspaceRelativePath path)
    {
        var segments = path.Value.Split('\\');
        return segments.Skip(1).Any(segment =>
            segment.Equals(".git", StringComparison.OrdinalIgnoreCase));
    }

    private static int Depth(WorkspaceRelativePath path) =>
        path.Value.Count(character => character == '\\') + 1;

    private static InfrastructureOperationException UnsafeTree() => new(
        "DF-GIT-004",
        "The finalized project tree is not safe for Git publication.");
}
