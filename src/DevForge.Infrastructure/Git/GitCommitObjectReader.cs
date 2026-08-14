using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using DevForge.Application.Contracts;

namespace DevForge.Infrastructure.Git;

internal sealed record GitCommitEvidence(
    string CommitId,
    string TreeId,
    string? ParentCommitId,
    string AuthorName,
    string AuthorEmail,
    string CommitterName,
    string CommitterEmail,
    string Subject);

internal static class GitCommitObjectReader
{
    private const int MaximumCompressedBytes = 1024 * 1024;
    private const int MaximumObjectBytes = 4 * 1024 * 1024;
    private static readonly UTF8Encoding _strictUtf8 = new(false, true);

    public static async Task<GitCommitEvidence> ReadAsync(
        IWorkspaceFileSystem workspace,
        string commitId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (!PublicationSnapshot.IsObjectId(commitId))
        {
            throw UnsafeRepository();
        }

        var objectBytes = await ReadLooseObjectAsync(
            workspace,
            commitId,
            MaximumCompressedBytes,
            MaximumObjectBytes,
            cancellationToken).ConfigureAwait(false);
        VerifyObjectIdentity(commitId, objectBytes);
        var separator = Array.IndexOf(objectBytes, (byte)0);
        if (separator <= 0)
        {
            throw UnsafeRepository();
        }

        var header = Encoding.ASCII.GetString(objectBytes, 0, separator);
        var expectedHeader = "commit " + (objectBytes.Length - separator - 1);
        if (!StringComparer.Ordinal.Equals(header, expectedHeader))
        {
            throw UnsafeRepository();
        }

        var payload = _strictUtf8.GetString(objectBytes, separator + 1, objectBytes.Length - separator - 1);
        var bodySeparator = payload.IndexOf("\n\n", StringComparison.Ordinal);
        if (bodySeparator <= 0)
        {
            throw UnsafeRepository();
        }

        string? tree = null;
        string? parent = null;
        string? authorName = null;
        string? authorEmail = null;
        string? committerName = null;
        string? committerEmail = null;
        foreach (var line in payload[..bodySeparator].Split('\n'))
        {
            if (line.StartsWith("tree ", StringComparison.Ordinal) && tree is null)
            {
                tree = line[5..];
            }
            else if (line.StartsWith("parent ", StringComparison.Ordinal) && parent is null)
            {
                parent = line[7..];
            }
            else if (line.StartsWith("author ", StringComparison.Ordinal) && authorName is null)
            {
                (authorName, authorEmail) = ParseIdentity(line[7..]);
            }
            else if (line.StartsWith("committer ", StringComparison.Ordinal) && committerName is null)
            {
                (committerName, committerEmail) = ParseIdentity(line[10..]);
            }
            else
            {
                throw UnsafeRepository();
            }
        }

        if (!IsObjectId(tree, commitId.Length)
            || parent is not null && !IsObjectId(parent, commitId.Length)
            || authorName is null
            || authorEmail is null
            || committerName is null
            || committerEmail is null)
        {
            throw UnsafeRepository();
        }

        var message = payload[(bodySeparator + 2)..];
        if (!message.EndsWith('\n')
            || message[..^1].Contains('\n'))
        {
            throw UnsafeRepository();
        }

        return new GitCommitEvidence(
            commitId,
            tree!,
            parent,
            authorName,
            authorEmail,
            committerName,
            committerEmail,
            message[..^1]);
    }

    internal static async Task<byte[]> ReadLooseObjectAsync(
        IWorkspaceFileSystem workspace,
        string objectId,
        int maximumCompressedBytes,
        int maximumObjectBytes,
        CancellationToken cancellationToken)
    {
        if (!PublicationSnapshot.IsObjectId(objectId))
        {
            throw UnsafeRepository();
        }

        var path = WorkspaceRelativePath.Create(
            $".git\\objects\\{objectId[..2]}\\{objectId[2..]}").Value;
        await using var compressed = await workspace.OpenReadAsync(path, cancellationToken)
            .ConfigureAwait(false);
        if (compressed.Length <= 0 || compressed.Length > maximumCompressedBytes)
        {
            throw UnsafeRepository();
        }

        await using var inflater = new ZLibStream(compressed, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await inflater.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (output.Length > maximumObjectBytes - read)
            {
                throw UnsafeRepository();
            }

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    internal static void VerifyObjectIdentity(string objectId, ReadOnlySpan<byte> objectBytes)
    {
#pragma warning disable CA5350 // Git SHA-1 object repositories require protocol-compatible object identity verification.
        var actual = objectId.Length == 40
            ? SHA1.HashData(objectBytes)
            : SHA256.HashData(objectBytes);
#pragma warning restore CA5350
        if (!StringComparer.Ordinal.Equals(Convert.ToHexStringLower(actual), objectId))
        {
            throw UnsafeRepository();
        }
    }

    private static (string Name, string Email) ParseIdentity(string value)
    {
        var emailStart = value.LastIndexOf(" <", StringComparison.Ordinal);
        var emailEnd = value.LastIndexOf("> ", StringComparison.Ordinal);
        if (emailStart <= 0 || emailEnd <= emailStart + 2)
        {
            throw UnsafeRepository();
        }

        var timestamp = value[(emailEnd + 2)..].Split(' ');
        if (timestamp.Length != 2
            || timestamp[0].Length == 0
            || !timestamp[0].All(char.IsAsciiDigit)
            || timestamp[1].Length != 5
            || timestamp[1][0] is not ('+' or '-')
            || !timestamp[1][1..].All(char.IsAsciiDigit))
        {
            throw UnsafeRepository();
        }

        return (value[..emailStart], value[(emailStart + 2)..emailEnd]);
    }

    private static bool IsObjectId(string? value, int expectedLength) =>
        value is not null
        && value.Length == expectedLength
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static InfrastructureOperationException UnsafeRepository() => new(
        "DF-GIT-001",
        "The local Git object evidence is not an exact DevForge bootstrap object.");
}
