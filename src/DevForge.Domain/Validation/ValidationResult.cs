using System.Collections.Immutable;

namespace DevForge.Domain.Validation;

public sealed class ValidationResult<T>
{
    private readonly T? _value;

    internal ValidationResult(T value)
    {
        _value = value;
        Issues = [];
    }

    internal ValidationResult(IEnumerable<ValidationIssue> issues)
    {
        Issues = [.. issues];
    }

    public bool IsValid => Issues.IsEmpty;

    public ImmutableArray<ValidationIssue> Issues { get; }

    public T Value => IsValid
        ? _value!
        : throw new InvalidOperationException("A failed validation result does not contain a value.");
}

public static class ValidationResult
{
    public static ValidationResult<T> Success<T>(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new ValidationResult<T>(value);
    }

    public static ValidationResult<T> Failure<T>(IEnumerable<ValidationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        var snapshot = issues.ToImmutableArray();
        if (snapshot.IsEmpty)
        {
            throw new ArgumentException("A failed validation result requires at least one issue.", nameof(issues));
        }

        return new ValidationResult<T>(snapshot);
    }
}
