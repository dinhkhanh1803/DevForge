using System.Globalization;
using DevForge.Application.Contracts.Persistence;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Validation;
using DevForge.Infrastructure.Persistence.Entities;

namespace DevForge.Infrastructure.Persistence.Mapping;

internal static class MetadataMapper
{
    public static AppSettingEntity ToEntity(AppSetting model) => new()
    {
        Key = model.Key,
        ValueKind = model.Value.Kind.ToString(),
        SerializedValue = model.Value.Serialize(),
        UpdatedAtUnixMs = model.UpdatedAt.ToUnixTimeMilliseconds(),
    };

    public static AppSetting ToModel(AppSettingEntity entity)
    {
        if (!TryParseDefined(entity.ValueKind, out AppSettingValueKind kind))
        {
            throw new PersistenceDataException();
        }

        ValidationResult<AppSettingValue> value = kind switch
        {
            AppSettingValueKind.Text => AppSettingValue.CreateString(entity.SerializedValue),
            AppSettingValueKind.BooleanFlag when bool.TryParse(entity.SerializedValue, out var parsed) =>
                ValidationResult.Success(AppSettingValue.CreateBoolean(parsed)),
            AppSettingValueKind.WholeNumber when long.TryParse(
                entity.SerializedValue,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed) => ValidationResult.Success(AppSettingValue.CreateInteger(parsed)),
            AppSettingValueKind.JsonObject => CreateJsonSetting(entity.SerializedValue),
            _ => throw new PersistenceDataException(),
        };
        return RequireValid(AppSetting.Create(entity.Key, RequireValid(value), ToTimestamp(entity.UpdatedAtUnixMs)));
    }

    public static IdeInstallationEntity ToEntity(IdeInstallationRecord model) => new()
    {
        Id = model.Id,
        Kind = model.Kind.ToString(),
        ExecutablePath = model.ExecutablePath,
        Version = model.Version,
        ValidationState = model.ValidationState.ToString(),
        ScannedAtUnixMs = model.ScannedAt.ToUnixTimeMilliseconds(),
    };

    public static IdeInstallationRecord ToModel(IdeInstallationEntity entity)
    {
        if (!TryParseDefined(entity.Kind, out IdeKind kind)
            || !TryParseDefined(entity.ValidationState, out InstallationValidationState state))
        {
            throw new PersistenceDataException();
        }

        return RequireValid(IdeInstallationRecord.Create(
            entity.Id,
            kind,
            entity.ExecutablePath,
            entity.Version,
            state,
            ToTimestamp(entity.ScannedAtUnixMs)));
    }

    public static EnvironmentToolEntity ToEntity(EnvironmentToolRecord model) => new()
    {
        Id = model.Id,
        ExecutablePath = model.ExecutablePath,
        Version = model.Version,
        Status = model.Status.ToString(),
        ScannedAtUnixMs = model.ScannedAt.ToUnixTimeMilliseconds(),
        ExpiresAtUnixMs = model.ExpiresAt.ToUnixTimeMilliseconds(),
    };

    public static EnvironmentToolRecord ToModel(EnvironmentToolEntity entity)
    {
        if (!TryParseDefined(entity.Status, out EnvironmentToolStatus status))
        {
            throw new PersistenceDataException();
        }

        return RequireValid(EnvironmentToolRecord.Create(
            entity.Id,
            entity.ExecutablePath,
            entity.Version,
            status,
            ToTimestamp(entity.ScannedAtUnixMs),
            ToTimestamp(entity.ExpiresAtUnixMs)));
    }

    public static BlueprintEntity ToEntity(BlueprintMetadataRecord model) => new()
    {
        Id = model.Id,
        Version = model.Version,
        Source = model.Source.ToString(),
        Trust = model.Trust.ToString(),
        Checksum = model.Checksum,
        IsDisabled = model.IsDisabled,
        DiscoveredAtUnixMs = model.DiscoveredAt.ToUnixTimeMilliseconds(),
    };

    public static BlueprintMetadataRecord ToModel(BlueprintEntity entity)
    {
        if (!TryParseDefined(entity.Source, out BlueprintSource source)
            || !TryParseDefined(entity.Trust, out BlueprintTrust trust))
        {
            throw new PersistenceDataException();
        }

        return RequireValid(BlueprintMetadataRecord.Create(
            entity.Id,
            entity.Version,
            source,
            trust,
            entity.Checksum,
            entity.IsDisabled,
            ToTimestamp(entity.DiscoveredAtUnixMs)));
    }

    public static TeamProfileEntity ToEntity(TeamProfileRecord model) => new()
    {
        Id = model.Id,
        Name = model.Name,
        SchemaVersion = model.SchemaVersion,
        PolicyJson = model.Policy.Value,
        UpdatedAtUnixMs = model.UpdatedAt.ToUnixTimeMilliseconds(),
    };

    public static TeamProfileRecord ToModel(TeamProfileEntity entity) => RequireValid(TeamProfileRecord.Create(
        entity.Id,
        entity.Name,
        entity.SchemaVersion,
        RequireValid(PersistableJson.Create(entity.PolicyJson)),
        ToTimestamp(entity.UpdatedAtUnixMs)));

    public static PresetEntity ToEntity(PresetRecord model) => new()
    {
        Id = model.Id,
        Name = model.Name,
        SchemaVersion = model.SchemaVersion,
        RecipeJson = model.Recipe.Value,
        UpdatedAtUnixMs = model.UpdatedAt.ToUnixTimeMilliseconds(),
    };

    public static PresetRecord ToModel(PresetEntity entity) => RequireValid(PresetRecord.Create(
        entity.Id,
        entity.Name,
        entity.SchemaVersion,
        RequireValid(PersistableJson.Create(entity.RecipeJson)),
        ToTimestamp(entity.UpdatedAtUnixMs)));

    public static RecentProjectEntity ToEntity(RecentProjectRecord model) => new()
    {
        ProjectPath = model.ProjectPath,
        DisplayName = model.DisplayName,
        RepositoryUrl = model.RepositoryUrl,
        IdeId = model.IdeId,
        LastOpenedAtUnixMs = model.LastOpenedAt.ToUnixTimeMilliseconds(),
    };

    public static RecentProjectRecord ToModel(RecentProjectEntity entity) => RequireValid(RecentProjectRecord.Create(
        entity.ProjectPath,
        entity.DisplayName,
        entity.RepositoryUrl,
        entity.IdeId,
        ToTimestamp(entity.LastOpenedAtUnixMs)));

    private static ValidationResult<AppSettingValue> CreateJsonSetting(string json)
    {
        var document = PersistableJson.Create(json);
        return document.IsValid
            ? AppSettingValue.CreateJson(document.Value)
            : ValidationResult.Failure<AppSettingValue>(document.Issues);
    }

    private static T RequireValid<T>(ValidationResult<T> result)
    {
        return result.IsValid ? result.Value : throw new PersistenceDataException();
    }

    private static DateTimeOffset ToTimestamp(long unixMilliseconds)
    {
        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new PersistenceDataException();
        }
    }

    private static bool TryParseDefined<TEnum>(string value, out TEnum parsed)
        where TEnum : struct, Enum
    {
        return Enum.TryParse(value, ignoreCase: false, out parsed)
            && Enum.IsDefined(parsed)
            && string.Equals(value, parsed.ToString(), StringComparison.Ordinal);
    }
}
