using System.Collections.Immutable;
using DevForge.Blueprints.Abstractions.Validation;

namespace DevForge.Blueprints.Abstractions.Models;

public enum BlueprintValueKind
{
    Text = 1,
    Boolean = 2,
    WholeNumber = 3,
    Sequence = 4,
    Map = 5,
}

public enum CompatibilityRuleSeverity
{
    Blocking = 1,
    Warning = 2,
}

public enum CompatibilityRuleOverride
{
    None = 1,
}

public sealed record BlueprintFeatureDefinition(string Id, bool DefaultEnabled);

public sealed record BlueprintDependency(string Id, string Version);

public sealed record BlueprintArtifact(string Path);

public sealed record BlueprintActionDefinition(
    string Id,
    string HandlerId,
    ImmutableDictionary<string, BlueprintValue> Parameters,
    TimeSpan Timeout);

public sealed class BlueprintValue : IEquatable<BlueprintValue>
{
    public const int MaximumDepth = 64;
    public const int MaximumCollectionItems = 2048;
    public const int MaximumTextLength = 16384;

    private BlueprintValue(
        BlueprintValueKind kind,
        string? stringValue = null,
        bool booleanValue = false,
        long integerValue = 0,
        ImmutableArray<BlueprintValue> arrayValue = default,
        ImmutableDictionary<string, BlueprintValue>? objectValue = null,
        int depth = 0)
    {
        Kind = kind;
        StringValue = stringValue;
        BooleanValue = booleanValue;
        IntegerValue = integerValue;
        ArrayValue = arrayValue.IsDefault ? [] : arrayValue;
        ObjectValue = objectValue
            ?? ImmutableDictionary<string, BlueprintValue>.Empty.WithComparers(StringComparer.Ordinal);
        Depth = depth;
    }

    public BlueprintValueKind Kind { get; }

    public string? StringValue { get; }

    public bool BooleanValue { get; }

    public long IntegerValue { get; }

    public ImmutableArray<BlueprintValue> ArrayValue { get; }

    public ImmutableDictionary<string, BlueprintValue> ObjectValue { get; }

    private int Depth { get; }

    public static BlueprintValidationResult<BlueprintValue> FromString(string? value)
    {
        var issues = new List<BlueprintValidationIssue>();
        if (value is null)
        {
            issues.Add(new BlueprintValidationIssue(
                "blueprint.value.string.required",
                "A blueprint string value is required.",
                "value"));
        }
        else if (value.Length > MaximumTextLength)
        {
            issues.Add(new BlueprintValidationIssue(
                "blueprint.value.string.too-long",
                "A blueprint string value exceeds the supported length.",
                "value"));
        }
        else if (value.Contains('\0'))
        {
            issues.Add(new BlueprintValidationIssue(
                "blueprint.value.string.null-character",
                "A blueprint string value cannot contain null characters.",
                "value"));
        }
        else if (BlueprintPrivacyPolicy.ContainsSensitiveDefault(value))
        {
            issues.Add(new BlueprintValidationIssue(
                "blueprint.value.string.secret-shaped",
                "A blueprint string value resembles credential material.",
                "value"));
        }

        return issues.Count == 0
            ? BlueprintValidationResult.Success(new BlueprintValue(BlueprintValueKind.Text, stringValue: value))
            : BlueprintValidationResult.Failure<BlueprintValue>(issues);
    }

    public static BlueprintValue FromBoolean(bool value)
    {
        return new BlueprintValue(BlueprintValueKind.Boolean, booleanValue: value);
    }

    public static BlueprintValue FromInteger(long value)
    {
        return new BlueprintValue(BlueprintValueKind.WholeNumber, integerValue: value);
    }

    public static BlueprintValidationResult<BlueprintValue> FromArray(
        IEnumerable<BlueprintValue?>? values)
    {
        var snapshot = values?.ToImmutableArray() ?? [];
        var issues = new List<BlueprintValidationIssue>();
        if (values is null)
        {
            issues.Add(new BlueprintValidationIssue(
                "blueprint.value.collection.required",
                "A blueprint array collection is required.",
                "values"));
        }
        else
        {
            AddCollectionIssues(snapshot.Length, issues);
            for (var index = 0; index < snapshot.Length; index++)
            {
                if (snapshot[index] is null)
                {
                    issues.Add(new BlueprintValidationIssue(
                        "blueprint.value.item.required",
                        "A blueprint collection item is required.",
                        $"values[{index}]"));
                }
            }
        }

        var depth = snapshot.IsEmpty
            ? 1
            : 1 + snapshot.Where(value => value is not null)
                .Select(value => value!.Depth)
                .DefaultIfEmpty()
                .Max();
        AddDepthIssue(depth, issues);
        return issues.Count == 0
            ? BlueprintValidationResult.Success(new BlueprintValue(
                BlueprintValueKind.Sequence,
                arrayValue: [.. snapshot.Select(value => value!)],
                depth: depth))
            : BlueprintValidationResult.Failure<BlueprintValue>(issues);
    }

