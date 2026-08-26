using System.IO;
using DevForge.Application.Contracts;
using DevForge.Application.Contracts.Persistence;
using DevForge.Desktop.Theming;
using DevForge.Domain.Privacy;
using DevForge.Domain.Validation;

namespace DevForge.Desktop.Settings;

public interface IDesktopSettingsService
{
    Task<DesktopSettings> LoadAsync(CancellationToken cancellationToken);

    Task<ValidationResult<DesktopSettings>> SaveAsync(
        DesktopSettingsDraft draft,
        CancellationToken cancellationToken);
}

internal static class DesktopSettingKeys
{
    public const string Theme = "ui.theme";
    public const string Culture = "ui.culture";
    public const string DefaultProjectRoot = "projects.default-root";
    public const string DefaultIdeId = "ide.default-id";
    public const string DefaultTeamProfileId = "team.default-profile-id";
    public const string OnboardingCompleted = "onboarding.completed";
    public const string DiagnosticRetentionDays = "diagnostics.retention-days";
    public const string DiagnosticRetentionMaxBytes = "diagnostics.retention-max-bytes";
}

public sealed class DesktopSettingsService : IDesktopSettingsService
{
    private static readonly HashSet<string> _supportedCultures =
        new(["en-US", "vi-VN"], StringComparer.Ordinal);
    private static readonly HashSet<string> _supportedIdeIds =
        new(["none", "vscode", "visual-studio", "rider", "unity"], StringComparer.Ordinal);

    private readonly IAppSettingsStore _store;
    private readonly TimeProvider _timeProvider;

    public DesktopSettingsService(IAppSettingsStore store, TimeProvider timeProvider)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<DesktopSettings> LoadAsync(CancellationToken cancellationToken)
    {
        var settings = await _store.ListAsync(cancellationToken).ConfigureAwait(false);
        var map = settings
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        var retention = ReadRetentionPolicy(map);

        return new DesktopSettings(
            ReadText(map, DesktopSettingKeys.DefaultProjectRoot, string.Empty),
            ReadIdeIdentifier(map),
            ReadIdentifier(map, DesktopSettingKeys.DefaultTeamProfileId),
            ReadCulture(map),
            ReadTheme(map),
            ReadBoolean(map, DesktopSettingKeys.OnboardingCompleted),
            retention.MaxAgeDays,
            retention.MaxTotalBytes);
    }

    public async Task<ValidationResult<DesktopSettings>> SaveAsync(
        DesktopSettingsDraft draft,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        cancellationToken.ThrowIfCancellationRequested();

        var validation = Validate(draft);
        if (!validation.IsValid)
        {
            return validation;
        }

        var settings = validation.Value;
        var timestamp = _timeProvider.GetUtcNow();
        var records = new[]
        {
            CreateText(DesktopSettingKeys.DefaultProjectRoot, settings.DefaultProjectRoot, timestamp),
            CreateText(DesktopSettingKeys.DefaultIdeId, settings.DefaultIdeId, timestamp),
            CreateText(DesktopSettingKeys.DefaultTeamProfileId, settings.DefaultTeamProfileId, timestamp),
            CreateText(DesktopSettingKeys.Culture, settings.CultureName, timestamp),
            CreateText(DesktopSettingKeys.Theme, settings.Theme.ToString(), timestamp),
            CreateBoolean(DesktopSettingKeys.OnboardingCompleted, settings.OnboardingCompleted, timestamp),
            CreateInteger(
                DesktopSettingKeys.DiagnosticRetentionDays,
                settings.DiagnosticRetentionDays,
                timestamp),
            CreateInteger(
                DesktopSettingKeys.DiagnosticRetentionMaxBytes,
                settings.DiagnosticRetentionMaxBytes,
                timestamp),
        };

        foreach (var record in records)
        {
            await _store.UpsertAsync(record, cancellationToken).ConfigureAwait(false);
        }

        return validation;
    }

