using System.Collections.Immutable;
using System.Globalization;
using DevForge.Application.Contracts;

namespace DevForge.Infrastructure.Diagnostics;

public sealed class DiagnosticRetentionService : IDiagnosticRetentionService
{
    private static readonly WorkspaceRelativePath _logsDirectory = Relative("logs");
    private readonly IFileSystem _fileSystem;
    private readonly WorkspaceRoot _localDataRoot;

    public DiagnosticRetentionService(IFileSystem fileSystem, WorkspaceRoot localDataRoot)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _localDataRoot = localDataRoot ?? throw new ArgumentNullException(nameof(localDataRoot));
    }

    public async Task<DiagnosticRetentionResult> ApplyAsync(
        DiagnosticRetentionPolicy policy,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (nowUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("A UTC retention timestamp is required.", nameof(nowUtc));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Result(0, 0, 0, cancelled: true, DiagnosticRetentionReason.Cancelled);
        }

        var unownedCount = 0;
        try
        {
            var workspace = await _fileSystem.OpenWorkspaceAsync(
                _localDataRoot,
                cancellationToken).ConfigureAwait(false);
            if (!await workspace.DirectoryExistsAsync(_logsDirectory, cancellationToken)
                    .ConfigureAwait(false))
            {
                return DiagnosticRetentionResult.Empty;
            }

            if (workspace is not IWorkspaceFileMetadataFileSystem metadataWorkspace
                || workspace is not IExclusiveLeaseWorkspaceFileSystem leases)
            {
                throw Failure();
            }

            await using var lease = await DiagnosticLogLease.AcquireAsync(
                leases,
                cancellationToken).ConfigureAwait(false);
            if (lease is null)
            {
                return Result(
                    0,
                    0,
                    0,
                    cancelled: false,
                    DiagnosticRetentionReason.LeaseUnavailable);
            }

            var files = await workspace.EnumerateFilesAsync(
                _logsDirectory,
                recursive: true,
                cancellationToken).ConfigureAwait(false);
            var owned = new List<WorkspaceFileMetadata>();
            foreach (var path in files.Where(IsOwnedLog))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!await DiagnosticLogOwnership.IsVerifiedAsync(
                        workspace,
                        path,
                        cancellationToken).ConfigureAwait(false))
                {
                    unownedCount++;
                    continue;
                }

                var metadata = await metadataWorkspace.GetFileMetadataAsync(path, cancellationToken)
                    .ConfigureAwait(false);
                if (metadata is not null)
                {
                    owned.Add(metadata);
                }
            }

            var activeDate = nowUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var activeDailyPath = $"logs\\daily\\{activeDate}.jsonl";
            var deletionOrder = owned
                .Where(item => !item.Path.Value.Equals(activeDailyPath, StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.LastWriteUtc)
                .ThenBy(item => item.Path.Value, StringComparer.Ordinal)
                .ToArray();
            var cutoff = nowUtc.AddDays(-policy.MaxAgeDays);
            var totalBytes = owned.Sum(item => item.Length);
            var planned = new List<WorkspaceFileMetadata>();
            var plannedPaths = new HashSet<WorkspaceRelativePath>();
            var projectedBytes = totalBytes;
            foreach (var item in deletionOrder.Where(item => item.LastWriteUtc < cutoff))
            {
                planned.Add(item);
                plannedPaths.Add(item.Path);
                projectedBytes -= item.Length;
            }

            foreach (var item in deletionOrder)
            {
                if (projectedBytes <= policy.MaxTotalBytes)
                {
                    break;
                }

                if (plannedPaths.Contains(item.Path))
                {
                    continue;
                }

                planned.Add(item);
                plannedPaths.Add(item.Path);
                projectedBytes -= item.Length;
            }

            var deletedCount = 0;
            for (var index = 0; index < planned.Count; index++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return Result(
                        deletedCount,
                        planned.Count - deletedCount,
                        unownedCount,
                        cancelled: true,
                        Reasons(unownedCount, DiagnosticRetentionReason.Cancelled));
                }

                var item = planned[index];
                try
                {
                    await workspace.DeleteFileAsync(item.Path, CancellationToken.None)
                        .ConfigureAwait(false);
                    deletedCount++;
                    await workspace.DeleteFileAsync(
                        DiagnosticLogOwnership.MarkerPath(item.Path),
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is InfrastructureOperationException
                    or IOException
                    or UnauthorizedAccessException)
                {
                    return Result(
                        deletedCount,
                        planned.Count - deletedCount,
                        unownedCount,
                        cancelled: false,
                        Reasons(unownedCount, DiagnosticRetentionReason.DeleteFailed));
                }
            }

            return Result(
                deletedCount,
                0,
                unownedCount,
                cancelled: false,
                Reasons(unownedCount));
        }
        catch (OperationCanceledException)
        {
            return Result(
                0,
                0,
                unownedCount,
                cancelled: true,
                Reasons(unownedCount, DiagnosticRetentionReason.Cancelled));
        }
        catch (InfrastructureOperationException exception) when (exception.Code == "DF-DIAG-002")
        {
            throw;
        }
        catch (Exception exception) when (exception is InfrastructureOperationException
            or IOException
            or UnauthorizedAccessException
            or OverflowException)
        {
            throw Failure();
        }
    }

    private static ImmutableArray<DiagnosticRetentionReason> Reasons(
        int unownedCount,
        params DiagnosticRetentionReason[] additional)
    {
        var builder = ImmutableArray.CreateBuilder<DiagnosticRetentionReason>();
        if (unownedCount > 0)
        {
            builder.Add(DiagnosticRetentionReason.OwnershipUnverified);
        }

        builder.AddRange(additional);
        return builder.ToImmutable();
    }

    private static DiagnosticRetentionResult Result(
        int deletedCount,
        int deferredCount,
        int unownedCount,
        bool cancelled,
        params DiagnosticRetentionReason[] reasons) =>
        new(deletedCount, deferredCount, unownedCount, cancelled, [.. reasons]);

    private static DiagnosticRetentionResult Result(
        int deletedCount,
        int deferredCount,
        int unownedCount,
        bool cancelled,
        ImmutableArray<DiagnosticRetentionReason> reasons) =>
        new(deletedCount, deferredCount, unownedCount, cancelled, reasons);

    private static bool IsOwnedLog(WorkspaceRelativePath path)
    {
        var value = path.Value;
        if (!value.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        const string dailyPrefix = "logs\\daily\\";
        if (value.StartsWith(dailyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var fileName = value[dailyPrefix.Length..^".jsonl".Length];
            return DateOnly.TryParseExact(
                fileName,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _);
        }

        const string runsPrefix = "logs\\runs\\";
        if (!value.StartsWith(runsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var runId = value[runsPrefix.Length..^".jsonl".Length];
        return runId.Length is > 0 and <= 128
            && !runId.Contains('\\')
            && runId.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');
    }

    private static WorkspaceRelativePath Relative(string value) =>
        WorkspaceRelativePath.Create(value).Value;

    private static InfrastructureOperationException Failure() =>
        new("DF-DIAG-002", "Diagnostic retention could not be completed safely.");
}
