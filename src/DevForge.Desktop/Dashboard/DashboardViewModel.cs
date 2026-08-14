using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevForge.Desktop.Navigation;
using DevForge.Desktop.Notifications;

namespace DevForge.Desktop.Dashboard;

public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly IDashboardService _dashboardService;
    private readonly NavigationService _navigation;
    private readonly NotificationService _notifications;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoRecentProjects))]
    [NotifyPropertyChangedFor(nameof(HasNoSavedPresets))]
    [NotifyPropertyChangedFor(nameof(HasNoActionNeededRuns))]
    private DashboardSnapshot? _snapshot;

    [ObservableProperty]
    private bool _isBusy;

    public DashboardViewModel(
        IDashboardService dashboardService,
        NavigationService navigation,
        NotificationService notifications)
    {
        _dashboardService = dashboardService ?? throw new ArgumentNullException(nameof(dashboardService));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        CreateProjectCommand = new RelayCommand(
            () => _navigation.TryNavigate(DesktopRoute.CreateProject));
        OpenEnvironmentDoctorCommand = new RelayCommand(
            () => _navigation.TryNavigate(DesktopRoute.EnvironmentDoctor));
    }

    public bool HasNoRecentProjects => Snapshot is null || Snapshot.HasNoRecentProjects;

    public bool HasNoSavedPresets => Snapshot is null || Snapshot.HasNoSavedPresets;

    public bool HasNoActionNeededRuns => Snapshot is null || Snapshot.HasNoActionNeededRuns;

    public IAsyncRelayCommand RefreshCommand { get; }

    public IRelayCommand CreateProjectCommand { get; }

    public IRelayCommand OpenEnvironmentDoctorCommand { get; }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        SetBusy(true);
        try
        {
            Snapshot = await _dashboardService.LoadAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            Snapshot = null;
            _notifications.TryPublish(NotificationSeverity.Error, "Dashboard could not be loaded.");
        }
        finally
        {
            SetBusy(false);
        }
    }

    internal void ApplySnapshot(DashboardSnapshot? snapshot)
    {
        Snapshot = snapshot;
    }

    private void SetBusy(bool value)
    {
        IsBusy = value;
        RefreshCommand.NotifyCanExecuteChanged();
    }
}
