using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DevForge.Application.Contracts;

namespace DevForge.Infrastructure.Blueprints;

internal static class BlueprintChecksumVerifier
{
    internal const int MaximumFiles = 2048;
    internal const long MaximumDeclaredBytes = 32L * 1024L * 1024L;

    private const string ChecksumFileName = "checksums.json";
    private static readonly UTF8Encoding _utf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    internal static async Task<BlueprintChecksumResult> VerifyAsync(
        IWorkspaceFileSystem workspace,
        WorkspaceRelativePath packageDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(packageDirectory);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var checksumPath = Combine(packageDirectory, ChecksumFileName);
            await using var checksumStream = await workspace.OpenReadAsync(
                checksumPath,
                cancellationToken).ConfigureAwait(false);
            var checksumText = await BlueprintControlReadSupport.ReadTextAsync(
                checksumStream,
                cancellationToken).ConfigureAwait(false);
            if (!checksumText.IsValid)
            {
                return BlueprintChecksumResult.Failure(checksumText.Issue!);
            }

            var declaredResult = ParseDeclarations(checksumText.Text!);
            if (!declaredResult.Issues.IsEmpty)
            {
                return declaredResult;
            }

            var files = await workspace.EnumerateFilesAsync(
                packageDirectory,
                recursive: true,
                cancellationToken).ConfigureAwait(false);
            if (files.Length > MaximumFiles)
            {
                return BoundsFailure();
            }

            var actualPaths = CreateActualPathMap(packageDirectory, files);
            if (actualPaths is null
                || !HaveSamePaths(declaredResult.DeclaredHashes, actualPaths))
            {
                return IntegrityFailure();
            }

            long totalBytes = 0;
            using var aggregate = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var verifiedControlFiles = ImmutableDictionary.CreateBuilder<string, ImmutableArray<byte>>(
                StringComparer.Ordinal);
            foreach (var declaration in declaredResult.DeclaredHashes.OrderBy(
                         item => item.Key,
                         StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var contentResult = await HashContentAsync(
                    workspace,
                    actualPaths[declaration.Key],
                    MaximumDeclaredBytes - totalBytes,
                    IsControlFile(declaration.Key),
                    cancellationToken).ConfigureAwait(false);
                if (contentResult.ExceedsBound)
                {
                    return BoundsFailure();
                }

                totalBytes = checked(totalBytes + contentResult.BytesRead);
                if (!HashesEqual(contentResult.Hash!, declaration.Value))
                {
                    return IntegrityFailure();
                }

                if (contentResult.Content is not null)
                {
                    verifiedControlFiles.Add(
                        declaration.Key,
                        ImmutableArray.CreateRange(contentResult.Content));
                }

                AppendAggregateEntry(aggregate, declaration.Key, declaration.Value);
            }

            var aggregateChecksum = "sha256:"
                + Convert.ToHexString(aggregate.GetHashAndReset()).ToLowerInvariant();
            return BlueprintChecksumResult.Success(
                aggregateChecksum,
                declaredResult.DeclaredHashes,
                verifiedControlFiles.ToImmutable());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InfrastructureOperationException
            or IOException
            or UnauthorizedAccessException
            or JsonException
            or DecoderFallbackException
            or ArgumentException
            or OverflowException)
        {
            return IntegrityFailure();
        }
    }

