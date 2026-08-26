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

    public async Task ApplyAsync(
        DiagnosticRetentionPolicy policy,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);
        cancellationToken.ThrowIfCancellationRequested();
        if (nowUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("A UTC retention timestamp is required.", nameof(nowUtc));
        }

        try
        {
            var workspace = await _fileSystem.OpenWorkspaceAsync(
                _localDataRoot,
                cancellationToken).ConfigureAwait(false);
            if (!await workspace.DirectoryExistsAsync(_logsDirectory, cancellationToken)
                    .ConfigureAwait(false))
            {
                return;
            }

            if (workspace is not IWorkspaceFileMetadataFileSystem metadataWorkspace)
            {
                throw Failure();
            }

            var files = await workspace.EnumerateFilesAsync(
                _logsDirectory,
                recursive: true,
                cancellationToken).ConfigureAwait(false);
            var owned = new List<WorkspaceFileMetadata>();
            foreach (var path in files.Where(IsOwnedLog))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var metadata = await metadataWorkspace.GetFileMetadataAsync(path, cancellationToken)
                    .ConfigureAwait(false);
                if (metadata is not null)
                {
                    owned.Add(metadata);
                }
            }

            var activeDailyPath = $"logs\\daily\\{nowUtc:yyyy-MM-dd}.jsonl";
            var deletionOrder = owned
                .Where(item => !item.Path.Value.Equals(activeDailyPath, StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.LastWriteUtc)
                .ThenBy(item => item.Path.Value, StringComparer.Ordinal)
                .ToArray();
            var cutoff = nowUtc.AddDays(-policy.MaxAgeDays);
            var deleted = new HashSet<WorkspaceRelativePath>();
            var totalBytes = owned.Sum(item => item.Length);

            foreach (var item in deletionOrder.Where(item => item.LastWriteUtc < cutoff))
            {
                await workspace.DeleteFileAsync(item.Path, cancellationToken).ConfigureAwait(false);
                deleted.Add(item.Path);
                totalBytes -= item.Length;
            }

            foreach (var item in deletionOrder)
            {
                if (totalBytes <= policy.MaxTotalBytes)
                {
                    break;
                }

                if (deleted.Contains(item.Path))
                {
                    continue;
                }

                await workspace.DeleteFileAsync(item.Path, cancellationToken).ConfigureAwait(false);
                totalBytes -= item.Length;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
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
