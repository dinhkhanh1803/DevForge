using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DevForge.Application.Contracts;
using DevForge.Domain.Diagnostics;
using DevForge.Domain.Environment;
using DevForge.Domain.Privacy;

namespace DevForge.Infrastructure.Diagnostics;

public sealed class SupportBundleWriter : ISupportBundleWriter, ISupportBundleCleanupService
{
    internal const int MaximumEntryBytes = 4 * 1024 * 1024;
    internal const int MaximumBundleBytes = 16 * 1024 * 1024;
    internal const int MaximumEnvironmentTools = 64;
    internal const int MaximumPlanItems = 2048;
    private static readonly UTF8Encoding _utf8 = new(false, true);
    private static readonly WorkspaceRelativePath _bundleDirectory = Relative("support-bundles");
    private static readonly WorkspaceRelativePath _leasePath = Relative("support-bundles\\.export.lease");
    private static readonly DateTimeOffset _zipTimestamp =
        new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly IFileSystem _fileSystem;
    private readonly WorkspaceRoot _localDataRoot;
    private readonly IEnvironmentDoctor _environment;
    private readonly TimeProvider _timeProvider;

    public SupportBundleWriter(
        IFileSystem fileSystem,
        WorkspaceRoot localDataRoot,
        IEnvironmentDoctor environment,
        TimeProvider timeProvider)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _localDataRoot = localDataRoot ?? throw new ArgumentNullException(nameof(localDataRoot));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<ExecutionOperationResult<SupportBundleReceipt>> WriteAsync(
        RunCheckpoint checkpoint,
        bool includeEnvironmentSnapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        cancellationToken.ThrowIfCancellationRequested();
        if (!SupportBundleRequest.Create(checkpoint.Run.Id, includeEnvironmentSnapshot).IsValid)
        {
            return Failure();
        }

        try
        {
            var entries = await CollectEntriesAsync(
                checkpoint,
                includeEnvironmentSnapshot,
                cancellationToken).ConfigureAwait(false);
            var inventory = WriteInventory(checkpoint.Run.Id, entries);
            entries.Add(new BundleEntry("inventory.json", inventory));
            entries.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
            ValidateEntries(entries);

            var archive = WriteArchive(entries);
            if (archive.Length > MaximumBundleBytes)
            {
                return Failure();
            }

            var sha256 = Convert.ToHexString(SHA256.HashData(archive)).ToLowerInvariant();
            var bundleId = $"bundle-{sha256[..24]}";
            var bundlePath = Relative($"support-bundles\\{bundleId}.zip");
            var markerPath = Relative($"support-bundles\\{bundleId}.owner.json");
            var workspace = await _fileSystem.OpenWorkspaceAsync(
                _localDataRoot,
                cancellationToken).ConfigureAwait(false);
            if (workspace is not IAtomicFileWorkspaceFileSystem atomic
                || workspace is not IAtomicWorkspaceFileSystem atomicDirectories
                || workspace is not IExclusiveLeaseWorkspaceFileSystem leases)
            {
                return Failure();
            }

            await workspace.CreateDirectoryAsync(_bundleDirectory, cancellationToken)
                .ConfigureAwait(false);
            await using var lease = await AcquireLeaseAsync(leases, cancellationToken)
                .ConfigureAwait(false);
            if (lease is null)
            {
                return Failure();
            }

            var receipt = await PrepareStagingAsync(
                workspace,
                atomic,
                atomicDirectories,
                bundleId,
                bundlePath,
                sha256,
                archive,
                cancellationToken).ConfigureAwait(false);
            if (receipt is null)
            {
                return Failure();
            }

            var publishedReceipt = await ReadOrCreateMarkerAsync(
                workspace,
                atomic,
                markerPath,
                receipt,
                cancellationToken).ConfigureAwait(false);
            if (publishedReceipt is null || publishedReceipt != receipt)
            {
                return Failure();
            }

            if (await workspace.FileExistsAsync(bundlePath, cancellationToken).ConfigureAwait(false))
            {
                var existing = await DiagnosticLogOwnership.ReadBoundedAsync(
                    workspace,
                    bundlePath,
                    MaximumBundleBytes,
                    cancellationToken).ConfigureAwait(false);
                if (!CryptographicOperations.FixedTimeEquals(
                    SHA256.HashData(existing),
                    SHA256.HashData(archive)))
                {
                    return Failure();
                }
            }

            else
            {
                await atomic.WriteFileAtomicallyAsync(
                    bundlePath,
                    archive,
                    overwrite: false,
                    cancellationToken).ConfigureAwait(false);
            }

            await CleanupStagingAsync(workspace, receipt, CancellationToken.None)
                .ConfigureAwait(false);
            return ExecutionOperationResult.Success(receipt);
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
            or InvalidDataException
            or CryptographicException
            or OverflowException)
        {
            return Failure();
        }
    }

