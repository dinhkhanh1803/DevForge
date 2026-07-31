using System.Collections.Immutable;
using DevForge.Domain.Privacy;
using DevForge.Domain.Validation;

namespace DevForge.Domain.Environment;

public sealed record EnvironmentTool(
    string Name,
    string? Version,
    bool IsAvailable);

public sealed class EnvironmentSnapshot
{
    private EnvironmentSnapshot(
        DateTimeOffset capturedAt,
        IEnumerable<EnvironmentTool> tools,
        IEnumerable<KeyValuePair<string, RedactedText>> properties)
    {
        CapturedAt = capturedAt;
        Tools = [.. tools];
        Properties = properties.ToImmutableDictionary(StringComparer.Ordinal);
    }

    public DateTimeOffset CapturedAt { get; }

    public ImmutableArray<EnvironmentTool> Tools { get; }

    public ImmutableDictionary<string, RedactedText> Properties { get; }

    public static ValidationResult<EnvironmentSnapshot> Create(
        DateTimeOffset capturedAt,
        IEnumerable<EnvironmentTool?>? tools,
        IEnumerable<KeyValuePair<string, RedactedText>>? properties)
    {
        var issues = new List<ValidationIssue>();
        var toolsSnapshot = tools?.ToImmutableArray() ?? [];
        if (tools is null)
        {
            issues.Add(
                new ValidationIssue(
                    "environment.tools.required",
                    "Environment tools are required.",
                    "tools"));
        }
        else
        {
            var toolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < toolsSnapshot.Length; index++)
            {
                var tool = toolsSnapshot[index];
                if (tool is null || string.IsNullOrWhiteSpace(tool.Name))
                {
                    issues.Add(
                        new ValidationIssue(
                            "environment.tool.name.required",
                            "An environment tool name is required.",
                            $"tools[{index}].name"));
                }
                var normalizedName = tool?.Name?.Trim();
                if (normalizedName is not null && !toolNames.Add(normalizedName))
                {
                    issues.Add(
                        new ValidationIssue(
                            "environment.tool.name.duplicate",
                            "Environment tool names must be unique.",
                            $"tools[{index}].name"));
                }
            }
        }

        var propertiesSnapshot = properties?.ToImmutableArray() ?? [];
        if (properties is null)
        {
            issues.Add(
                new ValidationIssue(
                    "environment.properties.required",
                    "Environment properties are required.",
                    "properties"));
        }
        else
        {
            var propertyNames = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < propertiesSnapshot.Length; index++)
            {
                var property = propertiesSnapshot[index];
                if (string.IsNullOrWhiteSpace(property.Key))
                {
                    issues.Add(
                        new ValidationIssue(
                            "environment.property.name.required",
                            "An environment property name is required.",
                            $"properties[{index}].name"));
                }
                var normalizedName = property.Key?.Trim();
                if (normalizedName is not null && !propertyNames.Add(normalizedName))
                {
                    issues.Add(
                        new ValidationIssue(
                            "environment.property.name.duplicate",
                            "Environment property names must be unique.",
                            $"properties[{index}].name"));
                }

                else if (RedactedText.IsSecretShapedKey(normalizedName))
                {
                    issues.Add(
                        new ValidationIssue(
                            "environment.property.name.secret-shaped",
                            "Environment property names cannot describe secrets.",
                            $"properties[{index}].name"));
                }

                if (property.Value is null)
                {
                    issues.Add(
                        new ValidationIssue(
                            "environment.property.value.required",
                            "An environment property value is required.",
                            $"properties[{index}].value"));
                }
            }
        }

        var normalizedTools = toolsSnapshot.Select(
            tool => new EnvironmentTool(tool!.Name.Trim(), tool.Version?.Trim(), tool.IsAvailable));
        var normalizedProperties = propertiesSnapshot.Select(
            property => KeyValuePair.Create(property.Key.Trim(), property.Value));

        return issues.Count == 0
            ? ValidationResult.Success(
                new EnvironmentSnapshot(
                    capturedAt,
                    normalizedTools,
                    normalizedProperties))
            : ValidationResult.Failure<EnvironmentSnapshot>(issues);
    }
}
