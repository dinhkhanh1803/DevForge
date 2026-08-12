using System.Collections.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DevForge.Desktop.Navigation;

public sealed partial class NavigationService : ObservableObject
{
    private static readonly ImmutableDictionary<DesktopRoute, RouteDescriptor> _descriptorMap =
        CreateDescriptors().ToImmutableDictionary(item => item.Route);

    [ObservableProperty]
    private DesktopRoute _currentRoute = DesktopRoute.Dashboard;

    public static ImmutableArray<RouteDescriptor> Descriptors { get; } = CreateDescriptors();

    public bool TryNavigate(DesktopRoute route)
    {
        if (!_descriptorMap.TryGetValue(route, out var descriptor) || !descriptor.IsEnabled)
        {
            return false;
        }

        CurrentRoute = route;
        return true;
    }

    private static ImmutableArray<RouteDescriptor> CreateDescriptors()
    {
        const string future = "Available in M7";
        return
        [
            new(DesktopRoute.Dashboard, "Dashboard", "Home", true),
            new(DesktopRoute.CreateProject, "Create Project", "Add", false, future),
            new(DesktopRoute.Projects, "Projects", "Folder", false, future),
            new(DesktopRoute.BlueprintCatalog, "Blueprint Catalog", "Catalog", false, future),
            new(DesktopRoute.EnvironmentDoctor, "Environment Doctor", "Health", true),
            new(DesktopRoute.Settings, "Settings", "Settings", true),
        ];
    }
}
