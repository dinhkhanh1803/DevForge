namespace DevForge.Blueprints.Abstractions.Models;

public enum BlueprintTrustLevel
{
    BuiltIn,
    TrustedLocal,
    Untrusted,
    Quarantined,
}

public enum BlueprintInputKind
{
    Text,
    Boolean,
    WholeNumber,
    Choice,
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
    BlueprintTrustLevel Trust,
    IReadOnlyCollection<ToolRequirement?>? Tools,
    IReadOnlyCollection<InputDefinition?>? Inputs,
    IReadOnlyCollection<CompatibilityRule?>? CompatibilityRules,
    IReadOnlyCollection<BlueprintStepDefinition?>? Steps,
    IReadOnlyCollection<ValidatorDefinition?>? Validators);
