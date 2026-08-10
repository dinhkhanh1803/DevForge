namespace DevForge.Infrastructure.Persistence.Entities;

internal sealed class AppSettingEntity
{
    public string Key { get; set; } = string.Empty;

    public string ValueKind { get; set; } = string.Empty;

    public string SerializedValue { get; set; } = string.Empty;

    public long UpdatedAtUnixMs { get; set; }
}

internal sealed class IdeInstallationEntity
{
    public string Id { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string ExecutablePath { get; set; } = string.Empty;

    public string? Version { get; set; }

    public string ValidationState { get; set; } = string.Empty;

    public long ScannedAtUnixMs { get; set; }
}

internal sealed class EnvironmentToolEntity
{
    public string Id { get; set; } = string.Empty;

    public string? ExecutablePath { get; set; }

    public string? Version { get; set; }

    public string Status { get; set; } = string.Empty;

    public long ScannedAtUnixMs { get; set; }

    public long ExpiresAtUnixMs { get; set; }
}

internal sealed class BlueprintEntity
{
    public string Id { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public string Trust { get; set; } = string.Empty;

    public string Checksum { get; set; } = string.Empty;

    public bool IsDisabled { get; set; }

    public long DiscoveredAtUnixMs { get; set; }
}

internal sealed class TeamProfileEntity
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int SchemaVersion { get; set; }

    public string PolicyJson { get; set; } = string.Empty;

    public long UpdatedAtUnixMs { get; set; }
}

internal sealed class PresetEntity
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int SchemaVersion { get; set; }

    public string RecipeJson { get; set; } = string.Empty;

    public long UpdatedAtUnixMs { get; set; }
}

internal sealed class RecentProjectEntity
{
    public string ProjectPath { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? RepositoryUrl { get; set; }

    public string? IdeId { get; set; }

    public long LastOpenedAtUnixMs { get; set; }
}
