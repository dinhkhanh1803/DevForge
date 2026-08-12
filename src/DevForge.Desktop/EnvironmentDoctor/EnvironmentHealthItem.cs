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

    public string CompatibilitySummary => Status switch
    {
        EnvironmentToolStatus.Compatible => "Compatible with the current DevForge policy.",
        EnvironmentToolStatus.Installed => "Installed; no stricter compatibility rule is configured.",
        EnvironmentToolStatus.Missing => "Not detected in the trusted tool catalog.",
        EnvironmentToolStatus.Outdated => "Detected version is below the supported policy.",
        EnvironmentToolStatus.Conflicting => "Multiple or conflicting installations were detected.",
        EnvironmentToolStatus.Unknown => "Compatibility could not be determined safely.",
        _ => "Compatibility could not be determined safely.",
    };

    public string Remediation => Status switch
    {
        EnvironmentToolStatus.Compatible or EnvironmentToolStatus.Installed => "No action required.",
        EnvironmentToolStatus.Missing => "Install the tool from its official vendor, then rescan.",
        EnvironmentToolStatus.Outdated => "Update the tool with vendor-approved steps, then rescan.",
        EnvironmentToolStatus.Conflicting => "Keep one trusted installation active, then rescan.",
        EnvironmentToolStatus.Unknown => "Check the local installation and rescan.",
        _ => "Check the local installation and rescan.",
    };
}

public sealed record EnvironmentHealthSnapshot(
    ImmutableArray<EnvironmentHealthItem> Tools,
    DateTimeOffset? ScannedAt,
    EnvironmentSnapshotSource Source,
    bool IsStale,
    bool ScanFailed);