    public static BlueprintValidationResult<BlueprintValue> FromObject(
        IEnumerable<KeyValuePair<string, BlueprintValue?>>? values)
    {
        var snapshot = values?.ToImmutableArray() ?? [];
        var normalized = new List<KeyValuePair<string, BlueprintValue>>(snapshot.Length);
        var issues = new List<BlueprintValidationIssue>();
        if (values is null)
        {
            issues.Add(new BlueprintValidationIssue(
                "blueprint.value.collection.required",
                "A blueprint object collection is required.",
                "values"));
        }
        else
        {
            AddCollectionIssues(snapshot.Length, issues);
            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < snapshot.Length; index++)
            {
                var item = snapshot[index];
                var key = item.Key?.Trim();
                if (string.IsNullOrWhiteSpace(key))
                {
                    issues.Add(new BlueprintValidationIssue(
                        "blueprint.value.key.required",
                        "A blueprint object key is required.",
                        $"values[{index}].key"));
                }
                else
                {
                    if (!keys.Add(key))
                    {
                        issues.Add(new BlueprintValidationIssue(
                            "blueprint.value.key.duplicate",
                            "Blueprint object keys must be unique.",
                            $"values[{index}].key"));
                    }

                    if (BlueprintPrivacyPolicy.IsSensitiveIdentifier(key))
                    {
                        issues.Add(new BlueprintValidationIssue(
                            "blueprint.value.key.secret-shaped",
                            "Blueprint object keys cannot describe secrets.",
                            $"values[{index}].key"));
                    }
                }

                if (item.Value is null)
                {
                    issues.Add(new BlueprintValidationIssue(
                        "blueprint.value.item.required",
                        "A blueprint collection item is required.",
                        $"values[{index}].value"));
                }
                else if (!string.IsNullOrWhiteSpace(key))
                {
                    normalized.Add(KeyValuePair.Create(key, item.Value));
                }
            }
        }

        var depth = snapshot.IsEmpty
            ? 1
            : 1 + snapshot.Where(item => item.Value is not null)
                .Select(item => item.Value!.Depth)
                .DefaultIfEmpty()
                .Max();
        AddDepthIssue(depth, issues);
        return issues.Count == 0
            ? BlueprintValidationResult.Success(new BlueprintValue(
                BlueprintValueKind.Map,
                objectValue: normalized.ToImmutableDictionary(StringComparer.Ordinal),
                depth: depth))
            : BlueprintValidationResult.Failure<BlueprintValue>(issues);
    }

    public bool Equals(BlueprintValue? other)
    {
        if (other is null || Kind != other.Kind)
        {
            return false;
        }

        return Kind switch
        {
            BlueprintValueKind.Text => StringComparer.Ordinal.Equals(StringValue, other.StringValue),
            BlueprintValueKind.Boolean => BooleanValue == other.BooleanValue,
            BlueprintValueKind.WholeNumber => IntegerValue == other.IntegerValue,
            BlueprintValueKind.Sequence => ArrayValue.SequenceEqual(other.ArrayValue),
            BlueprintValueKind.Map => ObjectValue.Count == other.ObjectValue.Count
                && ObjectValue.All(pair => other.ObjectValue.TryGetValue(pair.Key, out var value)
                    && pair.Value.Equals(value)),
            _ => false,
        };
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as BlueprintValue);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(StringValue, StringComparer.Ordinal);
        hash.Add(BooleanValue);
        hash.Add(IntegerValue);
        foreach (var value in ArrayValue)
        {
            hash.Add(value);
        }

        foreach (var pair in ObjectValue.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            hash.Add(pair.Key, StringComparer.Ordinal);
            hash.Add(pair.Value);
        }

        return hash.ToHashCode();
    }

    private static void AddCollectionIssues(int count, List<BlueprintValidationIssue> issues)
    {
        if (count > MaximumCollectionItems)
        {
            issues.Add(new BlueprintValidationIssue(
                "blueprint.value.collection.too-large",
                "A blueprint collection exceeds the supported item limit.",
                "values"));
        }
    }

    private static void AddDepthIssue(int depth, List<BlueprintValidationIssue> issues)
    {
        if (depth > MaximumDepth)
        {
            issues.Add(new BlueprintValidationIssue(
                "blueprint.value.depth.exceeded",
                "A blueprint value exceeds the supported nesting depth.",
                "values"));
        }
    }
}

public sealed record BlueprintInputPropertyDraft(
    string? Id,
    BlueprintInputKind Kind,
    bool Required,
    BlueprintValue? DefaultValue,
    IReadOnlyCollection<string?>? AllowedValues,
    int? MinimumLength,
    int? MaximumLength,
    long? Minimum,
    long? Maximum);

public sealed class BlueprintInputPropertyDefinition
{
    private BlueprintInputPropertyDefinition(
        BlueprintInputPropertyDraft draft,
        ImmutableArray<string> allowedValues)
    {
        Id = draft.Id!.Trim();
        Kind = draft.Kind;
        Required = draft.Required;
        DefaultValue = draft.DefaultValue;
        AllowedValues = allowedValues;
        MinimumLength = draft.MinimumLength;
        MaximumLength = draft.MaximumLength;
        Minimum = draft.Minimum;
        Maximum = draft.Maximum;
    }

