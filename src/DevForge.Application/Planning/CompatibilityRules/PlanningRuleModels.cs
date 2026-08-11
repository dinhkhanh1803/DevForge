using System.Collections.Immutable;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Blueprints.Abstractions.Validation;
using DevForge.Domain.Privacy;
using DevForge.Domain.Validation;

namespace DevForge.Application.Planning.CompatibilityRules;

public enum PlanningRuleValueKind
{
    Text = 1,
    Boolean = 2,
    WholeNumber = 3,
    SemanticVersion = 4,
}

public sealed class PlanningRuleValue : IEquatable<PlanningRuleValue>
{
    private PlanningRuleValue(
        PlanningRuleValueKind kind,
        string? text = null,
        bool boolean = false,
        long wholeNumber = 0,
        SemanticVersion? semanticVersion = null)
    {
        Kind = kind;
        Text = text;
        Boolean = boolean;
        WholeNumber = wholeNumber;
        SemanticVersion = semanticVersion;
    }

    public PlanningRuleValueKind Kind { get; }

    public string? Text { get; }

    public bool Boolean { get; }

    public long WholeNumber { get; }

    public SemanticVersion? SemanticVersion { get; }

    public static ValidationResult<PlanningRuleValue> FromText(string? value)
    {
        var blueprintValue = BlueprintValue.FromString(value);
        return blueprintValue.IsValid
            ? ValidationResult.Success(new PlanningRuleValue(PlanningRuleValueKind.Text, text: value))
            : Failure("A safe bounded planning-rule text value is required.");
    }

    public static PlanningRuleValue FromBoolean(bool value)
    {
        return new PlanningRuleValue(PlanningRuleValueKind.Boolean, boolean: value);
    }

    public static PlanningRuleValue FromInteger(long value)
    {
        return new PlanningRuleValue(PlanningRuleValueKind.WholeNumber, wholeNumber: value);
    }

    public static ValidationResult<PlanningRuleValue> FromSemanticVersion(string? value)
    {
        return global::DevForge.Blueprints.Abstractions.Models.SemanticVersion.TryParse(
            value,
            out var version)
                ? ValidationResult.Success(new PlanningRuleValue(
                    PlanningRuleValueKind.SemanticVersion,
                    semanticVersion: version))
                : Failure("A semantic version is required for the planning-rule value.");
    }

    public bool Equals(PlanningRuleValue? other)
    {
        return other is not null
            && Kind == other.Kind
            && Kind switch
            {
                PlanningRuleValueKind.Text => StringComparer.Ordinal.Equals(Text, other.Text),
                PlanningRuleValueKind.Boolean => Boolean == other.Boolean,
                PlanningRuleValueKind.WholeNumber => WholeNumber == other.WholeNumber,
                PlanningRuleValueKind.SemanticVersion => SemanticVersion!.CompareTo(other.SemanticVersion) == 0,
                _ => false,
            };
    }

    public override bool Equals(object? obj) => Equals(obj as PlanningRuleValue);

    public override int GetHashCode()
    {
        return Kind switch
        {
            PlanningRuleValueKind.Text => HashCode.Combine(Kind, Text),
            PlanningRuleValueKind.Boolean => HashCode.Combine(Kind, Boolean),
            PlanningRuleValueKind.WholeNumber => HashCode.Combine(Kind, WholeNumber),
            PlanningRuleValueKind.SemanticVersion => HashCode.Combine(
                Kind,
                SemanticVersion!.Normalized.Split('+')[0]),
            _ => 0,
        };
    }

    private static ValidationResult<PlanningRuleValue> Failure(string message)
    {
        return ValidationResult.Failure<PlanningRuleValue>(
        [
            new ValidationIssue("DF-PLAN-001", message, "value"),
        ]);
    }
}

public sealed class PlanningRuleContext
{
    private readonly ImmutableDictionary<string, PlanningRuleValue> _values;

    private PlanningRuleContext(ImmutableDictionary<string, PlanningRuleValue> values)
    {
        _values = values;
    }

