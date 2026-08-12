using System.Collections.Immutable;
using DevForge.Application.Contracts;
using DevForge.Application.Contracts.Persistence;
using DevForge.Desktop.EnvironmentDoctor;
using DevForge.Domain.Environment;
using DevForge.Domain.Privacy;

namespace DevForge.E2ETests.Desktop;

public sealed class EnvironmentDoctorServiceTests
{
    [Theory]
    [InlineData(14, false)]
    [InlineData(15, true)]
    public async Task StartupScansOnlyWhenCacheIsAtLeastFifteenMinutesOld(
        int ageMinutes,
        bool scans)
    {
        var now = new DateTimeOffset(2026, 8, 12, 8, 0, 0, TimeSpan.Zero);
        var store = new FakeEnvironmentToolStore(
            CreateRecord("git", EnvironmentToolStatus.Compatible, now.AddMinutes(-ageMinutes)));
        var doctor = new FakeEnvironmentDoctor(CreateSnapshot(now, new EnvironmentTool("git", "2.51.0", true)));
        var sut = new EnvironmentDoctorService(doctor, store, new FixedTimeProvider(now));

        var result = await sut.LoadAsync(forceRefresh: false, CancellationToken.None);

        Assert.Equal(scans ? 1 : 0, doctor.Calls);
        Assert.Equal(scans ? EnvironmentSnapshotSource.Fresh : EnvironmentSnapshotSource.Cache, result.Source);
    }

    [Fact]
    public async Task ExplicitRescanAlwaysScansAndPersistsFifteenMinuteExpiry()
    {
        var now = new DateTimeOffset(2026, 8, 12, 8, 0, 0, TimeSpan.Zero);
        var store = new FakeEnvironmentToolStore(
            CreateRecord("git", EnvironmentToolStatus.Compatible, now));
        var doctor = new FakeEnvironmentDoctor(CreateSnapshot(
            now,
            new EnvironmentTool("git", "2.51.0", true),
            new EnvironmentTool("gh", null, false)));
        var sut = new EnvironmentDoctorService(doctor, store, new FixedTimeProvider(now));

        var result = await sut.LoadAsync(forceRefresh: true, CancellationToken.None);

        Assert.Equal(1, doctor.Calls);
        Assert.Equal(["gh", "git"], result.Tools.Select(item => item.Id));
        Assert.Equal(EnvironmentToolStatus.Missing, result.Tools[0].Status);
        Assert.All(store.Writes, item => Assert.Equal(now.AddMinutes(15), item.ExpiresAt));
        Assert.DoesNotContain(
            typeof(EnvironmentHealthItem).GetProperties(),
            property => property.Name.Contains("Path", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task FailedScanPreservesStaleCacheWithoutExposingPath()
    {
        var now = new DateTimeOffset(2026, 8, 12, 8, 0, 0, TimeSpan.Zero);
        var record = EnvironmentToolRecord.Create(
            "git",
            @"C:\Tools\git.exe",
            "2.50.0",
            EnvironmentToolStatus.Outdated,
            now.AddHours(-1),
            now.AddMinutes(-45)).Value;
        var store = new FakeEnvironmentToolStore(record);
        var sut = new EnvironmentDoctorService(
            new FakeEnvironmentDoctor(new InvalidOperationException("raw failure")),
            store,
            new FixedTimeProvider(now));

        var result = await sut.LoadAsync(forceRefresh: false, CancellationToken.None);

        Assert.True(result.ScanFailed);
        Assert.True(result.IsStale);
        Assert.Equal(EnvironmentToolStatus.Outdated, result.Tools[0].Status);
        Assert.DoesNotContain(
            typeof(EnvironmentHealthItem).GetProperties(),
            property => property.Name.Contains("Path", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(store.Writes);
    }

    private static EnvironmentToolRecord CreateRecord(
        string id,
        EnvironmentToolStatus status,
        DateTimeOffset scannedAt)
    {
        return EnvironmentToolRecord.Create(
            id,
            null,
            "1.0.0",
            status,
            scannedAt,
            scannedAt.AddMinutes(15)).Value;
    }

    private static EnvironmentSnapshot CreateSnapshot(
        DateTimeOffset capturedAt,
        params EnvironmentTool[] tools)
    {
        return EnvironmentSnapshot.Create(
            capturedAt,
            tools,
            [KeyValuePair.Create("Platform", RedactedText.FromTrustedRedaction("Windows").Value)]).Value;
    }

    private sealed class FakeEnvironmentDoctor : IEnvironmentDoctor
    {
        private readonly EnvironmentSnapshot? _snapshot;
        private readonly Exception? _failure;

        public FakeEnvironmentDoctor(EnvironmentSnapshot snapshot) => _snapshot = snapshot;

        public FakeEnvironmentDoctor(Exception failure) => _failure = failure;

        public int Calls { get; private set; }

        public Task<EnvironmentSnapshot> InspectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return _failure is null
                ? Task.FromResult(_snapshot!)
                : Task.FromException<EnvironmentSnapshot>(_failure);
        }
    }

    private sealed class FakeEnvironmentToolStore(params EnvironmentToolRecord[] records)
        : IEnvironmentToolStore
    {
        private readonly Dictionary<string, EnvironmentToolRecord> _records =
            records.ToDictionary(item => item.Id, StringComparer.Ordinal);

        public List<EnvironmentToolRecord> Writes { get; } = [];

        public Task<EnvironmentToolRecord?> GetAsync(string id, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _records.TryGetValue(id, out var record);
            return Task.FromResult(record);
        }

        public Task<ImmutableArray<EnvironmentToolRecord>> ListAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_records.Values.ToImmutableArray());
        }

        public Task UpsertAsync(EnvironmentToolRecord tool, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _records[tool.Id] = tool;
            Writes.Add(tool);
            return Task.CompletedTask;
        }

        public Task<bool> RemoveAsync(string id, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_records.Remove(id));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