    public async Task<ExecutionOperationResult<SupportBundleCleanupReceipt>> CleanupAsync(
        SupportBundleReceipt receipt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var workspace = await _fileSystem.OpenWorkspaceAsync(
                _localDataRoot,
                cancellationToken).ConfigureAwait(false);
            if (workspace is not IExclusiveLeaseWorkspaceFileSystem leases)
            {
                return CleanupFailure();
            }

            await using var lease = await AcquireLeaseAsync(leases, cancellationToken)
                .ConfigureAwait(false);
            if (lease is null)
            {
                return CleanupFailure();
            }

            var markerPath = Relative($"support-bundles\\{receipt.BundleId}.owner.json");
            var bundleExists = await workspace.FileExistsAsync(
                receipt.RelativePath,
                cancellationToken).ConfigureAwait(false);
            var markerExists = await workspace.FileExistsAsync(markerPath, cancellationToken)
                .ConfigureAwait(false);
            if (!bundleExists && !markerExists)
            {
                return ExecutionOperationResult.Success(
                    new SupportBundleCleanupReceipt(receipt.BundleId, WasPresent: false));
            }

            if (!markerExists)
            {
                return CleanupFailure();
            }

            var marker = await DiagnosticLogOwnership.ReadBoundedAsync(
                workspace,
                markerPath,
                2048,
                cancellationToken).ConfigureAwait(false);
            if (ReadMarker(
                    marker,
                    receipt.BundleId,
                    receipt.RelativePath,
                    receipt.Sha256,
                    receipt.Length) is null)
            {
                return CleanupFailure();
            }

            if (bundleExists)
            {
                var archive = await DiagnosticLogOwnership.ReadBoundedAsync(
                    workspace,
                    receipt.RelativePath,
                    MaximumBundleBytes,
                    cancellationToken).ConfigureAwait(false);
                var sha256 = Convert.ToHexString(SHA256.HashData(archive)).ToLowerInvariant();
                if (archive.LongLength != receipt.Length
                    || !StringComparer.Ordinal.Equals(sha256, receipt.Sha256))
                {
                    return CleanupFailure();
                }

                await workspace.DeleteFileAsync(receipt.RelativePath, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            await workspace.DeleteFileAsync(markerPath, CancellationToken.None).ConfigureAwait(false);
            return ExecutionOperationResult.Success(
                new SupportBundleCleanupReceipt(receipt.BundleId, WasPresent: true));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InfrastructureOperationException
            or IOException
            or UnauthorizedAccessException
            or JsonException
            or CryptographicException
            or OverflowException)
        {
            return CleanupFailure();
        }
    }

    private async Task<List<BundleEntry>> CollectEntriesAsync(
        RunCheckpoint checkpoint,
        bool includeEnvironmentSnapshot,
        CancellationToken cancellationToken)
    {
        if (checkpoint.Plan.Steps.Length > MaximumPlanItems
            || checkpoint.Evidence.Length > MaximumPlanItems)
        {
            throw new InvalidDataException("The support snapshot exceeds its item limit.");
        }

        var entries = new List<BundleEntry>
        {
            new("blueprint/manifest-checksum.json", WriteBlueprintIdentity(checkpoint)),
            new("catalog/error-catalog.json", WriteErrorCatalog()),
            new("run/checkpoint.json", WriteCheckpoint(checkpoint)),
            new("run/plan-summary.json", WritePlanSummary(checkpoint)),
            new("run/recipe-summary.json", WriteRecipeSummary(checkpoint)),
        };
        var runWorkspace = await _fileSystem.OpenWorkspaceAsync(
            checkpoint.RunArtifacts.Root,
            cancellationToken).ConfigureAwait(false);
        if (checkpoint.ReportState == ReportPersistenceState.Succeeded)
        {
            await AddTextFileIfPresentAsync(
                entries,
                runWorkspace,
                Relative($"reports\\{checkpoint.Run.Id}.json"),
                "run/generation-report.json",
                cancellationToken).ConfigureAwait(false);
            await AddTextFileIfPresentAsync(
                entries,
                runWorkspace,
                Relative($"reports\\{checkpoint.Run.Id}.md"),
                "run/generation-report.md",
                cancellationToken).ConfigureAwait(false);
        }

        var localWorkspace = await _fileSystem.OpenWorkspaceAsync(
            _localDataRoot,
            cancellationToken).ConfigureAwait(false);
        var logPath = Relative($"logs\\runs\\{checkpoint.Run.Id}.jsonl");
        if (await localWorkspace.FileExistsAsync(logPath, cancellationToken).ConfigureAwait(false)
            && await DiagnosticLogOwnership.IsVerifiedAsync(
                localWorkspace,
                logPath,
                cancellationToken).ConfigureAwait(false))
        {
            await AddTextFileIfPresentAsync(
                entries,
                localWorkspace,
                logPath,
                "logs/run.jsonl",
                cancellationToken).ConfigureAwait(false);
        }

        if (includeEnvironmentSnapshot)
        {
            var snapshot = await _environment.InspectAsync(cancellationToken).ConfigureAwait(false);
            if (snapshot.Tools.Length > MaximumEnvironmentTools)
            {
                throw new InvalidDataException("The support environment snapshot is oversized.");
            }

            entries.Add(new BundleEntry(
                "environment/tool-status.json",
                WriteEnvironment(snapshot)));
        }

        return entries;
    }

    private static async Task AddTextFileIfPresentAsync(
        ICollection<BundleEntry> entries,
        IWorkspaceFileSystem workspace,
        WorkspaceRelativePath sourcePath,
        string archiveName,
        CancellationToken cancellationToken)
    {
        if (!await workspace.FileExistsAsync(sourcePath, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var content = await DiagnosticLogOwnership.ReadBoundedAsync(
            workspace,
            sourcePath,
            MaximumEntryBytes,
            cancellationToken).ConfigureAwait(false);
        var text = _utf8.GetString(content);
        if (text.Length > 0 && text[0] == '\uFEFF')
        {
            text = text[1..];
        }

        text = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        entries.Add(new BundleEntry(archiveName, _utf8.GetBytes(text)));
    }

    private static byte[] WriteCheckpoint(RunCheckpoint checkpoint) => WriteJson(writer =>
    {
        writer.WriteString("schema", "devforge-support-checkpoint-v1");
        writer.WriteString("runId", checkpoint.Run.Id);
        writer.WriteString("recipeId", checkpoint.Run.RecipeId);
        writer.WriteString("status", checkpoint.Run.Status.ToString());
        writer.WriteString("planHash", checkpoint.PlanHash);
        writer.WriteString("blueprintId", checkpoint.Blueprint.Id);
        writer.WriteString("blueprintVersion", checkpoint.Blueprint.Version);
        writer.WriteString("finalization", checkpoint.FinalizationState.ToString());
        writer.WriteString("reportPersistence", checkpoint.ReportState.ToString());
        writer.WriteStartArray("evidence");
        foreach (var evidence in checkpoint.Evidence.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("id", evidence.Id);
            writer.WriteString("status", evidence.Status.ToString());
            writer.WriteString("outputDigest", evidence.OutputDigest);
            writer.WriteString("errorCode", evidence.ErrorCode);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    });

    private static byte[] WriteRecipeSummary(RunCheckpoint checkpoint) => WriteJson(writer =>
    {
        writer.WriteString("schema", "devforge-support-recipe-summary-v1");
        writer.WriteString("recipeId", checkpoint.Run.RecipeId);
    });

    private static byte[] WriteBlueprintIdentity(RunCheckpoint checkpoint) => WriteJson(writer =>
    {
        writer.WriteString("schema", "devforge-support-blueprint-identity-v1");
        writer.WriteString("id", checkpoint.Blueprint.Id);
        writer.WriteString("version", checkpoint.Blueprint.Version);
        writer.WriteString("source", checkpoint.BlueprintFingerprint.SourceId);
        writer.WriteString("trust", checkpoint.BlueprintFingerprint.Trust.ToString());
        writer.WriteString("aggregateChecksum", checkpoint.BlueprintFingerprint.AggregateChecksum);
    });

    private static byte[] WriteErrorCatalog() => WriteJson(writer =>
    {
        writer.WriteString("schema", "devforge-support-error-catalog-v1");
        writer.WriteStartArray("errors");
        WriteError(writer, "DF-SUPPORT-001", "Support export request failed.");
        WriteError(writer, "DF-SUPPORT-002", "Support archive validation failed.");
        WriteError(writer, "DF-SUPPORT-003", "Owned support cleanup failed.");
        writer.WriteEndArray();
    });

    private static void WriteError(Utf8JsonWriter writer, string code, string summary)
    {
        writer.WriteStartObject();
        writer.WriteString("code", code);
        writer.WriteString("summary", summary);
        writer.WriteEndObject();
    }

    private static byte[] WritePlanSummary(RunCheckpoint checkpoint) => WriteJson(writer =>
    {
        writer.WriteString("schema", "devforge-support-plan-summary-v1");
        writer.WriteString("planHash", checkpoint.PlanHash);
        writer.WriteStartArray("steps");
        foreach (var step in checkpoint.Plan.Steps)
        {
            writer.WriteStartObject();
            writer.WriteString("id", step.Id);
            writer.WriteString("handler", step.Handler);
            writer.WriteNumber("timeoutMs", checked((long)step.Timeout.TotalMilliseconds));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    });

    private static byte[] WriteEnvironment(EnvironmentSnapshot snapshot) => WriteJson(writer =>
    {
        writer.WriteString("schema", "devforge-support-tool-status-v1");
        writer.WriteStartArray("tools");
        foreach (var tool in snapshot.Tools.OrderBy(item => item.Name, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("name", tool.Name);
            writer.WriteString("version", tool.Version);
            writer.WriteBoolean("available", tool.IsAvailable);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    });

    private static byte[] WriteInventory(string runId, IEnumerable<BundleEntry> entries) =>
        WriteJson(writer =>
        {
            writer.WriteString("schema", "devforge-support-bundle-inventory-v1");
            writer.WriteString("runId", runId);
            writer.WriteStartArray("entries");
            foreach (var entry in entries.OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("name", entry.Name);
                writer.WriteNumber("length", entry.Content.Length);
                writer.WriteString(
                    "sha256",
                    Convert.ToHexString(SHA256.HashData(entry.Content)).ToLowerInvariant());
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        });

    private static byte[] WriteJson(Action<Utf8JsonWriter> writeBody)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writeBody(writer);
            writer.WriteEndObject();
        }

        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    private static void ValidateEntries(IReadOnlyList<BundleEntry> entries)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "blueprint/manifest-checksum.json",
            "catalog/error-catalog.json",
            "environment/tool-status.json",
            "inventory.json",
            "logs/run.jsonl",
            "run/checkpoint.json",
            "run/generation-report.json",
            "run/generation-report.md",
            "run/plan-summary.json",
            "run/recipe-summary.json",
        };
        var names = new HashSet<string>(StringComparer.Ordinal);
        long total = 0;
        foreach (var entry in entries)
        {
            if (!allowed.Contains(entry.Name)
                || !names.Add(entry.Name)
                || entry.Name.Contains("..", StringComparison.Ordinal)
                || entry.Name.Contains(':', StringComparison.Ordinal)
                || entry.Name.Contains('\\', StringComparison.Ordinal)
                || entry.Content.Length > MaximumEntryBytes)
            {
                throw new InvalidDataException("A support entry is not allowed.");
            }

            total = checked(total + entry.Content.Length);
            var text = _utf8.GetString(entry.Content);
            if (RedactedText.IsSecretShapedValue(text)
                || RedactedText.IsSourceShapedContent(text))
            {
                throw new InvalidDataException("A support entry failed privacy validation.");
            }
        }

        if (total > MaximumBundleBytes)
        {
            throw new InvalidDataException("The support bundle exceeds its size limit.");
        }
    }

    private static byte[] WriteArchive(IEnumerable<BundleEntry> entries)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true, _utf8))
        {
            foreach (var item in entries)
            {
                var entry = archive.CreateEntry(item.Name, CompressionLevel.NoCompression);
                entry.LastWriteTime = _zipTimestamp;
                using var stream = entry.Open();
                stream.Write(item.Content);
            }
        }

        return output.ToArray();
    }

    private async Task<SupportBundleReceipt?> PrepareStagingAsync(
        IWorkspaceFileSystem workspace,
        IAtomicFileWorkspaceFileSystem atomic,
        IAtomicWorkspaceFileSystem atomicDirectories,
        string bundleId,
        WorkspaceRelativePath bundlePath,
        string sha256,
        byte[] archive,
        CancellationToken cancellationToken)
    {
        var stagingParent = Relative("support-bundles\\.staging");
        var stagingDirectory = Relative($"support-bundles\\.staging\\{bundleId}");
        var stagingMarker = Relative(
            $"support-bundles\\.staging\\{bundleId}\\ownership.owner.json");
        var stagingArchive = Relative(
            $"support-bundles\\.staging\\{bundleId}\\bundle.zip");
        await workspace.CreateDirectoryAsync(stagingParent, cancellationToken).ConfigureAwait(false);
        var created = await atomicDirectories.TryCreateDirectoryAsync(
            stagingDirectory,
            cancellationToken).ConfigureAwait(false);
        SupportBundleReceipt? receipt;
        if (created)
        {
            receipt = SupportBundleReceipt.Create(
                bundleId,
                bundlePath,
                sha256,
                archive.LongLength,
                _timeProvider.GetUtcNow()).Value;
            await atomic.WriteFileAtomicallyAsync(
                stagingMarker,
                WriteMarker(receipt),
                overwrite: false,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            if (!await workspace.FileExistsAsync(stagingMarker, cancellationToken)
                    .ConfigureAwait(false))
            {
                return null;
            }

            var marker = await DiagnosticLogOwnership.ReadBoundedAsync(
                workspace,
                stagingMarker,
                2048,
                cancellationToken).ConfigureAwait(false);
            receipt = ReadMarker(marker, bundleId, bundlePath, sha256, archive.LongLength);
            if (receipt is null)
            {
                return null;
            }
        }

        if (await workspace.FileExistsAsync(stagingArchive, cancellationToken).ConfigureAwait(false))
        {
            var staged = await DiagnosticLogOwnership.ReadBoundedAsync(
                workspace,
                stagingArchive,
                MaximumBundleBytes,
                cancellationToken).ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(
                    SHA256.HashData(staged),
                    SHA256.HashData(archive)))
            {
                return null;
            }
        }
        else
        {
            await atomic.WriteFileAtomicallyAsync(
                stagingArchive,
                archive,
                overwrite: false,
                cancellationToken).ConfigureAwait(false);
        }

        return receipt;
    }

    private static async Task<SupportBundleReceipt?> ReadOrCreateMarkerAsync(
        IWorkspaceFileSystem workspace,
        IAtomicFileWorkspaceFileSystem atomic,
        WorkspaceRelativePath markerPath,
        SupportBundleReceipt expected,
        CancellationToken cancellationToken)
    {
        if (await workspace.FileExistsAsync(markerPath, cancellationToken).ConfigureAwait(false))
        {
            var content = await DiagnosticLogOwnership.ReadBoundedAsync(
                workspace,
                markerPath,
                2048,
                cancellationToken).ConfigureAwait(false);
            return ReadMarker(
                content,
                expected.BundleId,
                expected.RelativePath,
                expected.Sha256,
                expected.Length);
        }

        if (await workspace.FileExistsAsync(expected.RelativePath, cancellationToken)
                .ConfigureAwait(false))
        {
            return null;
        }

        await atomic.WriteFileAtomicallyAsync(
            markerPath,
            WriteMarker(expected),
            overwrite: false,
            cancellationToken).ConfigureAwait(false);
        return expected;
    }

    private static async Task CleanupStagingAsync(
        IWorkspaceFileSystem workspace,
        SupportBundleReceipt receipt,
        CancellationToken cancellationToken)
    {
        var stagingDirectory = Relative($"support-bundles\\.staging\\{receipt.BundleId}");
        var stagingMarker = Relative(
            $"support-bundles\\.staging\\{receipt.BundleId}\\ownership.owner.json");
        if (!await workspace.DirectoryExistsAsync(stagingDirectory, cancellationToken)
                .ConfigureAwait(false))
        {
            return;
        }

        if (!await workspace.FileExistsAsync(stagingMarker, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException("The support staging directory is unowned.");
        }

        var marker = await DiagnosticLogOwnership.ReadBoundedAsync(
            workspace,
            stagingMarker,
            2048,
            cancellationToken).ConfigureAwait(false);
        if (ReadMarker(
                marker,
                receipt.BundleId,
                receipt.RelativePath,
                receipt.Sha256,
                receipt.Length) != receipt)
        {
            throw new InvalidDataException("The support staging marker is invalid.");
        }

        await workspace.DeleteDirectoryAsync(
            stagingDirectory,
            DirectoryCleanupIntent.RecursiveRunOwned,
            cancellationToken).ConfigureAwait(false);
    }

    private static byte[] WriteMarker(SupportBundleReceipt receipt) => WriteJson(writer =>
    {
        writer.WriteString("schema", "devforge-support-bundle-owner-v1");
        writer.WriteString("bundleId", receipt.BundleId);
        writer.WriteString("path", receipt.RelativePath.Value);
        writer.WriteString("sha256", receipt.Sha256);
        writer.WriteNumber("length", receipt.Length);
        writer.WriteString(
            "createdAtUtc",
            receipt.CreatedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
    });

    private static SupportBundleReceipt? ReadMarker(
        byte[] content,
        string bundleId,
        WorkspaceRelativePath bundlePath,
        string sha256,
        long length)
    {
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        string[] expectedProperties =
        [
            "schema",
            "bundleId",
            "path",
            "sha256",
            "length",
            "createdAtUtc",
        ];
        if (root.ValueKind != JsonValueKind.Object
            || !root.EnumerateObject().Select(property => property.Name)
                .SequenceEqual(expectedProperties, StringComparer.Ordinal)
            || root.GetProperty("schema").GetString() != "devforge-support-bundle-owner-v1"
            || root.GetProperty("bundleId").GetString() != bundleId
            || root.GetProperty("path").GetString() != bundlePath.Value
            || root.GetProperty("sha256").GetString() != sha256
            || root.GetProperty("length").GetInt64() != length
            || !DateTimeOffset.TryParseExact(
                root.GetProperty("createdAtUtc").GetString(),
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var createdAtUtc))
        {
            return null;
        }

        var receipt = SupportBundleReceipt.Create(
            bundleId,
            bundlePath,
            sha256,
            length,
            createdAtUtc);
        return receipt.IsValid ? receipt.Value : null;
    }

    private static async Task<IWorkspaceExclusiveLease?> AcquireLeaseAsync(
        IExclusiveLeaseWorkspaceFileSystem leases,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lease = await leases.TryAcquireExclusiveLeaseAsync(_leasePath, cancellationToken)
                .ConfigureAwait(false);
            if (lease is not null)
            {
                return lease;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private static ExecutionOperationResult<SupportBundleReceipt> Failure() =>
        ExecutionOperationResult.Failure<SupportBundleReceipt>(
            DevForgeError.Create(
                "DF-SUPPORT-002",
                "The support bundle could not be written safely.",
                RedactedText.FromTrustedRedaction(
                    "An owned support artifact failed privacy or integrity validation.").Value,
                "support-export",
                null,
                isRetryable: true,
                ["Retry the export or review local diagnostics."],
                []).Value);

    private static ExecutionOperationResult<SupportBundleCleanupReceipt> CleanupFailure() =>
        ExecutionOperationResult.Failure<SupportBundleCleanupReceipt>(
            DevForgeError.Create(
                "DF-SUPPORT-003",
                "The support bundle could not be cleaned safely.",
                RedactedText.FromTrustedRedaction(
                    "The support artifact ownership or integrity proof is unavailable.").Value,
                "support-cleanup",
                null,
                isRetryable: true,
                ["Retry cleanup after verifying the owned support artifact."],
                []).Value);

    private static WorkspaceRelativePath Relative(string value) =>
        WorkspaceRelativePath.Create(value).Value;

    private sealed record BundleEntry(string Name, byte[] Content);
}
