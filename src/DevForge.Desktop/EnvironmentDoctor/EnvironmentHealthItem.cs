using System.Collections.Immutable;
using DevForge.Application.Contracts.Persistence;

namespace DevForge.Desktop.EnvironmentDoctor;

public enum EnvironmentSnapshotSource
{
    Cache = 1,
    Fresh = 2,
}

public sealed record EnvironmentHealthItem(
    string Id,
    string? Version,
    EnvironmentToolStatus Status,
    DateTimeOffset ScannedAt)
{
    public string StatusLabel => Status.ToString();

    public string StatusGlyph => Status switch
    {
        EnvironmentToolStatus.Installed or EnvironmentToolStatus.Compatible => "Check",
        EnvironmentToolStatus.Missing => "Missing",
        EnvironmentToolStatus.Outdated => "Update",
        EnvironmentToolStatus.Conflicting => "Conflict",
        EnvironmentToolStatus.Unknown => "Unknown",
        _ => "Unknown",
    };
}

public sealed record EnvironmentHealthSnapshot(
    ImmutableArray<EnvironmentHealthItem> Tools,
    DateTimeOffset? ScannedAt,
    EnvironmentSnapshotSource Source,
    bool IsStale,
    bool ScanFailed);
