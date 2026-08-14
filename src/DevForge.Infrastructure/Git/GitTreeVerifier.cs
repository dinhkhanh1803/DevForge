using System.Collections.Immutable;
using System.Text;
using DevForge.Application.Contracts;
using DevForge.Infrastructure.Execution;

namespace DevForge.Infrastructure.Git;

internal sealed record GitTreeEvidence(
    ImmutableHashSet<string> ObjectIds,
    ImmutableDictionary<string, string> Blobs,
    ImmutableDictionary<string, string> Trees);

internal static class GitTreeVerifier
{
    private static readonly UTF8Encoding _strictUtf8 = new(false, true);

    public static async Task<GitTreeEvidence> VerifyAsync(
        IWorkspaceFileSystem workspace,
        CanonicalProjectTreeSnapshot projectTree,
        string treeId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(projectTree);
        if (!IsObjectId(treeId))
        {
            throw Mismatch();
        }

        var blobs = new Dictionary<string, string>(StringComparer.Ordinal);
        var objectIds = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        var trees = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        var directoryCount = -1;
        await ReadTreeAsync(
            workspace,
            treeId,
            prefix: null,
            depth: 0,
            blobs,
            objectIds,
            trees,
            () => ++directoryCount,
            cancellationToken).ConfigureAwait(false);
        if (!IsDirectoryCountWithinBounds(directoryCount + 1)
            || blobs.Count != projectTree.SourceFiles.Length)
        {
            throw Mismatch();
        }

        var committedBlobs = blobs.ToImmutableDictionary(StringComparer.Ordinal);

        foreach (var path in projectTree.SourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!blobs.Remove(path.Value, out var objectId))
            {
                throw Mismatch();
            }

            objectIds.Add(objectId);

            var committed = await ReadBlobAsync(workspace, objectId, cancellationToken)
                .ConfigureAwait(false);

            await using var source = await workspace.OpenReadAsync(path, cancellationToken)
                .ConfigureAwait(false);
            if (source.Length != committed.Length)
            {
                throw Mismatch();
            }

            var offset = 0;
            var buffer = new byte[81920];
            while (offset < committed.Length)
            {
                var read = await source.ReadAsync(
                    buffer.AsMemory(0, Math.Min(buffer.Length, committed.Length - offset)),
                    cancellationToken).ConfigureAwait(false);
                if (read == 0
                    || !buffer.AsSpan(0, read).SequenceEqual(
                        committed.AsSpan(offset, read)))
                {
                    throw Mismatch();
                }

                offset += read;
            }

            if (await source.ReadByteAsync(cancellationToken).ConfigureAwait(false) != -1)
            {
                throw Mismatch();
            }
        }

        if (blobs.Count != 0)
        {
            throw Mismatch();
        }


