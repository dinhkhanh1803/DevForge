using System.Collections.Immutable;
using DevForge.Application.Contracts;
using DevForge.Application.Contracts.Persistence;

namespace DevForge.Desktop.EnvironmentDoctor;

public interface IEnvironmentDoctorService
{
    Task<EnvironmentHealthSnapshot> LoadAsync(
        bool forceRefresh,
        CancellationToken cancellationToken);

    Task<EnvironmentHealthSnapshot> LoadCachedAsync(CancellationToken cancellationToken);
}

public sealed class EnvironmentDoctorService : IEnvironmentDoctorService
{
    internal static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(15);

    private readonly IEnvironmentDoctor _doctor;
    private readonly IEnvironmentToolStore _store;
    private readonly TimeProvider _timeProvider;
    private int _scanActive;

    public EnvironmentDoctorService(
        IEnvironmentDoctor doctor,
        IEnvironmentToolStore store,
        TimeProvider timeProvider)
    {
        _doctor = doctor ?? throw new ArgumentNullException(nameof(doctor));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<EnvironmentHealthSnapshot> LoadAsync(
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cached = await _store.ListAsync(cancellationToken).ConfigureAwait(false);
        var now = _timeProvider.GetUtcNow();
        if (!forceRefresh && IsFresh(cached, now))
        {
            return FromCache(cached, isStale: false, scanFailed: false);
        }

        if (Interlocked.CompareExchange(ref _scanActive, 1, 0) != 0)
        {
            return FromCache(cached, isStale: true, scanFailed: false);
        }

        try
        {
            return await RefreshAsync(cached, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _scanActive, 0);
        }
    }

    private async Task<EnvironmentHealthSnapshot> RefreshAsync(
        ImmutableArray<EnvironmentToolRecord> cached,
        CancellationToken cancellationToken)
    {
        DevForge.Domain.Environment.EnvironmentSnapshot inspected;
        try
        {
            inspected = await _doctor.InspectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return FromCache(cached, isStale: true, scanFailed: true);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var records = inspected.Tools
            .Select(tool => CreateRecord(tool.Name, tool.Version, tool.IsAvailable, inspected.CapturedAt))
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToImmutableArray();

        foreach (var record in records)
        {
            await _store.UpsertAsync(record, cancellationToken).ConfigureAwait(false);
        }

        return new EnvironmentHealthSnapshot(
            [.. records.Select(ToPresentation)],
            inspected.CapturedAt,
            EnvironmentSnapshotSource.Fresh,
            IsStale: false,
            ScanFailed: false);
    }

    public async Task<EnvironmentHealthSnapshot> LoadCachedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cached = await _store.ListAsync(cancellationToken).ConfigureAwait(false);
        return FromCache(
            cached,
            isStale: !IsFresh(cached, _timeProvider.GetUtcNow()),
            scanFailed: false);
    }

    private static bool IsFresh(ImmutableArray<EnvironmentToolRecord> records, DateTimeOffset now)
    {
        return !records.IsEmpty && records.All(record => record.ExpiresAt > now);
    }

    private static EnvironmentHealthSnapshot FromCache(
        ImmutableArray<EnvironmentToolRecord> records,
        bool isStale,
        bool scanFailed)
    {
        var ordered = records.OrderBy(item => item.Id, StringComparer.Ordinal).ToImmutableArray();
        return new EnvironmentHealthSnapshot(
            [.. ordered.Select(ToPresentation)],
            ordered.IsEmpty ? null : ordered.Max(item => item.ScannedAt),
            EnvironmentSnapshotSource.Cache,
            isStale,
            scanFailed);
    }

    private static EnvironmentToolRecord CreateRecord(
        string name,
        string? version,
        bool isAvailable,
        DateTimeOffset scannedAt)
    {
        var result = EnvironmentToolRecord.Create(
            name,
            null,
            version,
            isAvailable ? EnvironmentToolStatus.Installed : EnvironmentToolStatus.Missing,
            scannedAt,
            scannedAt.Add(CacheLifetime));
        return result.IsValid
            ? result.Value
            : throw new InvalidOperationException("The environment doctor returned an invalid tool identity.");
    }

    private static EnvironmentHealthItem ToPresentation(EnvironmentToolRecord record)
    {
        return new EnvironmentHealthItem(record.Id, record.Version, record.Status, record.ScannedAt);
    }
}
