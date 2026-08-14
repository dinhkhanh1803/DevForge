using System.Collections.Immutable;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevForge.Desktop.BlueprintCatalog;
using DevForge.Desktop.CreateProject;
using DevForge.Desktop.Dashboard;
using DevForge.Desktop.EnvironmentDoctor;
using DevForge.Desktop.Execution;
using DevForge.Desktop.Navigation;
using DevForge.Desktop.Notifications;
using DevForge.Desktop.RunHistory;
using DevForge.Desktop.Settings;

namespace DevForge.Desktop.Shell;

public sealed partial class ShellViewModel : ObservableObject, IDisposable
{
    private readonly NavigationService _navigation;
    private readonly ImmutableArray<RouteDescriptor> _routes = NavigationService.Descriptors;
    private readonly DashboardViewModel _dashboard;
    private readonly SettingsViewModel _settings;
    private readonly EnvironmentDoctorViewModel _environmentDoctor;
    private readonly CreateProjectViewModel _createProject;
    private readonly RunHistoryViewModel _runHistory;
    private readonly BlueprintCatalogViewModel _blueprintCatalog;
    private CancellationTokenSource? _routeLoad;
    private readonly ExecutionCenterViewModel _executionCenter;
    private bool _showExecutionCenter;

    [ObservableProperty]
    private bool _isSafeMode;

    [ObservableProperty]
    private string? _safeModeMessage;

    public ShellViewModel(
        NavigationService navigation,
        NotificationService notifications,
        DashboardViewModel dashboard,
        SettingsViewModel settings,
        EnvironmentDoctorViewModel environmentDoctor,
        CreateProjectViewModel createProject,
        RunHistoryViewModel runHistory,
        BlueprintCatalogViewModel blueprintCatalog,
        ExecutionCenterViewModel executionCenter)
    {
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        Notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _dashboard = dashboard ?? throw new ArgumentNullException(nameof(dashboard));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _environmentDoctor = environmentDoctor ?? throw new ArgumentNullException(nameof(environmentDoctor));
        _createProject = createProject ?? throw new ArgumentNullException(nameof(createProject));
        _runHistory = runHistory ?? throw new ArgumentNullException(nameof(runHistory));
        _blueprintCatalog = blueprintCatalog ?? throw new ArgumentNullException(nameof(blueprintCatalog));
        _executionCenter = executionCenter ?? throw new ArgumentNullException(nameof(executionCenter));
        NavigateCommand = new RelayCommand<DesktopRoute>(Navigate);
        _navigation.PropertyChanged += OnNavigationPropertyChanged;
        _runHistory.ExecutionOpened += OnExecutionOpened;
    }

    public ImmutableArray<RouteDescriptor> Routes => _routes;

    public DesktopRoute CurrentRoute => _navigation.CurrentRoute;

    public object CurrentPage => _showExecutionCenter
        ? _runHistory.OpenedPage ?? _executionCenter
        : CurrentRoute switch
        {
            DesktopRoute.Dashboard => _dashboard,
            DesktopRoute.Settings => _settings,
            DesktopRoute.EnvironmentDoctor => _environmentDoctor,
            DesktopRoute.CreateProject => _createProject,
            DesktopRoute.RunHistory => _runHistory,
            DesktopRoute.BlueprintCatalog => _blueprintCatalog,
            _ => _dashboard,
        };

    public NotificationService Notifications { get; }

    public IRelayCommand<DesktopRoute> NavigateCommand { get; }

    public void SetSafeMode(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        IsSafeMode = true;
        SafeModeMessage = message;
    }

    public void Dispose()
    {
        _routeLoad?.Cancel();
        _routeLoad?.Dispose();
        _navigation.PropertyChanged -= OnNavigationPropertyChanged;
        _runHistory.ExecutionOpened -= OnExecutionOpened;
    }

    private void Navigate(DesktopRoute route)
    {
        _navigation.TryNavigate(route);
    }

    private void OnNavigationPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(NavigationService.CurrentRoute))
        {
            _showExecutionCenter = false;
            OnPropertyChanged(nameof(CurrentRoute));
            OnPropertyChanged(nameof(CurrentPage));
            BeginRouteLoad();
        }
    }

    private void OnExecutionOpened(object? sender, EventArgs args)
    {
        _showExecutionCenter = _runHistory.OpenedPage is not null;
        OnPropertyChanged(nameof(CurrentPage));
    }

    private void BeginRouteLoad()
    {
        _routeLoad?.Cancel();
        _routeLoad?.Dispose();
        _routeLoad = new CancellationTokenSource();
        _ = LoadRouteAsync(CurrentRoute, _routeLoad.Token);
    }

    private async Task LoadRouteAsync(DesktopRoute route, CancellationToken cancellationToken)
    {
        try
        {
            switch (route)
            {
                case DesktopRoute.CreateProject:
                    await _createProject.LoadAsync(cancellationToken).ConfigureAwait(true);
                    break;
                case DesktopRoute.BlueprintCatalog:
                    await _blueprintCatalog.LoadAsync(cancellationToken).ConfigureAwait(true);
                    break;
                case DesktopRoute.RunHistory:
                    await _runHistory.LoadAsync(cancellationToken).ConfigureAwait(true);
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            Notifications.TryPublish(
                NotificationSeverity.Error,
                "The selected page could not be loaded safely.");
        }
    }
}
