using System.Collections.Immutable;
using DevForge.Domain.Privacy;
using DevForge.Domain.Validation;

namespace DevForge.Application.Contracts.Persistence;

public enum AppSettingValueKind
{
    Text = 1,
    BooleanFlag = 2,
    WholeNumber = 3,
    JsonObject = 4,
}

public sealed class AppSettingValue
{
    private const int MaxStringLength = 4_096;
    private readonly object _value;

    private AppSettingValue(AppSettingValueKind kind, object value)
    {
        Kind = kind;
        _value = value;
    }

    public AppSettingValueKind Kind { get; }

    public string StringValue => Kind == AppSettingValueKind.Text
        ? (string)_value
        : throw new InvalidOperationException("The setting value is not a string.");

    public bool BooleanValue => Kind == AppSettingValueKind.BooleanFlag
        ? (bool)_value
        : throw new InvalidOperationException("The setting value is not a boolean.");

    public long IntegerValue => Kind == AppSettingValueKind.WholeNumber
        ? (long)_value
        : throw new InvalidOperationException("The setting value is not an integer.");

    public PersistableJson JsonValue => Kind == AppSettingValueKind.JsonObject
        ? (PersistableJson)_value
        : throw new InvalidOperationException("The setting value is not JSON.");

    public static ValidationResult<AppSettingValue> CreateString(string? value)
    {
        if (value is null || value.Length > MaxStringLength || value.Contains('\0'))
        {
            return Failure(
                "persistence.setting.value.invalid",
                "A string setting must be non-null, bounded, and contain no null character.");
        }

        if (!string.IsNullOrWhiteSpace(value) && !RedactedText.FromTrustedRedaction(value).IsValid)
        {
            return Failure(
                "persistence.setting.value.secret-shaped",
                "A credential-shaped value cannot be persisted as a setting.");
        }

        return ValidationResult.Success(new AppSettingValue(AppSettingValueKind.Text, value));
    }

    public static AppSettingValue CreateBoolean(bool value)
    {
        return new AppSettingValue(AppSettingValueKind.BooleanFlag, value);
    }

    public static AppSettingValue CreateInteger(long value)
    {
        return new AppSettingValue(AppSettingValueKind.WholeNumber, value);
    }

    public static ValidationResult<AppSettingValue> CreateJson(PersistableJson? value)
    {
        return value is null
            ? Failure("persistence.setting.json.required", "A JSON setting value is required.")
            : ValidationResult.Success(new AppSettingValue(AppSettingValueKind.JsonObject, value));
    }

    public override string ToString()
    {
        return $"[SETTING:{Kind}]";
    }

    internal string Serialize()
    {
        return Kind switch
        {
            AppSettingValueKind.Text => StringValue,
            AppSettingValueKind.BooleanFlag => BooleanValue ? "true" : "false",
            AppSettingValueKind.WholeNumber => IntegerValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            AppSettingValueKind.JsonObject => JsonValue.Value,
            _ => throw new InvalidOperationException("The setting kind is not defined."),
        };
    }

    private static ValidationResult<AppSettingValue> Failure(string code, string message)
    {
        return ValidationResult.Failure<AppSettingValue>(
        [
            new ValidationIssue(code, message, "value"),
        ]);
    }
}

public sealed class AppSetting
{
    private AppSetting(string key, AppSettingValue value, DateTimeOffset updatedAt)
    {
        Key = key;
        Value = value;
        UpdatedAt = updatedAt;
    }

    public string Key { get; }

    public AppSettingValue Value { get; }

    public DateTimeOffset UpdatedAt { get; }

    public static ValidationResult<AppSetting> Create(
        string? key,
        AppSettingValue? value,
        DateTimeOffset updatedAt)
    {
        var issues = new List<ValidationIssue>();
        var normalizedKey = key?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedKey)
            || normalizedKey.Length > 128
            || normalizedKey.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')))
        {
            issues.Add(
                new ValidationIssue(
                    "persistence.setting.key.invalid",
                    "A setting key must use a bounded identifier.",
                    "key"));
        }
        else if (RedactedText.IsSecretShapedKey(normalizedKey))
        {
            issues.Add(
                new ValidationIssue(
                    "persistence.setting.key.secret-shaped",
                    "Secret-shaped setting keys cannot be persisted.",
                    "key"));
        }

        if (value is null)
        {
            issues.Add(
                new ValidationIssue(
                    "persistence.setting.value.required",
                    "A typed setting value is required.",
                    "value"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new AppSetting(normalizedKey!, value!, updatedAt))
            : ValidationResult.Failure<AppSetting>(issues);
    }
}

public interface IAppSettingsStore
{
    Task<AppSetting?> GetAsync(string key, CancellationToken cancellationToken);

    Task<ImmutableArray<AppSetting>> ListAsync(CancellationToken cancellationToken);

    Task UpsertAsync(AppSetting setting, CancellationToken cancellationToken);

    Task<bool> RemoveAsync(string key, CancellationToken cancellationToken);
}
