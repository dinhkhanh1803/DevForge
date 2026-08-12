using DevForge.Desktop.Theming;

namespace DevForge.Desktop.Settings;

public sealed record DesktopSettings(
    string DefaultProjectRoot,
    string DefaultIdeId,
    string DefaultTeamProfileId,
    string CultureName,
    ThemePreference Theme,
    bool OnboardingCompleted);

public sealed record DesktopSettingsDraft(
    string? DefaultProjectRoot,
    string? DefaultIdeId,
    string? DefaultTeamProfileId,
    string? CultureName,
    ThemePreference Theme,
    bool OnboardingCompleted);
