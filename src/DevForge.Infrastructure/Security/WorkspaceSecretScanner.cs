using System.Buffers;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using DevForge.Application.Contracts;
using DevForge.Domain.Privacy;

namespace DevForge.Infrastructure.Security;

public sealed class WorkspaceSecretScanner : ISecretScanner
{
    private const int MaxFileBytes = 1_048_576;
    private const int ReadBufferSize = 8_192;
    // Immutable React 1.0.0 public bundle; exact-byte provenance and fixture in ADR-0028.
    // This is not a file exemption: only one reviewed generic-assignment occurrence differs.
    private const string ReviewedReactBundleHash = "0dc53246ec934df87e6acfa00a2471debd43f04b14226866942282655cb5236d";

    private static readonly ImmutableHashSet<string> _textExtensions =
        ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            ".config",
            ".cs",
            ".csproj",
            ".css",
            ".cjs",
            ".env",
            ".fsproj",
            ".html",
            ".js",
            ".json",
            ".jsx",
            ".md",
            ".mjs",
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
            ".user",
            ".vbproj",
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

    private static async Task<ScannableText?> ReadBoundedTextAsync(
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
                if (IsJavascript(path)) { throw ScanFailure(); }
                return null;
            }

            var text = new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true).GetString(bytes);
            return new ScannableText(text, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        }
        finally
        {
            Array.Clear(buffer, 0, buffer.Length);
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void ScanText(
        WorkspaceRelativePath path,
        ScannableText contents,
        ImmutableArray<SecretFinding>.Builder findings,
        CancellationToken cancellationToken)
    {
        using var reader = new StringReader(contents.Text);
        var reviewedReactBundle = Path.GetExtension(path.Value).Equals(".js", StringComparison.OrdinalIgnoreCase)
            && contents.RawByteHash == ReviewedReactBundleHash;
        var lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lineNumber++;
            // The complete UTF-8 file is already bounded to 1 MiB. Scan a minified
            // line intact so tokens spanning arbitrary chunk boundaries cannot escape.
            // Each catalog expression still has its bounded execution timeout.
            foreach (var category in SecretPatternCatalog.FindCategories(line,
                         reviewedReactBundle && lineNumber == 8))
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

    private static bool IsJavascript(WorkspaceRelativePath path) => Path.GetExtension(path.Value).ToLowerInvariant()
        is ".js" or ".mjs" or ".cjs";

    private sealed record ScannableText(string Text, string RawByteHash);

    private static InfrastructureOperationException ScanFailure()
    {
        return new InfrastructureOperationException(
            "DF-SCAN-001",
            "The workspace secret scan could not be completed safely.");
    }
}
