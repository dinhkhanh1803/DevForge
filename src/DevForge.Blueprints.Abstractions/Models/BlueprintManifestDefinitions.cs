using System.Collections.Immutable;

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
    string Id,
    string Expression,
    CompatibilityRuleSeverity Severity,
    string Message,
    string? Remediation = null,
    CompatibilityRuleOverride Override = CompatibilityRuleOverride.None)
{
    public CompatibilityRule(string expression, string message)
        : this(
            "compatibility",
            expression,
            CompatibilityRuleSeverity.Blocking,
            message)
    {
    }
}

public sealed record BlueprintStepDefinition(
    string Id,
    string HandlerId,
    TimeSpan Timeout);

public sealed class ValidatorDefinition : IEquatable<ValidatorDefinition>
{
    public ValidatorDefinition(
        string id,
        string handlerId,
        TimeSpan timeout,
        ImmutableDictionary<string, BlueprintValue>? parameters = null,
        bool required = true)
    {
        Id = id;
        HandlerId = handlerId;
        Timeout = timeout;
        Parameters = parameters
            ?? ImmutableDictionary<string, BlueprintValue>.Empty.WithComparers(StringComparer.Ordinal);
        Required = required;
    }

    public string Id { get; }

    public string HandlerId { get; }

    public TimeSpan Timeout { get; }

    public ImmutableDictionary<string, BlueprintValue> Parameters { get; }

    public bool Required { get; }

    public bool Equals(ValidatorDefinition? other)
    {
        return other is not null
            && StringComparer.Ordinal.Equals(Id, other.Id)
            && StringComparer.Ordinal.Equals(HandlerId, other.HandlerId)
            && Timeout == other.Timeout
            && Required == other.Required
            && Parameters.Count == other.Parameters.Count
            && Parameters.All(pair => other.Parameters.TryGetValue(pair.Key, out var value)
                && pair.Value.Equals(value));
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as ValidatorDefinition);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id, StringComparer.Ordinal);
        hash.Add(HandlerId, StringComparer.Ordinal);
        hash.Add(Timeout);
        hash.Add(Required);
        foreach (var pair in Parameters.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            hash.Add(pair.Key, StringComparer.Ordinal);
            hash.Add(pair.Value);
        }

        return hash.ToHashCode();
    }
}

public sealed record BlueprintManifestDraft(
    string? Id,
    string? Version,
    string? EngineVersionRange,
    IReadOnlyCollection<ToolRequirement?>? Tools,
    IReadOnlyCollection<InputDefinition?>? Inputs,
    IReadOnlyCollection<CompatibilityRule?>? CompatibilityRules,
    IReadOnlyCollection<BlueprintStepDefinition?>? Steps,
    IReadOnlyCollection<ValidatorDefinition?>? Validators,
    string? Name = null,
    IReadOnlyCollection<BlueprintFeatureDefinition?>? Features = null,
    IReadOnlyCollection<BlueprintActionDefinition?>? Actions = null,
    IReadOnlyCollection<BlueprintDependency?>? Dependencies = null,
    IReadOnlyCollection<BlueprintArtifact?>? Artifacts = null);
