using DevForge.Desktop.Dashboard;
using DevForge.Desktop.EnvironmentDoctor;
using DevForge.Desktop.Navigation;
using DevForge.Desktop.Notifications;

namespace DevForge.E2ETests.Desktop;

public sealed class DashboardViewModelTests
{
    [Fact]
    public async Task LoadPublishesSnapshotAndNavigatesToM7Create()
    {
        var snapshot = new DashboardSnapshot(
            [], [], [],
            new EnvironmentHealthSnapshot([], null, EnvironmentSnapshotSource.Cache, true, false));
        var navigation = new NavigationService();
        var sut = new DashboardViewModel(
            new FakeDashboardService(snapshot),
            navigation,
            new NotificationService());

        await sut.LoadAsync(CancellationToken.None);

        Assert.Same(snapshot, sut.Snapshot);
        Assert.True(sut.HasNoRecentProjects);
        Assert.True(sut.CreateProjectCommand.CanExecute(null));
        sut.CreateProjectCommand.Execute(null);
        Assert.Equal(DesktopRoute.CreateProject, navigation.CurrentRoute);
    }

    [Fact]
    public async Task LoadFailureKeepsEmptyStateAndShowsSafeMessage()
    {
        var notifications = new NotificationService();
        var sut = new DashboardViewModel(
            new FakeDashboardService(new InvalidOperationException("raw database failure")),
            new NavigationService(),
            notifications);

        await sut.LoadAsync(CancellationToken.None);

        Assert.Null(sut.Snapshot);
        Assert.Single(notifications.Items);
        Assert.DoesNotContain("database", notifications.Items[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeDashboardService : IDashboardService
    {
        private readonly DashboardSnapshot? _snapshot;
        private readonly Exception? _failure;

        public FakeDashboardService(DashboardSnapshot snapshot) => _snapshot = snapshot;

        public FakeDashboardService(Exception failure) => _failure = failure;

        public Task<DashboardSnapshot> LoadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _failure is null
                ? Task.FromResult(_snapshot!)
                : Task.FromException<DashboardSnapshot>(_failure);
        }
    }
}
