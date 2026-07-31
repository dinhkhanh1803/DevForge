using System.Collections.Immutable;

namespace DevForge.Blueprints.Abstractions.Validation;

public sealed class BlueprintValidationResult<T>
{
    private readonly T? _value;

    internal BlueprintValidationResult(T value)
    {
        _value = value;
        Issues = [];
    }

    internal BlueprintValidationResult(ImmutableArray<BlueprintValidationIssue> issues)
    {
        Issues = issues;
    }

    public bool IsValid => Issues.IsEmpty;

    public ImmutableArray<BlueprintValidationIssue> Issues { get; }

    public T Value => IsValid
        ? _value!
        : throw new InvalidOperationException(
            "A failed blueprint validation result does not contain a value.");
}

public static class BlueprintValidationResult
{
    public static BlueprintValidationResult<T> Success<T>(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new BlueprintValidationResult<T>(value);
    }

    public static BlueprintValidationResult<T> Failure<T>(
        IEnumerable<BlueprintValidationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        var snapshot = issues.ToImmutableArray();
        if (snapshot.Any(issue => issue is null))
        {
            throw new ArgumentException(
                "Blueprint validation issues cannot contain null values.",
                nameof(issues));
        }

        if (snapshot.IsEmpty)
        {
            throw new ArgumentException(
                "A failed blueprint validation result requires at least one issue.",
                nameof(issues));
        }

        return new BlueprintValidationResult<T>(snapshot);
    }
}
