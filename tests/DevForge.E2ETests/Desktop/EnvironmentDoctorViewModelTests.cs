using System.Collections.Immutable;
using DevForge.Application.Contracts.Persistence;
using DevForge.Desktop.EnvironmentDoctor;
using DevForge.Desktop.Notifications;

namespace DevForge.E2ETests.Desktop;

public sealed class EnvironmentDoctorViewModelTests
{
    [Fact]
    public async Task StartupLoadUsesCachePolicyAndPublishesFailureWarning()
    {
        var service = new FakeDoctorService
        {
            Snapshot = CreateSnapshot(isStale: true, scanFailed: true),
        };
        var notifications = new NotificationService();
        var sut = new EnvironmentDoctorViewModel(service, new FakeClipboardService(), notifications);

        await sut.LoadAsync(CancellationToken.None);

        Assert.Equal([false], service.ForceRefreshCalls);
        Assert.True(sut.IsStale);
        Assert.True(sut.ScanFailed);
        Assert.Single(notifications.Items);
        Assert.Equal("git", sut.Tools[0].Id);
    }

    [Fact]
    public async Task RescanForcesFreshInspection()
    {
        var service = new FakeDoctorService { Snapshot = CreateSnapshot(false, false) };
        var sut = new EnvironmentDoctorViewModel(
            service,
            new FakeClipboardService(),
            new NotificationService());

        await sut.RescanAsync(CancellationToken.None);

        Assert.Equal([true], service.ForceRefreshCalls);
    }

    [Fact]
    public async Task CopyDiagnosticsContainsOnlyBoundedDisplayEvidence()
    {
        var clipboard = new FakeClipboardService();
        var sut = new EnvironmentDoctorViewModel(
            new FakeDoctorService { Snapshot = CreateSnapshot(false, false) },
            clipboard,
            new NotificationService());
        await sut.LoadAsync(CancellationToken.None);

        sut.CopyDiagnosticsCommand.Execute(null);

        Assert.Contains("git | Compatible | 2.51.0", clipboard.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\", clipboard.Text, StringComparison.Ordinal);
        Assert.InRange(clipboard.Text!.Length, 1, 4_096);
    }

    private static EnvironmentHealthSnapshot CreateSnapshot(bool isStale, bool scanFailed)
    {
        var scannedAt = new DateTimeOffset(2026, 8, 12, 8, 0, 0, TimeSpan.Zero);
        return new EnvironmentHealthSnapshot(
            [new EnvironmentHealthItem("git", "2.51.0", EnvironmentToolStatus.Compatible, scannedAt)],
            scannedAt,
            EnvironmentSnapshotSource.Cache,
            isStale,
            scanFailed);
    }

    private sealed class FakeDoctorService : IEnvironmentDoctorService
    {
        public EnvironmentHealthSnapshot Snapshot { get; set; } = CreateSnapshot(false, false);

        public List<bool> ForceRefreshCalls { get; } = [];

        public Task<EnvironmentHealthSnapshot> LoadAsync(
            bool forceRefresh,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ForceRefreshCalls.Add(forceRefresh);
            return Task.FromResult(Snapshot);
        }
    }

    private sealed class FakeClipboardService : IClipboardService
    {
        public string? Text { get; private set; }

        public void SetText(string text) => Text = text;
    }
}
