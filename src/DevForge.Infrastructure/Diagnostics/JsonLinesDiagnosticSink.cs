using System.Globalization;
using DevForge.Application.Contracts;

namespace DevForge.Infrastructure.Diagnostics;

public sealed class JsonLinesDiagnosticSink : IDiagnosticSink, IDisposable
{
    internal const int MaximumLogFileBytes = 16 * 1024 * 1024;

    private static readonly WorkspaceRelativePath _logsDirectory = Relative("logs");
    private static readonly WorkspaceRelativePath _dailyDirectory = Relative("logs\\daily");
    private static readonly WorkspaceRelativePath _runsDirectory = Relative("logs\\runs");

    private readonly IFileSystem _fileSystem;
    private readonly WorkspaceRoot _localDataRoot;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public JsonLinesDiagnosticSink(IFileSystem fileSystem, WorkspaceRoot localDataRoot)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _localDataRoot = localDataRoot ?? throw new ArgumentNullException(nameof(localDataRoot));
    }

    public async Task WriteAsync(
        DiagnosticEvent diagnosticEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);
        cancellationToken.ThrowIfCancellationRequested();
        var line = DiagnosticEventNormalizer.Serialize(diagnosticEvent);

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var workspace = await _fileSystem.OpenWorkspaceAsync(
                _localDataRoot,
                cancellationToken).ConfigureAwait(false);
            if (workspace is not IAtomicFileWorkspaceFileSystem atomic
                || workspace is not IExclusiveLeaseWorkspaceFileSystem leases)
            {
                throw Failure();
            }

            await workspace.CreateDirectoryAsync(_logsDirectory, cancellationToken).ConfigureAwait(false);
            await workspace.CreateDirectoryAsync(_dailyDirectory, cancellationToken).ConfigureAwait(false);
            if (diagnosticEvent.RunId is not null)
            {
                await workspace.CreateDirectoryAsync(_runsDirectory, cancellationToken).ConfigureAwait(false);
            }

            await using var lease = await DiagnosticLogLease.AcquireAsync(
                leases,
                cancellationToken).ConfigureAwait(false);
            if (lease is null)
            {
                throw Failure();
            }

            var dailyDate = diagnosticEvent.TimestampUtc.ToString(
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture);
            var dailyPath = Relative($"logs\\daily\\{dailyDate}.jsonl");
            await DiagnosticLogOwnership.EnsureAsync(
                workspace,
                atomic,
                dailyPath,
                cancellationToken).ConfigureAwait(false);
            await AppendSnapshotAsync(
                workspace,
                atomic,
                dailyPath,
                line,
                cancellationToken).ConfigureAwait(false);

            if (diagnosticEvent.RunId is not null)
            {
                var runPath = Relative($"logs\\runs\\{diagnosticEvent.RunId}.jsonl");
                await DiagnosticLogOwnership.EnsureAsync(
                    workspace,
                    atomic,
                    runPath,
                    cancellationToken).ConfigureAwait(false);
                await AppendSnapshotAsync(
                    workspace,
                    atomic,
                    runPath,
                    line,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InfrastructureOperationException exception) when (exception.Code == "DF-DIAG-001")
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
        finally
        {
            _writeGate.Release();
        }
    }

    public void Dispose()
    {
        _writeGate.Dispose();
    }

    private static async Task AppendSnapshotAsync(
        IWorkspaceFileSystem workspace,
        IAtomicFileWorkspaceFileSystem atomic,
        WorkspaceRelativePath path,
        byte[] line,
        CancellationToken cancellationToken)
    {
        var existing = await ReadExistingAsync(workspace, path, cancellationToken).ConfigureAwait(false);
        var combinedLength = checked(existing.Length + line.Length);
        if (combinedLength > MaximumLogFileBytes)
        {
            throw Failure();
        }

        var combined = GC.AllocateUninitializedArray<byte>(combinedLength);
        existing.CopyTo(combined, 0);
        line.CopyTo(combined, existing.Length);
        await atomic.WriteFileAtomicallyAsync(
            path,
            combined,
            overwrite: true,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadExistingAsync(
        IWorkspaceFileSystem workspace,
        WorkspaceRelativePath path,
        CancellationToken cancellationToken)
    {
        if (!await workspace.FileExistsAsync(path, cancellationToken).ConfigureAwait(false))
        {
            return [];
        }

        await using var stream = await workspace.OpenReadAsync(path, cancellationToken)
            .ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (output.Length + read > MaximumLogFileBytes)
            {
                throw Failure();
            }

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private static WorkspaceRelativePath Relative(string value) =>
        WorkspaceRelativePath.Create(value).Value;

    private static InfrastructureOperationException Failure() =>
        new("DF-DIAG-001", "The diagnostic event could not be persisted safely.");
}