    public static ValidationResult<PlanningRuleContext> Create(
        IEnumerable<KeyValuePair<string, PlanningRuleValue?>>? values)
    {
        var snapshot = values?.ToImmutableArray() ?? [];
        var normalized = ImmutableDictionary.CreateBuilder<string, PlanningRuleValue>(
            StringComparer.Ordinal);
        var issues = new List<ValidationIssue>();
        if (values is null)
        {
            issues.Add(Issue("A planning-rule context is required.", "values"));
        }

        for (var index = 0; index < snapshot.Length; index++)
        {
            var item = snapshot[index];
            if (!PlanningRuleIdentifierPolicy.IsAllowed(item.Key)
                || item.Value is null
                || !PlanningRuleIdentifierPolicy.AcceptsKind(item.Key, item.Value.Kind)
                || !normalized.TryAdd(item.Key, item.Value))
            {
                issues.Add(Issue(
                    "A planning-rule context entry is invalid, duplicated, or has the wrong type.",
                    $"values[{index}]"));
            }
        }

        return issues.Count == 0
            ? ValidationResult.Success(new PlanningRuleContext(normalized.ToImmutable()))
            : ValidationResult.Failure<PlanningRuleContext>(issues);
    }

    internal bool TryGetValue(string identifier, out PlanningRuleValue value)
    {
        return _values.TryGetValue(identifier, out value!);
    }

    private static ValidationIssue Issue(string message, string location)
    {
        return new ValidationIssue("DF-PLAN-001", message, location);
    }
}

public sealed record CompatibilityRuleFinding(
    string RuleId,
    CompatibilityRuleSeverity Severity,
    RedactedText Message,
    RedactedText? Remediation);

public sealed class CompatibilityRuleEvaluation
{
    internal CompatibilityRuleEvaluation(ImmutableArray<CompatibilityRuleFinding> findings)
    {
        Findings = findings;
        BlockingFailures = [.. findings.Where(item => item.Severity == CompatibilityRuleSeverity.Blocking)];
        Warnings = [.. findings.Where(item => item.Severity == CompatibilityRuleSeverity.Warning)];
    }

    public ImmutableArray<CompatibilityRuleFinding> Findings { get; }

    public ImmutableArray<CompatibilityRuleFinding> BlockingFailures { get; }

    public ImmutableArray<CompatibilityRuleFinding> Warnings { get; }

    public bool IsCompatible => BlockingFailures.IsEmpty;
}

internal static class PlanningRuleIdentifierPolicy
{
    private static readonly ImmutableHashSet<string> _textIdentifiers =
        new[]
        {
            "runtime.os",
            "runtime.arch",
            "blueprint.id",
            "team.package-manager",
            "git.branch-policy",
        }.ToImmutableHashSet(StringComparer.Ordinal);

    internal static bool IsAllowed(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier)
            || identifier != identifier.Trim()
            || RedactedText.IsSecretShapedKey(identifier))
        {
            return false;
        }

        return _textIdentifiers.Contains(identifier)
            || identifier is "engine.version" or "blueprint.version"
            || HasDynamic(identifier, "recipe.input.")
            || HasDynamic(identifier, "recipe.feature.")
            || TryGetToolMember(identifier, out _, out _);
    }

    internal static bool AcceptsKind(string identifier, PlanningRuleValueKind kind)
    {
        if (_textIdentifiers.Contains(identifier))
        {
            return kind == PlanningRuleValueKind.Text;
        }

        if (identifier is "engine.version" or "blueprint.version")
        {
            return kind == PlanningRuleValueKind.SemanticVersion;
        }

        if (HasDynamic(identifier, "recipe.input."))
        {
            return kind is PlanningRuleValueKind.Text
                or PlanningRuleValueKind.Boolean
                or PlanningRuleValueKind.WholeNumber;
        }

        if (HasDynamic(identifier, "recipe.feature."))
        {
            return kind == PlanningRuleValueKind.Boolean;
        }

        return TryGetToolMember(identifier, out _, out var member)
            && (member == "available"
                ? kind == PlanningRuleValueKind.Boolean
                : kind == PlanningRuleValueKind.SemanticVersion);
    }

    private static bool HasDynamic(string identifier, string prefix)
    {
        return identifier.StartsWith(prefix, StringComparison.Ordinal)
            && BlueprintIdentifierValidator.IsValid(identifier[prefix.Length..]);
    }

    private static bool TryGetToolMember(
        string identifier,
        out string? toolId,
        out string? member)
    {
        toolId = null;
        member = null;
        if (!identifier.StartsWith("tool.", StringComparison.Ordinal))
        {
            return false;
        }

        var lastSeparator = identifier.LastIndexOf('.');
        if (lastSeparator <= "tool.".Length || lastSeparator == identifier.Length - 1)
        {
            return false;
        }

        toolId = identifier["tool.".Length..lastSeparator];
        member = identifier[(lastSeparator + 1)..];
        return BlueprintIdentifierValidator.IsValid(toolId)
            && member is "available" or "version";
    }
}
