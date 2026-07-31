using System.Collections.Immutable;
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
        IEnumerable<KeyValuePair<string, string>> properties)
    {
        CapturedAt = capturedAt;
        Tools = [.. tools];
        Properties = properties.ToImmutableDictionary(StringComparer.Ordinal);
    }

    public DateTimeOffset CapturedAt { get; }

    public ImmutableArray<EnvironmentTool> Tools { get; }

    public ImmutableDictionary<string, string> Properties { get; }

    public static ValidationResult<EnvironmentSnapshot> Create(
        DateTimeOffset capturedAt,
        IEnumerable<EnvironmentTool?>? tools,
        IEnumerable<KeyValuePair<string, string>>? properties)
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
                else if (!toolNames.Add(tool.Name))
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
                else if (!propertyNames.Add(property.Key))
                {
                    issues.Add(
                        new ValidationIssue(
                            "environment.property.name.duplicate",
                            "Environment property names must be unique.",
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

        return issues.Count == 0
            ? ValidationResult.Success(
                new EnvironmentSnapshot(
                    capturedAt,
                    toolsSnapshot.Select(tool => tool!),
                    propertiesSnapshot))
            : ValidationResult.Failure<EnvironmentSnapshot>(issues);
    }
}