        return new GitTreeEvidence(
            objectIds.ToImmutable(),
            committedBlobs,
            trees.ToImmutable());
    }

    private static async Task ReadTreeAsync(
        IWorkspaceFileSystem workspace,
        string treeId,
        string? prefix,
        int depth,
        Dictionary<string, string> blobs,
        ImmutableHashSet<string>.Builder objectIds,
        ImmutableDictionary<string, string>.Builder trees,
        Func<int> incrementDirectoryCount,
        CancellationToken cancellationToken)
    {
        if (depth > AtomicProjectFinalizer.MaximumPathDepth
            || incrementDirectoryCount() > AtomicProjectFinalizer.MaximumDirectoryCount)
        {
            throw Mismatch();
        }

        objectIds.Add(treeId);
        if (!trees.TryAdd(prefix ?? string.Empty, treeId))
        {
            throw Mismatch();
        }

        var objectBytes = await GitCommitObjectReader.ReadLooseObjectAsync(
            workspace,
            treeId,
            checked((int)Math.Min(
                AtomicProjectFinalizer.MaximumFileBytes,
                int.MaxValue)),
            checked((int)Math.Min(
                AtomicProjectFinalizer.MaximumAggregateBytes,
                int.MaxValue)),
            cancellationToken).ConfigureAwait(false);
        GitCommitObjectReader.VerifyObjectIdentity(treeId, objectBytes);
        var separator = Array.IndexOf(objectBytes, (byte)0);
        if (separator <= 0
            || !StringComparer.Ordinal.Equals(
                Encoding.ASCII.GetString(objectBytes, 0, separator),
                "tree " + (objectBytes.Length - separator - 1)))
        {
            throw Mismatch();
        }

        var hashBytes = treeId.Length / 2;
        var offset = separator + 1;
        while (offset < objectBytes.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var modeEnd = Array.IndexOf(objectBytes, (byte)' ', offset);
            var nameEnd = modeEnd < 0 ? -1 : Array.IndexOf(objectBytes, (byte)0, modeEnd + 1);
            if (modeEnd <= offset
                || nameEnd <= modeEnd + 1
                || nameEnd > objectBytes.Length - hashBytes - 1)
            {
                throw Mismatch();
            }

            var mode = Encoding.ASCII.GetString(objectBytes, offset, modeEnd - offset);
            var name = _strictUtf8.GetString(
                objectBytes,
                modeEnd + 1,
                nameEnd - modeEnd - 1);
            if (name is "." or ".."
                || name.Length == 0
                || name.Contains('/')
                || name.Contains('\\'))
            {
                throw Mismatch();
            }

            var objectId = Convert.ToHexStringLower(
                objectBytes.AsSpan(nameEnd + 1, hashBytes));
            var path = prefix is null ? name : prefix + "\\" + name;
            if (!WorkspaceRelativePath.Create(path).IsValid)
            {
                throw Mismatch();
            }

            if (mode == "40000")
            {
                await ReadTreeAsync(
                    workspace,
                    objectId,
                    path,
                    depth + 1,
                    blobs,
                    objectIds,
                    trees,
                    incrementDirectoryCount,
                    cancellationToken).ConfigureAwait(false);
            }
            else if (mode == "100644")
            {
                if (blobs.Count >= AtomicProjectFinalizer.MaximumFileCount
                    || !blobs.TryAdd(path, objectId))
                {
                    throw Mismatch();
                }
            }
            else
            {
                throw Mismatch();
            }

            offset = nameEnd + 1 + hashBytes;
        }

        if (offset != objectBytes.Length)
        {
            throw Mismatch();
        }
    }

    private static async Task<byte[]> ReadBlobAsync(
        IWorkspaceFileSystem workspace,
        string objectId,
        CancellationToken cancellationToken)
    {
        var maximum = MaximumCompressedBlobBytes();
        var objectBytes = await GitCommitObjectReader.ReadLooseObjectAsync(
            workspace,
            objectId,
            maximum,
            maximum,
            cancellationToken).ConfigureAwait(false);
        GitCommitObjectReader.VerifyObjectIdentity(objectId, objectBytes);
        var separator = Array.IndexOf(objectBytes, (byte)0);
        if (separator <= 0
            || !StringComparer.Ordinal.Equals(
                Encoding.ASCII.GetString(objectBytes, 0, separator),
                "blob " + (objectBytes.Length - separator - 1)))
        {
            throw Mismatch();
        }

        return objectBytes[(separator + 1)..];
    }

    internal static int MaximumCompressedBlobBytes()
    {
        var rawBytes = AtomicProjectFinalizer.MaximumFileBytes + 128;
        var deflateBlockOverhead = ((rawBytes / 16_383) + 1) * 5;
        return checked((int)Math.Min(rawBytes + deflateBlockOverhead + 64, int.MaxValue));
    }

    internal static bool IsDirectoryCountWithinBounds(int visitedTreeCount) =>
        visitedTreeCount >= 1
        && visitedTreeCount - 1 <= AtomicProjectFinalizer.MaximumDirectoryCount;

    private static bool IsObjectId(string value) =>
        value.Length is 40 or 64
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static InfrastructureOperationException Mismatch() => new(
        "DF-GIT-004",
        "The committed Git tree does not exactly match the finalized project tree.");
}

internal static class StreamByteExtensions
{
    public static async ValueTask<int> ReadByteAsync(
        this Stream stream,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[1];
        var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        return read == 0 ? -1 : buffer[0];
    }
}
