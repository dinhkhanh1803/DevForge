using System.Collections.Immutable;
using DevForge.Domain.Privacy;
using DevForge.Domain.Validation;

namespace DevForge.Domain.Execution;

public enum PlanValueKind
{
    Text = 1,
    Boolean = 2,
    WholeNumber = 3,
    Sequence = 4,
    Map = 5,
}

public sealed class PlanValue : IEquatable<PlanValue>
{
    public const int MaximumDepth = 64;
    public const int MaximumCollectionItems = 2048;

    private PlanValue(
        PlanValueKind kind,
        string? stringValue = null,
        bool booleanValue = false,
        long integerValue = 0,
        ImmutableArray<PlanValue> arrayValue = default,
        ImmutableDictionary<string, PlanValue>? objectValue = null,
        int depth = 0)
    {
        Kind = kind;
        StringValue = stringValue;
        BooleanValue = booleanValue;
        IntegerValue = integerValue;
        ArrayValue = arrayValue.IsDefault ? [] : arrayValue;
        ObjectValue = objectValue ?? ImmutableDictionary<string, PlanValue>.Empty.WithComparers(StringComparer.Ordinal);
        Depth = depth;
    }

    public PlanValueKind Kind { get; }

    public string? StringValue { get; }

    public bool BooleanValue { get; }

    public long IntegerValue { get; }

    public ImmutableArray<PlanValue> ArrayValue { get; }

    public ImmutableDictionary<string, PlanValue> ObjectValue { get; }

    private int Depth { get; }

    public static ValidationResult<PlanValue> FromString(string? value)
    {
        var issues = new List<ValidationIssue>();
        if (value is null)
        {
            issues.Add(new ValidationIssue("plan.value.string.required", "A plan string value is required.", "value"));
        }
        else if (value.Contains('\0'))
        {
            issues.Add(new ValidationIssue("plan.value.string.null-character", "A plan string value cannot contain null characters.", "value"));
        }
        else if (RedactedText.IsSecretShapedValue(value))
        {
            issues.Add(new ValidationIssue("plan.value.string.secret-shaped", "A plan string value resembles credential material.", "value"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new PlanValue(PlanValueKind.Text, stringValue: value))
            : ValidationResult.Failure<PlanValue>(issues);
    }

    public static PlanValue FromBoolean(bool value) => new(PlanValueKind.Boolean, booleanValue: value);

    public static PlanValue FromInteger(long value) => new(PlanValueKind.WholeNumber, integerValue: value);

    public static ValidationResult<PlanValue> FromArray(IEnumerable<PlanValue?>? values)
    {
        var issues = new List<ValidationIssue>();
        var snapshot = values?.ToImmutableArray() ?? [];
        if (values is null)
        {
            issues.Add(new ValidationIssue("plan.value.collection.required", "A plan array collection is required.", "values"));
        }
        else
        {
            AddCollectionIssues(snapshot.Length, issues);
            for (var index = 0; index < snapshot.Length; index++)
            {
                if (snapshot[index] is null)
                {
                    issues.Add(new ValidationIssue("plan.value.item.required", "A plan collection item is required.", $"values[{index}]"));
                }
            }
        }

        var depth = snapshot.IsEmpty
            ? 1
            : 1 + snapshot.Where(value => value is not null).Select(value => value!.Depth).DefaultIfEmpty().Max();
        AddDepthIssue(depth, issues);
        return issues.Count == 0
            ? ValidationResult.Success(new PlanValue(
                PlanValueKind.Sequence,
                arrayValue: [.. snapshot.Select(value => value!)],
                depth: depth))
            : ValidationResult.Failure<PlanValue>(issues);
    }

    public static ValidationResult<PlanValue> FromObject(
        IEnumerable<KeyValuePair<string, PlanValue?>>? values)
    {
        var issues = new List<ValidationIssue>();
        var snapshot = values?.ToImmutableArray() ?? [];
        var normalized = new List<KeyValuePair<string, PlanValue>>(snapshot.Length);
        if (values is null)
        {
            issues.Add(new ValidationIssue("plan.value.collection.required", "A plan object collection is required.", "values"));
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
                    issues.Add(new ValidationIssue("plan.value.key.required", "A plan object key is required.", $"values[{index}].key"));
                }
                else
                {
                    if (!keys.Add(key))
                    {
                        issues.Add(new ValidationIssue("plan.value.key.duplicate", "Plan object keys must be unique.", $"values[{index}].key"));
                    }

                    if (RedactedText.IsSecretShapedKey(key))
                    {
                        issues.Add(new ValidationIssue("plan.value.key.secret-shaped", "Plan object keys cannot describe secrets.", $"values[{index}].key"));
                    }
                }

                if (item.Value is null)
                {
                    issues.Add(new ValidationIssue("plan.value.item.required", "A plan collection item is required.", $"values[{index}].value"));
                }
                else if (!string.IsNullOrWhiteSpace(key))
                {
                    normalized.Add(KeyValuePair.Create(key, item.Value));
                }
            }
        }

        var depth = snapshot.IsEmpty
            ? 1
            : 1 + snapshot.Where(item => item.Value is not null).Select(item => item.Value!.Depth).DefaultIfEmpty().Max();
        AddDepthIssue(depth, issues);
        return issues.Count == 0
            ? ValidationResult.Success(new PlanValue(
                PlanValueKind.Map,
                objectValue: normalized.ToImmutableDictionary(StringComparer.Ordinal),
                depth: depth))
            : ValidationResult.Failure<PlanValue>(issues);
    }

    public bool Equals(PlanValue? other)
    {
        if (other is null || Kind != other.Kind)
        {
            return false;
        }

        return Kind switch
        {
            PlanValueKind.Text => StringComparer.Ordinal.Equals(StringValue, other.StringValue),
            PlanValueKind.Boolean => BooleanValue == other.BooleanValue,
            PlanValueKind.WholeNumber => IntegerValue == other.IntegerValue,
            PlanValueKind.Sequence => ArrayValue.SequenceEqual(other.ArrayValue),
            PlanValueKind.Map => ObjectValue.Count == other.ObjectValue.Count &&
                ObjectValue.All(pair => other.ObjectValue.TryGetValue(pair.Key, out var value) && pair.Value.Equals(value)),
            _ => false,
        };
    }

    public override bool Equals(object? obj) => Equals(obj as PlanValue);

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

    private static void AddCollectionIssues(int count, List<ValidationIssue> issues)
    {
        if (count > MaximumCollectionItems)
        {
            issues.Add(new ValidationIssue("plan.value.collection.too-large", "A plan collection exceeds the supported item limit.", "values"));
        }
    }

    private static void AddDepthIssue(int depth, List<ValidationIssue> issues)
    {
        if (depth > MaximumDepth)
        {
            issues.Add(new ValidationIssue("plan.value.depth.exceeded", "A plan value exceeds the supported nesting depth.", "values"));
        }
    }
}
