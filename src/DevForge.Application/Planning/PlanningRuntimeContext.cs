using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Validation;

namespace DevForge.Application.Planning;

public interface IPlanningRuntimeContextProvider
{
    PlanningRuntimeContext GetCurrent();
}

public sealed class PlanningRuntimeContext
{
    private PlanningRuntimeContext(
        SemanticVersion engineVersion,
        string operatingSystem,
        string architecture)
    {
        EngineVersion = engineVersion;
        OperatingSystem = operatingSystem;
        Architecture = architecture;
    }

    public SemanticVersion EngineVersion { get; }

    public string OperatingSystem { get; }

    public string Architecture { get; }

    public static ValidationResult<PlanningRuntimeContext> Create(
        string? engineVersion,
        string? operatingSystem,
        string? architecture)
    {
        var issues = new List<ValidationIssue>();
        if (!SemanticVersion.TryParse(engineVersion, out var parsedVersion))
        {
            issues.Add(Issue("A semantic DevForge engine version is required.", "engineVersion"));
        }

        if (!IsRuntimeIdentifier(operatingSystem))
        {
            issues.Add(Issue("A canonical runtime operating-system identifier is required.", "operatingSystem"));
        }

        if (!IsRuntimeIdentifier(architecture))
        {
            issues.Add(Issue("A canonical runtime architecture identifier is required.", "architecture"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new PlanningRuntimeContext(
                parsedVersion!,
                operatingSystem!.Trim(),
                architecture!.Trim()))
            : ValidationResult.Failure<PlanningRuntimeContext>(issues);
    }

    private static bool IsRuntimeIdentifier(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value == value.Trim()
            && value.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
    }

    private static ValidationIssue Issue(string message, string location) =>
        new("DF-PLAN-001", message, location);
}
