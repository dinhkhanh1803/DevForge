using System.Buffers;
using System.Collections.Immutable;
using System.Text;
using DevForge.Application.Contracts;
using DevForge.Domain.Privacy;

namespace DevForge.Infrastructure.Security;

public sealed class WorkspaceSecretScanner : ISecretScanner
{
    private const int MaxFileBytes = 1_048_576;
    private const int MaxLineCharacters = 16_384;
    private const int ReadBufferSize = 8_192;

    private static readonly ImmutableHashSet<string> _textExtensions =
        ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            ".config",
            ".cs",
            ".css",
            ".env",
            ".html",
            ".js",
            ".json",
            ".jsx",
            ".md",
            ".pem",
            ".props",
            ".ps1",
            ".sh",
            ".sln",
            ".sql",
            ".targets",
            ".ts",
            ".tsx",
            ".txt",
            ".xml",
            ".yaml",
            ".yml");

    public async Task<SecretScanResult> ScanAsync(
        SecretScanRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var paths = await ResolvePathsAsync(request, cancellationToken).ConfigureAwait(false);
            var findings = ImmutableArray.CreateBuilder<SecretFinding>();
            foreach (var path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsTextCandidate(path))
                {
                    continue;
                }

                var contents = await ReadBoundedTextAsync(
                    request.Workspace,
                    path,
                    cancellationToken).ConfigureAwait(false);
                if (contents is null)
                {
                    continue;
                }

                ScanText(path, contents, findings, cancellationToken);
            }

            return SecretScanResult.Create(findings).Value;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InfrastructureOperationException exception) when (exception.Code == "DF-SCAN-001")
        {
            throw;
        }
        catch (Exception exception) when (exception is InfrastructureOperationException
            or IOException
            or UnauthorizedAccessException
            or DecoderFallbackException
            or System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            throw ScanFailure();
        }
    }

    private static async Task<ImmutableArray<WorkspaceRelativePath>> ResolvePathsAsync(
        SecretScanRequest request,
        CancellationToken cancellationToken)
    {
        return request.Scope switch
        {
            SecretScanScope.WholeWorkspace =>
                await request.Workspace.EnumerateAllFilesAsync(cancellationToken).ConfigureAwait(false),
            SecretScanScope.ExplicitPaths => request.Paths,
            _ => throw ScanFailure(),
        };
    }

    private static async Task<string?> ReadBoundedTextAsync(
        IWorkspaceFileSystem workspace,
        WorkspaceRelativePath path,
        CancellationToken cancellationToken)
    {
        await using var stream = await workspace.OpenReadAsync(path, cancellationToken)
            .ConfigureAwait(false);
        var buffer = ArrayPool<byte>.Shared.Rent(ReadBufferSize);
        using var contents = new MemoryStream();
        try
        {
            while (true)
            {
                var bytesRead = await stream.ReadAsync(
                    buffer.AsMemory(0, ReadBufferSize),
                    cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    break;
                }

                if (contents.Length + bytesRead > MaxFileBytes)
                {
                    throw ScanFailure();
                }

                contents.Write(buffer, 0, bytesRead);
            }

            var bytes = contents.ToArray();
            if (IsBinary(bytes))
            {
                return null;
            }

            return new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true).GetString(bytes);
        }
        finally
        {
            Array.Clear(buffer, 0, buffer.Length);
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void ScanText(
        WorkspaceRelativePath path,
        string contents,
        ImmutableArray<SecretFinding>.Builder findings,
        CancellationToken cancellationToken)
    {
        using var reader = new StringReader(contents);
        var lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lineNumber++;
            if (line.Length > MaxLineCharacters)
            {
                throw ScanFailure();
            }

            foreach (var category in SecretPatternCatalog.FindCategories(line))
            {
                var safeCategory = category.Equals(
                    "bearer credential",
                    StringComparison.OrdinalIgnoreCase)
                    ? "bearer-style credential"
                    : category;
                var description = RedactedText.FromTrustedRedaction(
                    "Potential " + safeCategory + " detected.").Value;
                findings.Add(SecretFinding.Create(path, lineNumber, description).Value);
            }
        }
    }

    private static bool IsTextCandidate(WorkspaceRelativePath path)
    {
        var fileName = Path.GetFileName(path.Value);
        return _textExtensions.Contains(Path.GetExtension(fileName))
            || fileName.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith(".env", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals(".gitignore", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBinary(ReadOnlySpan<byte> contents)
    {
        var prefixLength = Math.Min(contents.Length, 4_096);
        return contents[..prefixLength].Contains((byte)0);
    }

    private static InfrastructureOperationException ScanFailure()
    {
        return new InfrastructureOperationException(
            "DF-SCAN-001",
            "The workspace secret scan could not be completed safely.");
    }
}
