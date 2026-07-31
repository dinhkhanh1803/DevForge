using System.Collections.Immutable;

namespace DevForge.Domain.Environment;

public sealed record EnvironmentTool(
    string Name,
    string? Version,
    bool IsAvailable);

public sealed class EnvironmentSnapshot
{
    public EnvironmentSnapshot(
        DateTimeOffset capturedAt,
        IEnumerable<EnvironmentTool> tools,
        IEnumerable<KeyValuePair<string, string>> properties)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(properties);

        CapturedAt = capturedAt;
        Tools = [.. tools];
        Properties = properties.ToImmutableDictionary(StringComparer.Ordinal);
    }

    public DateTimeOffset CapturedAt { get; }

    public ImmutableArray<EnvironmentTool> Tools { get; }

    public ImmutableDictionary<string, string> Properties { get; }
}