    public string Id { get; }

    public BlueprintInputKind Kind { get; }

    public bool Required { get; }

    public BlueprintValue? DefaultValue { get; }

    public ImmutableArray<string> AllowedValues { get; }

    public int? MinimumLength { get; }

    public int? MaximumLength { get; }

    public long? Minimum { get; }

    public long? Maximum { get; }

    public static BlueprintValidationResult<BlueprintInputPropertyDefinition> Create(
        BlueprintInputPropertyDraft? draft)
    {
        if (draft is null)
        {
            return BlueprintValidationResult.Failure<BlueprintInputPropertyDefinition>(
            [
                new BlueprintValidationIssue(
                    "blueprint.input.required",
                    "A blueprint input property is required."),
            ]);
        }

        var choices = draft.AllowedValues?.ToImmutableArray() ?? [];
        var issues = new List<BlueprintValidationIssue>();
        if (!BlueprintIdentifierValidator.IsValid(draft.Id))
        {
            issues.Add(new BlueprintValidationIssue(
                "blueprint.input.id.invalid",
                "An input identifier must use lowercase dot- or hyphen-separated segments.",
                "id"));
        }

        if (!Enum.IsDefined(draft.Kind))
        {
            issues.Add(new BlueprintValidationIssue(
                "blueprint.input.kind.invalid",
                "The blueprint input kind is not defined.",
                "kind"));
        }

        if (draft.DefaultValue is not null && !MatchesKind(draft.Kind, draft.DefaultValue.Kind))
        {
            issues.Add(new BlueprintValidationIssue(
                "blueprint.input.default.kind-mismatch",
                "The blueprint input default does not match its declared kind.",
                "defaultValue"));
        }

        if (choices.Length > BlueprintValue.MaximumCollectionItems)
        {
            issues.Add(new BlueprintValidationIssue(
                "blueprint.input.choices.too-large",
                "The blueprint input choice collection exceeds the supported item limit.",
                "allowedValues"));
        }

        if (draft.Kind is not (BlueprintInputKind.Text or BlueprintInputKind.Choice)
            && (draft.MinimumLength is not null || draft.MaximumLength is not null))
        {
            issues.Add(new BlueprintValidationIssue(
                "blueprint.input.length.not-applicable",
                "String length constraints apply only to text or choice inputs.",
                "minimumLength"));
        }
        else if (draft.MinimumLength is < 0
            || draft.MaximumLength is < 0
            || draft.MinimumLength > draft.MaximumLength)
        {
            issues.Add(new BlueprintValidationIssue(
                "blueprint.input.length-range.invalid",
                "The blueprint input length range is invalid.",
                "minimumLength"));
        }

        if (draft.Kind != BlueprintInputKind.WholeNumber
            && (draft.Minimum is not null || draft.Maximum is not null))
        {
            issues.Add(new BlueprintValidationIssue(
                "blueprint.input.numeric.not-applicable",
                "Numeric constraints apply only to whole-number inputs.",
                "minimum"));
        }
        else if (draft.Minimum > draft.Maximum)
        {
            issues.Add(new BlueprintValidationIssue(
                "blueprint.input.numeric-range.invalid",
                "The blueprint input numeric range is invalid.",
                "minimum"));
        }

        var normalizedChoices = new List<string>(choices.Length);
        var uniqueChoices = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < choices.Length; index++)
        {
            var choice = choices[index]?.Trim();
            if (string.IsNullOrWhiteSpace(choice))
            {
                issues.Add(new BlueprintValidationIssue(
                    "blueprint.input.choice.required",
                    "A blueprint input choice is required.",
                    $"allowedValues[{index}]"));
            }
            else if (!uniqueChoices.Add(choice))
            {
                issues.Add(new BlueprintValidationIssue(
                    "blueprint.input.choice.duplicate",
                    "Blueprint input choices must be unique.",
                    $"allowedValues[{index}]"));
            }
            else
            {
                normalizedChoices.Add(choice);
            }
        }

        if (draft.DefaultValue?.Kind == BlueprintValueKind.Text
            && normalizedChoices.Count > 0
            && !uniqueChoices.Contains(draft.DefaultValue.StringValue!))
        {
            issues.Add(new BlueprintValidationIssue(
                "blueprint.input.default.not-allowed",
                "The blueprint input default is not in the allowed value set.",
                "defaultValue"));
        }

        return issues.Count == 0
            ? BlueprintValidationResult.Success(new BlueprintInputPropertyDefinition(
                draft,
                [.. normalizedChoices]))
            : BlueprintValidationResult.Failure<BlueprintInputPropertyDefinition>(issues);
    }

    private static bool MatchesKind(BlueprintInputKind inputKind, BlueprintValueKind valueKind)
    {
        return inputKind switch
        {
            BlueprintInputKind.Text or BlueprintInputKind.Choice => valueKind == BlueprintValueKind.Text,
            BlueprintInputKind.Boolean => valueKind == BlueprintValueKind.Boolean,
            BlueprintInputKind.WholeNumber => valueKind == BlueprintValueKind.WholeNumber,
            _ => false,
        };
    }
}
