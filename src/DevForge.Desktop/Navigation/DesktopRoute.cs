namespace DevForge.Desktop.Navigation;

public enum DesktopRoute
{
    Dashboard = 1,
    CreateProject = 2,
    RunHistory = 3,
    BlueprintCatalog = 4,
    EnvironmentDoctor = 5,
    Settings = 6,
}

public sealed record RouteDescriptor(
    DesktopRoute Route,
    string Label,
    string Glyph,
    bool IsEnabled,
    string? DisabledReason = null);
