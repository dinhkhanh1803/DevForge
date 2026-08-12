using System.Collections.Immutable;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevForge.Desktop.Dashboard;
using DevForge.Desktop.EnvironmentDoctor;
using DevForge.Desktop.Navigation;
using DevForge.Desktop.Notifications;
using DevForge.Desktop.Settings;

namespace DevForge.Desktop.Shell;

public sealed partial class ShellViewModel : ObservableObject, IDisposable
{
    private readonly NavigationService _navigation;
    private readonly ImmutableArray<RouteDescriptor> _routes = NavigationService.Descriptors;
    private readonly DashboardViewModel _dashboard;
    private readonly SettingsViewModel _settings;
    private readonly EnvironmentDoctorViewModel _environmentDoctor;

    [ObservableProperty]
    private bool _isSafeMode;

    [ObservableProperty]
    private string? _safeModeMessage;

    public ShellViewModel(
        NavigationService navigation,
        NotificationService notifications,
        DashboardViewModel dashboard,
        SettingsViewModel settings,
        EnvironmentDoctorViewModel environmentDoctor)
    {
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        Notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _dashboard = dashboard ?? throw new ArgumentNullException(nameof(dashboard));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _environmentDoctor = environmentDoctor ?? throw new ArgumentNullException(nameof(environmentDoctor));
        NavigateCommand = new RelayCommand<DesktopRoute>(Navigate);
        _navigation.PropertyChanged += OnNavigationPropertyChanged;
    }

    public ImmutableArray<RouteDescriptor> Routes => _routes;

    public DesktopRoute CurrentRoute => _navigation.CurrentRoute;

    public object CurrentPage => CurrentRoute switch
    {
        DesktopRoute.Dashboard => _dashboard,
        DesktopRoute.Settings => _settings,
        DesktopRoute.EnvironmentDoctor => _environmentDoctor,
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
        _navigation.PropertyChanged -= OnNavigationPropertyChanged;
    }

    private void Navigate(DesktopRoute route)
    {
        _navigation.TryNavigate(route);
    }

    private void OnNavigationPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(NavigationService.CurrentRoute))
        {
            OnPropertyChanged(nameof(CurrentRoute));
            OnPropertyChanged(nameof(CurrentPage));
        }
    }
}