    private static ValidationResult<DesktopSettings> Validate(DesktopSettingsDraft draft)
    {
        var issues = new List<ValidationIssue>();
        var rootResult = WorkspaceRoot.Create(draft.DefaultProjectRoot);
        if (!rootResult.IsValid)
        {
            issues.Add(new ValidationIssue(
                "desktop.settings.project-root.invalid",
                "Choose a canonical local Windows project root.",
                nameof(draft.DefaultProjectRoot)));
        }

        var ideId = NormalizeIdeIdentifier(draft.DefaultIdeId, issues);
        var teamId = NormalizeIdentifier(draft.DefaultTeamProfileId, nameof(draft.DefaultTeamProfileId), issues);
        var culture = draft.CultureName?.Trim();
        if (culture is null || !_supportedCultures.Contains(culture))
        {
            issues.Add(new ValidationIssue(
                "desktop.settings.culture.unsupported",
                "Choose a supported display language.",
                nameof(draft.CultureName)));
        }

        if (!Enum.IsDefined(draft.Theme))
        {
            issues.Add(new ValidationIssue(
                "desktop.settings.theme.invalid",
                "Choose System, Light, or Dark theme.",
                nameof(draft.Theme)));
        }

        var retention = DiagnosticRetentionPolicy.Create(
            draft.DiagnosticRetentionDays,
            draft.DiagnosticRetentionMaxBytes);
        if (!retention.IsValid)
        {
            issues.AddRange(retention.Issues);
        }

        if (issues.Count != 0)
        {
            return ValidationResult.Failure<DesktopSettings>(issues);
        }

        var canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(draft.DefaultProjectRoot!));
        return ValidationResult.Success(new DesktopSettings(
            canonicalRoot,
            ideId!,
            teamId!,
            culture!,
            draft.Theme,
            draft.OnboardingCompleted,
            retention.Value.MaxAgeDays,
            retention.Value.MaxTotalBytes));
    }

    private static string? NormalizeIdentifier(
        string? value,
        string location,
        List<ValidationIssue> issues)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Length > 128
            || normalized.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_'))
            || RedactedText.IsSecretShapedKey(normalized))
        {
            issues.Add(new ValidationIssue(
                "desktop.settings.identifier.invalid",
                "Choose a bounded non-secret identifier or none.",
                location));
            return null;
        }

        return normalized;
    }

    private static string? NormalizeIdeIdentifier(string? value, List<ValidationIssue> issues)
    {
        var normalized = NormalizeIdentifier(value, nameof(DesktopSettingsDraft.DefaultIdeId), issues);
        if (normalized is not null && !_supportedIdeIds.Contains(normalized))
        {
            issues.Add(new ValidationIssue(
                "desktop.settings.ide.unsupported",
                "Choose a supported IDE or none.",
                nameof(DesktopSettingsDraft.DefaultIdeId)));
            return null;
        }

        return normalized;
    }

    private static AppSetting CreateText(string key, string value, DateTimeOffset timestamp)
    {
        var settingValue = AppSettingValue.CreateString(value);
        if (!settingValue.IsValid)
        {
            throw new InvalidOperationException("A validated setting could not be encoded.");
        }

        return CreateSetting(key, settingValue.Value, timestamp);
    }

    private static AppSetting CreateBoolean(string key, bool value, DateTimeOffset timestamp)
    {
        return CreateSetting(key, AppSettingValue.CreateBoolean(value), timestamp);
    }

    private static AppSetting CreateInteger(string key, long value, DateTimeOffset timestamp)
    {
        return CreateSetting(key, AppSettingValue.CreateInteger(value), timestamp);
    }

    private static AppSetting CreateSetting(string key, AppSettingValue value, DateTimeOffset timestamp)
    {
        var setting = AppSetting.Create(key, value, timestamp);
        return setting.IsValid
            ? setting.Value
            : throw new InvalidOperationException("A fixed desktop setting key is invalid.");
    }

    private static string ReadText(
        Dictionary<string, AppSetting> settings,
        string key,
        string fallback)
    {
        return settings.TryGetValue(key, out var setting)
            && setting.Value.Kind == AppSettingValueKind.Text
                ? setting.Value.StringValue
                : fallback;
    }

    private static string ReadIdentifier(Dictionary<string, AppSetting> settings, string key)
    {
        var value = ReadText(settings, key, "none");
        var issues = new List<ValidationIssue>();
        return NormalizeIdentifier(value, key, issues) ?? "none";
    }

    private static string ReadIdeIdentifier(Dictionary<string, AppSetting> settings)
    {
        var value = ReadText(settings, DesktopSettingKeys.DefaultIdeId, "none");
        var issues = new List<ValidationIssue>();
        return NormalizeIdeIdentifier(value, issues) ?? "none";
    }

    private static string ReadCulture(Dictionary<string, AppSetting> settings)
    {
        var value = ReadText(settings, DesktopSettingKeys.Culture, "en-US");
        return _supportedCultures.Contains(value) ? value : "en-US";
    }

    private static ThemePreference ReadTheme(Dictionary<string, AppSetting> settings)
    {
        var value = ReadText(settings, DesktopSettingKeys.Theme, nameof(ThemePreference.System));
        return Enum.TryParse<ThemePreference>(value, ignoreCase: false, out var theme)
            && Enum.IsDefined(theme)
                ? theme
                : ThemePreference.System;
    }

    private static bool ReadBoolean(Dictionary<string, AppSetting> settings, string key)
    {
        return settings.TryGetValue(key, out var setting)
            && setting.Value.Kind == AppSettingValueKind.BooleanFlag
            && setting.Value.BooleanValue;
    }

    private static long ReadInteger(
        Dictionary<string, AppSetting> settings,
        string key,
        long fallback)
    {
        return settings.TryGetValue(key, out var setting)
            && setting.Value.Kind == AppSettingValueKind.WholeNumber
                ? setting.Value.IntegerValue
                : fallback;
    }

    private static DiagnosticRetentionPolicy ReadRetentionPolicy(
        Dictionary<string, AppSetting> settings)
    {
        var days = ReadInteger(
            settings,
            DesktopSettingKeys.DiagnosticRetentionDays,
            DiagnosticRetentionPolicy.Default.MaxAgeDays);
        var bytes = ReadInteger(
            settings,
            DesktopSettingKeys.DiagnosticRetentionMaxBytes,
            DiagnosticRetentionPolicy.Default.MaxTotalBytes);
        if (days is < int.MinValue or > int.MaxValue)
        {
            return DiagnosticRetentionPolicy.Default;
        }

        var policy = DiagnosticRetentionPolicy.Create((int)days, bytes);
        return policy.IsValid ? policy.Value : DiagnosticRetentionPolicy.Default;
    }
}
