using System.Text.Json;
using DevForge.Application.Contracts;

namespace DevForge.Infrastructure.Diagnostics;

internal static class DiagnosticLogOwnership
{
    private const int MaximumMarkerBytes = 1024;

    internal static WorkspaceRelativePath MarkerPath(WorkspaceRelativePath logPath) =>
        WorkspaceRelativePath.Create(logPath.Value + ".owner.json").Value;

    internal static byte[] CreateMarkerBytes(WorkspaceRelativePath logPath)
    {
        ArgumentNullException.ThrowIfNull(logPath);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("kind", "devforge-diagnostic-log");
            writer.WriteString("path", logPath.Value);
            writer.WriteEndObject();
        }

        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    internal static async Task<bool> IsVerifiedAsync(
        IWorkspaceFileSystem workspace,
        WorkspaceRelativePath logPath,
        CancellationToken cancellationToken)
    {
        var markerPath = MarkerPath(logPath);
        if (!await workspace.FileExistsAsync(markerPath, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var actual = await ReadBoundedAsync(
            workspace,
            markerPath,
            MaximumMarkerBytes,
            cancellationToken).ConfigureAwait(false);
        return actual.AsSpan().SequenceEqual(CreateMarkerBytes(logPath));
    }

    internal static async Task EnsureAsync(
        IWorkspaceFileSystem workspace,
        IAtomicFileWorkspaceFileSystem atomic,
        WorkspaceRelativePath logPath,
        CancellationToken cancellationToken)
    {
        if (await IsVerifiedAsync(workspace, logPath, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        if (await workspace.FileExistsAsync(logPath, cancellationToken).ConfigureAwait(false)
            || await workspace.FileExistsAsync(MarkerPath(logPath), cancellationToken).ConfigureAwait(false))
        {
            throw Failure();
        }

        await atomic.WriteFileAtomicallyAsync(
            MarkerPath(logPath),
            CreateMarkerBytes(logPath),
            overwrite: false,
            cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<byte[]> ReadBoundedAsync(
        IWorkspaceFileSystem workspace,
        WorkspaceRelativePath path,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = await workspace.OpenReadAsync(path, cancellationToken)
            .ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[Math.Min(maximumBytes, 8192)];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return output.ToArray();
            }

            if (output.Length + read > maximumBytes)
            {
                throw Failure();
            }

            output.Write(buffer, 0, read);
        }
    }

    private static InfrastructureOperationException Failure() =>
        new("DF-DIAG-001", "Diagnostic ownership could not be verified safely.");
}

internal static class DiagnosticLogLease
{
    internal static readonly WorkspaceRelativePath LeasePath =
        WorkspaceRelativePath.Create("logs\\.write.lease").Value;

    private const int MaximumAttempts = 200;
    private static readonly TimeSpan _retryDelay = TimeSpan.FromMilliseconds(10);
    private static readonly SemaphoreSlim _processGate = new(1, 1);

    internal static async Task<IWorkspaceExclusiveLease?> AcquireAsync(
        IExclusiveLeaseWorkspaceFileSystem leases,
        CancellationToken cancellationToken)
    {
        await _processGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            for (var attempt = 0; attempt < MaximumAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var lease = await leases.TryAcquireExclusiveLeaseAsync(
                    LeasePath,
                    cancellationToken).ConfigureAwait(false);
                if (lease is not null)
                {
                    return new ProcessScopedLease(lease);
                }

                await Task.Delay(_retryDelay, cancellationToken).ConfigureAwait(false);
            }

            _processGate.Release();
            return null;
        }
        catch
        {
            _processGate.Release();
            throw;
        }
    }

    private sealed class ProcessScopedLease(IWorkspaceExclusiveLease inner) : IWorkspaceExclusiveLease
    {
        private IWorkspaceExclusiveLease? _inner = inner;

        public async ValueTask DisposeAsync()
        {
            var owned = Interlocked.Exchange(ref _inner, null);
            if (owned is null)
            {
                return;
            }

            try
            {
                await owned.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                _processGate.Release();
            }
        }
    }
}
