using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using DevForge.Application.Contracts;
using DevForge.Infrastructure.Execution;

namespace DevForge.Infrastructure.Git;

internal static class GitIndexVerifier
{
    private const uint RegularFileMode = 0x81A4;
    private static readonly UTF8Encoding _strictUtf8 = new(false, true);
    private static readonly WorkspaceRelativePath _index =
        WorkspaceRelativePath.Create(".git\\index").Value;

    public static async Task VerifyAsync(
        IWorkspaceFileSystem workspace,
        int objectIdCharacters,
        ImmutableDictionary<string, string> expectedBlobs,
        ImmutableDictionary<string, string> expectedTrees,
        CancellationToken cancellationToken)
    {
        var hashBytes = objectIdCharacters / 2;
        if (hashBytes is not (20 or 32))
        {
            throw UnsafeRepository();
        }

        var maximumBytes = checked(
            AtomicProjectFinalizer.MaximumFileCount
            * (AtomicProjectFinalizer.MaximumPathDepth * 256 + 128));
        await using var input = await workspace.OpenReadAsync(_index, cancellationToken)
            .ConfigureAwait(false);
        if (input.Length < 12 + hashBytes || input.Length > maximumBytes)
        {
            throw UnsafeRepository();
        }

        var bytes = new byte[checked((int)input.Length)];
        await input.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        var contentLength = bytes.Length - hashBytes;
        VerifyChecksum(bytes.AsSpan(0, contentLength), bytes.AsSpan(contentLength), hashBytes);
        if (!bytes.AsSpan(0, 4).SequenceEqual("DIRC"u8)
            || BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4, 4)) != 2)
        {
            throw UnsafeRepository();
        }

        var count = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(8, 4));
        if (count != expectedBlobs.Count
            || count > AtomicProjectFinalizer.MaximumFileCount)
        {
            throw UnsafeRepository();
        }

        var remaining = expectedBlobs.ToBuilder();
        var offset = 12;
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entryStart = offset;
            var fixedLength = 40 + hashBytes + 2;
            if (offset > contentLength - fixedLength)
            {
                throw UnsafeRepository();
            }

            var mode = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 24, 4));
            var objectId = Convert.ToHexStringLower(bytes.AsSpan(offset + 40, hashBytes));
            var flags = BinaryPrimitives.ReadUInt16BigEndian(
                bytes.AsSpan(offset + 40 + hashBytes, 2));
            if (mode != RegularFileMode || (flags & 0xF000) != 0)
            {
                throw UnsafeRepository();
            }

            var pathStart = offset + fixedLength;
            var nul = Array.IndexOf(bytes, (byte)0, pathStart, contentLength - pathStart);
            if (nul < pathStart)
            {
                throw UnsafeRepository();
            }

            var pathBytes = bytes.AsSpan(pathStart, nul - pathStart);
            var declaredNameLength = flags & 0x0FFF;
            if (declaredNameLength != Math.Min(pathBytes.Length, 0x0FFF))
            {
                throw UnsafeRepository();
            }

            var gitPath = _strictUtf8.GetString(pathBytes);
            if (gitPath.Contains('\\')
                || !WorkspaceRelativePath.Create(gitPath.Replace('/', '\\')).IsValid)
            {
                throw UnsafeRepository();
            }

            var workspacePath = gitPath.Replace('/', '\\');
            if (!remaining.Remove(workspacePath, out var expectedObjectId)
                || !StringComparer.Ordinal.Equals(objectId, expectedObjectId))
            {
                throw UnsafeRepository();
            }

            var unpaddedLength = nul + 1 - entryStart;
            offset = checked(nul + 1 + ((8 - unpaddedLength % 8) % 8));
        }

        if (remaining.Count != 0)
        {
            throw UnsafeRepository();
        }

        if (offset < contentLength)
        {
            offset = VerifyCacheTreeExtension(
                bytes,
                offset,
                contentLength,
                hashBytes,
                expectedTrees,
                count);
        }

        if (offset != contentLength)
        {
            throw UnsafeRepository();
        }
    }

    private static int VerifyCacheTreeExtension(
        byte[] bytes,
        int offset,
        int contentLength,
        int hashBytes,
        ImmutableDictionary<string, string> expectedTrees,
        uint indexEntryCount)
    {
        if (offset > contentLength - 8
            || !bytes.AsSpan(offset, 4).SequenceEqual("TREE"u8))
        {
            throw UnsafeRepository();
        }

        var payloadLength = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 4, 4));
        if (payloadLength > contentLength - offset - 8)
        {
            throw UnsafeRepository();
        }

        var payloadEnd = checked(offset + 8 + (int)payloadLength);
        if (payloadEnd != contentLength)
        {
            throw UnsafeRepository();
        }

        var remainingTrees = expectedTrees.ToBuilder();
        var payloadOffset = offset + 8;
        ParseCacheTreeEntry(
            bytes,
            ref payloadOffset,
            payloadEnd,
            hashBytes,
            parentPath: null,
            remainingTrees,
            indexEntryCount);
        if (payloadOffset != payloadEnd || remainingTrees.Count != 0)
        {
            throw UnsafeRepository();
        }

        return payloadEnd;
    }

    private static void ParseCacheTreeEntry(
        byte[] bytes,
        ref int offset,
        int payloadEnd,
        int hashBytes,
        string? parentPath,
        ImmutableDictionary<string, string>.Builder remainingTrees,
        uint indexEntryCount)
    {
        var pathEnd = Array.IndexOf(bytes, (byte)0, offset, payloadEnd - offset);
        if (pathEnd < offset)
        {
            throw UnsafeRepository();
        }

        var component = _strictUtf8.GetString(bytes.AsSpan(offset, pathEnd - offset));
        if (parentPath is null && component.Length != 0
            || parentPath is not null && (component.Length == 0
                || component.Contains('/') || component.Contains('\\')))
        {
            throw UnsafeRepository();
        }

        var path = parentPath is null
            ? string.Empty
            : parentPath.Length == 0 ? component : parentPath + "\\" + component;
        offset = pathEnd + 1;
        var lineEnd = Array.IndexOf(bytes, (byte)'\n', offset, payloadEnd - offset);
        if (lineEnd < offset)
        {
            throw UnsafeRepository();
        }

        var counts = Encoding.ASCII.GetString(bytes, offset, lineEnd - offset).Split(' ');
        if (counts.Length != 2
            || !int.TryParse(counts[0], out var entryCount)
            || entryCount < 0
            || entryCount > indexEntryCount
            || !int.TryParse(counts[1], out var subtreeCount)
            || subtreeCount < 0
            || subtreeCount > AtomicProjectFinalizer.MaximumDirectoryCount)
        {
            throw UnsafeRepository();
        }

        offset = lineEnd + 1;
        if (offset > payloadEnd - hashBytes)
        {
            throw UnsafeRepository();
        }

        var objectId = Convert.ToHexStringLower(bytes.AsSpan(offset, hashBytes));
        offset += hashBytes;
        if (!remainingTrees.Remove(path, out var expectedObjectId)
            || !StringComparer.Ordinal.Equals(objectId, expectedObjectId))
        {
            throw UnsafeRepository();
        }

        for (var child = 0; child < subtreeCount; child++)
        {
            ParseCacheTreeEntry(
                bytes,
                ref offset,
                payloadEnd,
                hashBytes,
                path,
                remainingTrees,
                indexEntryCount);
        }
    }

    private static void VerifyChecksum(
        ReadOnlySpan<byte> content,
        ReadOnlySpan<byte> checksum,
        int hashBytes)
    {
#pragma warning disable CA5350 // Git SHA-1 index repositories require protocol-compatible checksum verification.
        var actual = hashBytes == 20 ? SHA1.HashData(content) : SHA256.HashData(content);
#pragma warning restore CA5350
        if (!CryptographicOperations.FixedTimeEquals(actual, checksum))
        {
            throw UnsafeRepository();
        }
    }

    private static InfrastructureOperationException UnsafeRepository() => new(
        "DF-GIT-001",
        "The Git index does not exactly match the reviewed project tree.");
}
