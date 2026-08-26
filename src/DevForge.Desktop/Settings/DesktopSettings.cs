using DevForge.Desktop.Theming;

namespace DevForge.Desktop.Settings;

public sealed record DesktopSettings(
    string DefaultProjectRoot,
    string DefaultIdeId,
    string DefaultTeamProfileId,
    string CultureName,
    ThemePreference Theme,
    bool OnboardingCompleted,
    int DiagnosticRetentionDays = 30,
    long DiagnosticRetentionMaxBytes = 256L * 1024 * 1024);

public sealed record DesktopSettingsDraft(
    string? DefaultProjectRoot,
    string? DefaultIdeId,
    string? DefaultTeamProfileId,
    string? CultureName,
    ThemePreference Theme,
    bool OnboardingCompleted,
    int DiagnosticRetentionDays = 30,
    long DiagnosticRetentionMaxBytes = 256L * 1024 * 1024);