    private static BlueprintChecksumResult ParseDeclarations(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = BlueprintControlLimits.MaximumDepth,
                });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return IntegrityFailure();
            }

            var declarations = ImmutableDictionary.CreateBuilder<string, string>(
                StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!TryNormalizeDeclaredPath(property.Name, out var path)
                    || property.Value.ValueKind != JsonValueKind.String
                    || !IsLowercaseSha256(property.Value.GetString())
                    || !declarations.TryAdd(path!, property.Value.GetString()!))
                {
                    return IntegrityFailure();
                }
            }

            return BlueprintChecksumResult.Parsed(declarations.ToImmutable());
        }
        catch (JsonException)
        {
            return IntegrityFailure();
        }
    }

    private static Dictionary<string, WorkspaceRelativePath>? CreateActualPathMap(
        WorkspaceRelativePath packageDirectory,
        ImmutableArray<WorkspaceRelativePath> files)
    {
        var prefix = packageDirectory.Value + '\\';
        var actual = new Dictionary<string, WorkspaceRelativePath>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            if (!file.Value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || file.Value.Length <= prefix.Length)
            {
                return null;
            }

            var relative = file.Value[prefix.Length..].Replace('\\', '/');
            if (string.Equals(relative, ChecksumFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!TryNormalizeDeclaredPath(relative, out var normalized)
                || !actual.TryAdd(normalized!, file))
            {
                return null;
            }
        }

        return actual;
    }

    private static bool HaveSamePaths(
        ImmutableDictionary<string, string> declared,
        Dictionary<string, WorkspaceRelativePath> actual)
    {
        return declared.Count == actual.Count
            && declared.Keys.All(actual.ContainsKey);
    }

    private static async Task<ContentHashResult> HashContentAsync(
        IWorkspaceFileSystem workspace,
        WorkspaceRelativePath path,
        long remainingBytes,
        bool captureContent,
        CancellationToken cancellationToken)
    {
        if (remainingBytes < 0)
        {
            return ContentHashResult.TooLarge;
        }

        await using var stream = await workspace.OpenReadAsync(path, cancellationToken).ConfigureAwait(false);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var captured = captureContent ? new MemoryStream() : null;
        var buffer = new byte[81920];
        long bytesRead = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            bytesRead = checked(bytesRead + read);
            if (bytesRead > remainingBytes)
            {
                return ContentHashResult.TooLarge;
            }

            hash.AppendData(buffer, 0, read);
            captured?.Write(buffer, 0, read);
        }

        return new ContentHashResult(
            hash.GetHashAndReset(),
            bytesRead,
            ExceedsBound: false,
            captured?.ToArray());
    }

    private static void AppendAggregateEntry(
        IncrementalHash aggregate,
        string path,
        string declaredHash)
    {
        aggregate.AppendData(_utf8.GetBytes(path));
        aggregate.AppendData([0]);
        aggregate.AppendData(_utf8.GetBytes(declaredHash));
        aggregate.AppendData([(byte)'\n']);
    }

    private static bool HashesEqual(byte[] actual, string declared)
    {
        var declaredBytes = Convert.FromHexString(declared);
        return CryptographicOperations.FixedTimeEquals(actual, declaredBytes);
    }

    private static bool TryNormalizeDeclaredPath(string value, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(value)
            || value != value.Trim()
            || value.Any(char.IsControl)
            || value.Contains('\\')
            || value.StartsWith('/')
            || value.Contains(':')
            || string.Equals(value, ChecksumFileName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = value.Split('/');
        if (segments.Any(segment => segment is "" or "." or ".."))
        {
            return false;
        }

        var workspacePath = WorkspaceRelativePath.Create(string.Join('\\', segments));
        if (!workspacePath.IsValid)
        {
            return false;
        }

        normalized = string.Join('/', segments);
        return true;
    }

    private static bool IsLowercaseSha256(string? value)
    {
        return value is not null
            && value.Length == 64
            && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    private static bool IsControlFile(string path)
    {
        return path is "manifest.yaml" or "inputs.schema.json" or "rules.yaml";
    }

    private static WorkspaceRelativePath Combine(
        WorkspaceRelativePath directory,
        string relativePath)
    {
        return WorkspaceRelativePath.Create(
            directory.Value + '\\' + relativePath.Replace('/', '\\')).Value;
    }

    private static BlueprintChecksumResult IntegrityFailure()
    {
        return BlueprintChecksumResult.Failure(BlueprintInspectionIssue.Create(
            "DF-BP-002",
            "Blueprint package integrity could not be verified.").Value);
    }

    private static BlueprintChecksumResult BoundsFailure()
    {
        return BlueprintChecksumResult.Failure(BlueprintInspectionIssue.Create(
            "DF-BP-004",
            "The blueprint package exceeds a supported bound.").Value);
    }

    private sealed record ContentHashResult(
        byte[]? Hash,
        long BytesRead,
        bool ExceedsBound,
        byte[]? Content)
    {
        internal static ContentHashResult TooLarge { get; } = new(
            null,
            0,
            ExceedsBound: true,
            null);
    }
}

internal sealed record BlueprintChecksumResult(
    string? AggregateChecksum,
    ImmutableDictionary<string, string> DeclaredHashes,
    ImmutableDictionary<string, ImmutableArray<byte>> VerifiedControlFiles,
    ImmutableArray<BlueprintInspectionIssue> Issues)
{
    internal bool IsValid => AggregateChecksum is not null && Issues.IsEmpty;

    internal static BlueprintChecksumResult Parsed(ImmutableDictionary<string, string> declaredHashes)
    {
        return new BlueprintChecksumResult(
            null,
            declaredHashes,
            ImmutableDictionary<string, ImmutableArray<byte>>.Empty.WithComparers(StringComparer.Ordinal),
            []);
    }

    internal static BlueprintChecksumResult Success(
        string aggregateChecksum,
        ImmutableDictionary<string, string> declaredHashes,
        ImmutableDictionary<string, ImmutableArray<byte>> verifiedControlFiles)
    {
        return new BlueprintChecksumResult(
            aggregateChecksum,
            declaredHashes,
            verifiedControlFiles,
            []);
    }

    internal static BlueprintChecksumResult Failure(BlueprintInspectionIssue issue)
    {
        return new BlueprintChecksumResult(
            null,
            ImmutableDictionary<string, string>.Empty.WithComparers(StringComparer.Ordinal),
            ImmutableDictionary<string, ImmutableArray<byte>>.Empty.WithComparers(StringComparer.Ordinal),
            [issue]);
    }
}
