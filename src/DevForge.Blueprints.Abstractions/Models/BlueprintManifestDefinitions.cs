namespace DevForge.Blueprints.Abstractions.Models;

public enum BlueprintTrust
{
    BuiltIn = 1,
    TrustedLocal = 2,
    Untrusted = 3,
    Quarantined = 4,
}

/// <summary>
/// Carries trust assigned by a catalog or loader boundary, independently of manifest content.
/// </summary>
public sealed record BlueprintTrustAssignment(BlueprintTrust Trust);

public enum BlueprintInputKind
{
    Text = 1,
    Boolean = 2,
    WholeNumber = 3,
    Choice = 4,
}

public sealed record ToolRequirement(
    string Id,
    string VersionRange,
    bool Required = true);

public sealed record InputDefinition(
    string Id,
    BlueprintInputKind Kind,
    bool Required,
    string? DefaultValue = null);

public sealed record CompatibilityRule(
    string Expression,
    string Message);

public sealed record BlueprintStepDefinition(
    string Id,
    string HandlerId,
    TimeSpan Timeout);

public sealed record ValidatorDefinition(
    string Id,
    string HandlerId,
    TimeSpan Timeout);

public sealed record BlueprintManifestDraft(
    string? Id,
    string? Version,
    string? EngineVersionRange,
    IReadOnlyCollection<ToolRequirement?>? Tools,
    IReadOnlyCollection<InputDefinition?>? Inputs,
    IReadOnlyCollection<CompatibilityRule?>? CompatibilityRules,
    IReadOnlyCollection<BlueprintStepDefinition?>? Steps,
    IReadOnlyCollection<ValidatorDefinition?>? Validators);
