using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Blueprints.Abstractions.Validation;
using DevForge.Domain.Execution;
using DevForge.Domain.Privacy;
using DevForge.Domain.Validation;

namespace DevForge.Application.Planning;

public sealed class PlanningVariableValue
{
    private PlanningVariableValue(PlanValue? value, string? placeholderIdentifier)
    {
        Value = value;
        PlaceholderIdentifier = placeholderIdentifier;
    }

    public PlanValue? Value { get; }

    public string? PlaceholderIdentifier { get; }

    public bool IsPlaceholder => PlaceholderIdentifier is not null;

    public static ValidationResult<PlanningVariableValue> FromValue(PlanValue? value)
    {
        return value is not null
            ? ValidationResult.Success(new PlanningVariableValue(value, null))
            : Failure<PlanningVariableValue>();
    }

    public static ValidationResult<PlanningVariableValue> Placeholder(string? identifier)
    {
        return PlanningVariableIdentifierPolicy.IsPlaceholder(identifier)
            ? ValidationResult.Success(new PlanningVariableValue(null, identifier))
            : Failure<PlanningVariableValue>();
    }

    private static ValidationResult<T> Failure<T>()
    {
        return ValidationResult.Failure<T>(
        [
            new ValidationIssue(
                "DF-PLAN-001",
                "A valid typed planning variable is required.",
                "value"),
        ]);
    }
}

public sealed class PlanningVariableContext
{
    private readonly ImmutableDictionary<string, PlanningVariableValue> _values;

    private PlanningVariableContext(ImmutableDictionary<string, PlanningVariableValue> values)
    {
        _values = values;
    }

    public static ValidationResult<PlanningVariableContext> Create(
        IEnumerable<KeyValuePair<string, PlanningVariableValue?>>? values)
    {
        var snapshot = values?.ToImmutableArray() ?? [];
        var normalized = ImmutableDictionary.CreateBuilder<string, PlanningVariableValue>(
            StringComparer.Ordinal);
        var issues = new List<ValidationIssue>();
        if (values is null)
        {
            issues.Add(Issue());
        }

        if (snapshot.Length > BlueprintValue.MaximumCollectionItems)
        {
            issues.Add(Issue());
        }

        foreach (var item in snapshot)
        {
            if (!PlanningVariableIdentifierPolicy.IsAllowed(item.Key)
                || item.Value is null
                || item.Value.IsPlaceholder
                    && !StringComparer.Ordinal.Equals(item.Key, item.Value.PlaceholderIdentifier)
                || !item.Value.IsPlaceholder
                    && !PlanningVariableIdentifierPolicy.AcceptsValue(item.Key, item.Value.Value!)
                || !normalized.TryAdd(item.Key, item.Value))
            {
                issues.Add(Issue());
            }
        }

        return issues.Count == 0
            ? ValidationResult.Success(new PlanningVariableContext(normalized.ToImmutable()))
            : ValidationResult.Failure<PlanningVariableContext>(issues);
    }

    internal bool TryGetValue(string identifier, out PlanningVariableValue value)
    {
        return _values.TryGetValue(identifier, out value!);
    }

    internal ValidationResult<ImmutableSortedDictionary<string, string>> CreateTemplateContext()
    {
        var context = ImmutableSortedDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (var item in _values.Where(item => !item.Value.IsPlaceholder))
        {
            var name = string.Join('.', item.Key.Split('.').Select(segment => segment.Replace('-', '_')));
            if (!context.TryAdd(name, FormatScalar(item.Value.Value!)))
            {
                return ValidationResult.Failure<ImmutableSortedDictionary<string, string>>(
                [
                    new ValidationIssue(
                        "DF-PLAN-001",
                        "The deterministic template context contains conflicting aliases.",
                        "templateContext"),
                ]);
            }
        }

        return ValidationResult.Success(context.ToImmutable());
    }

    private static string FormatScalar(PlanValue value)
    {
        return value.Kind switch
        {
            PlanValueKind.Text => value.StringValue!,
            PlanValueKind.Boolean => value.BooleanValue ? "true" : "false",
            PlanValueKind.WholeNumber => value.IntegerValue.ToString(CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException("Planning template context values must be scalar."),
        };
    }

    private static ValidationIssue Issue()
    {
        return new ValidationIssue(
            "DF-PLAN-001",
            "A planning variable context entry is invalid or duplicated.",
            "values");
    }
}

public interface IVariableTemplateResolver
{
    ValidationResult<PlanValue> Resolve(
        BlueprintValue? templateValue,
        PlanningVariableContext? context,
        CancellationToken cancellationToken = default);
}

public sealed class VariableTemplateResolver : IVariableTemplateResolver
{
    public const int MaximumTokens = 512;
    public const int MaximumResolvedTextCharacters = BlueprintValue.MaximumTextLength;

    public ValidationResult<PlanValue> Resolve(
        BlueprintValue? templateValue,
        PlanningVariableContext? context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (templateValue is null || context is null)
        {
            return Failure();
        }

        try
        {
            return ValidationResult.Success(ResolveValue(templateValue, context, cancellationToken));
        }
        catch (VariableResolutionException)
        {
            return Failure();
        }
    }

    private static PlanValue ResolveValue(
        BlueprintValue template,
        PlanningVariableContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return template.Kind switch
        {
            BlueprintValueKind.Text => ResolveText(template.StringValue!, context),
            BlueprintValueKind.Boolean => PlanValue.FromBoolean(template.BooleanValue),
            BlueprintValueKind.WholeNumber => PlanValue.FromInteger(template.IntegerValue),
            BlueprintValueKind.Sequence => ResolveArray(template.ArrayValue, context, cancellationToken),
            BlueprintValueKind.Map => ResolveMap(template.ObjectValue, context, cancellationToken),
            _ => throw new VariableResolutionException(),
        };
    }

    private static PlanValue ResolveText(string template, PlanningVariableContext context)
    {
        var tokens = Tokenize(template);
        if (tokens.Count == 1
            && tokens[0].Start == 0
            && tokens[0].Length == template.Length)
        {
            var value = ResolveVariable(tokens[0].Identifier, context);
            return value.IsPlaceholder
                ? CreatePlaceholder(value.PlaceholderIdentifier!)
                : value.Value!;
        }

        var output = new StringBuilder(template.Length);
        var position = 0;
        foreach (var token in tokens)
        {
            output.Append(template, position, token.Start - position);
            var value = ResolveVariable(token.Identifier, context);
            if (value.IsPlaceholder || value.Value!.Kind is PlanValueKind.Sequence or PlanValueKind.Map)
            {
                throw new VariableResolutionException();
            }

            output.Append(FormatScalar(value.Value));
            if (output.Length > MaximumResolvedTextCharacters)
            {
                throw new VariableResolutionException();
            }

            position = token.Start + token.Length;
        }

        output.Append(template, position, template.Length - position);
        if (output.Length > MaximumResolvedTextCharacters)
        {
            throw new VariableResolutionException();
        }

        var result = PlanValue.FromString(output.ToString());
        return result.IsValid ? result.Value : throw new VariableResolutionException();
    }

    private static PlanValue ResolveArray(
        ImmutableArray<BlueprintValue> values,
        PlanningVariableContext context,
        CancellationToken cancellationToken)
    {
        var resolved = values.Select(value =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ResolveValue(value, context, cancellationToken);
        });
        var result = PlanValue.FromArray(resolved);
        return result.IsValid ? result.Value : throw new VariableResolutionException();
    }

    private static PlanValue ResolveMap(
        ImmutableDictionary<string, BlueprintValue> values,
        PlanningVariableContext context,
        CancellationToken cancellationToken)
    {
        var resolved = values.Select(item =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return KeyValuePair.Create<string, PlanValue?>(
                item.Key,
                ResolveValue(item.Value, context, cancellationToken));
        });
        var result = PlanValue.FromObject(resolved);
        return result.IsValid ? result.Value : throw new VariableResolutionException();
    }

    private static PlanningVariableValue ResolveVariable(
        string identifier,
        PlanningVariableContext context)
    {
        if (!PlanningVariableIdentifierPolicy.IsAllowed(identifier)
            || !context.TryGetValue(identifier, out var value))
        {
            throw new VariableResolutionException();
        }

        return value;
    }

    private static PlanValue CreatePlaceholder(string identifier)
    {
        var text = PlanValue.FromString(identifier);
        var result = PlanValue.FromObject(
        [
            KeyValuePair.Create<string, PlanValue?>("placeholder", text.Value),
        ]);
        return result.IsValid ? result.Value : throw new VariableResolutionException();
    }

    private static string FormatScalar(PlanValue value)
    {
        return value.Kind switch
        {
            PlanValueKind.Text => value.StringValue!,
            PlanValueKind.Boolean => value.BooleanValue ? "true" : "false",
            PlanValueKind.WholeNumber => value.IntegerValue.ToString(CultureInfo.InvariantCulture),
            _ => throw new VariableResolutionException(),
        };
    }

    private static List<VariableToken> Tokenize(string value)
    {
        var tokens = new List<VariableToken>();
        var position = 0;
        while (position < value.Length)
        {
            var open = value.IndexOf("{{", position, StringComparison.Ordinal);
            var strayClose = value.IndexOf("}}", position, StringComparison.Ordinal);
            if (strayClose >= 0 && (open < 0 || strayClose < open))
            {
                throw new VariableResolutionException();
            }

            if (open < 0)
            {
                break;
            }

            var close = value.IndexOf("}}", open + 2, StringComparison.Ordinal);
            if (close < 0)
            {
                throw new VariableResolutionException();
            }

            var identifier = value[(open + 2)..close].Trim();
            if (!PlanningVariableIdentifierPolicy.IsAllowed(identifier)
                || identifier.Contains('{')
                || identifier.Contains('}'))
            {
                throw new VariableResolutionException();
            }

            tokens.Add(new VariableToken(open, close + 2 - open, identifier));
            if (tokens.Count > MaximumTokens)
            {
                throw new VariableResolutionException();
            }

            position = close + 2;
        }

        if (value.IndexOf("{{", position, StringComparison.Ordinal) >= 0
            || value.IndexOf("}}", position, StringComparison.Ordinal) >= 0)
        {
            throw new VariableResolutionException();
        }

        return tokens;
    }

    private static ValidationResult<PlanValue> Failure()
    {
        return ValidationResult.Failure<PlanValue>(
        [
            new ValidationIssue(
                "DF-PLAN-001",
                "A planning variable template is malformed, unavailable, or unsafe.",
                "template"),
        ]);
    }

    private sealed record VariableToken(int Start, int Length, string Identifier);

    private sealed class VariableResolutionException : Exception;
}

internal static class PlanningVariableIdentifierPolicy
{
    private static readonly ImmutableHashSet<string> _known =
        new[]
        {
            "project.name",
            "project.safe-name",
            "project.target-path",
            "blueprint.id",
            "blueprint.version",
            "engine.version",
            "team.company-name",
            "team.root-namespace",
            "team.package-manager",
            "git.primary-branch",
            "git.develop-branch",
            "runtime.staging-path",
            "runtime.run-id",
        }.ToImmutableHashSet(StringComparer.Ordinal);

    internal static bool IsAllowed(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier)
            || identifier != identifier.Trim()
            || RedactedText.IsSecretShapedKey(identifier))
        {
            return false;
        }

        return _known.Contains(identifier)
            || HasDynamic(identifier, "recipe.input.")
            || HasDynamic(identifier, "recipe.feature.");
    }

    internal static bool IsPlaceholder(string? identifier)
    {
        return identifier is "project.target-path" or "runtime.staging-path" or "runtime.run-id";
    }

    internal static bool AcceptsValue(string identifier, PlanValue value)
    {
        if (IsPlaceholder(identifier))
        {
            return false;
        }

        if (value.Kind == PlanValueKind.Text
            && value.StringValue!.Length > BlueprintValue.MaximumTextLength)
        {
            return false;
        }

        if (HasDynamic(identifier, "recipe.input."))
        {
            return value.Kind is PlanValueKind.Text or PlanValueKind.Boolean or PlanValueKind.WholeNumber;
        }

        if (HasDynamic(identifier, "recipe.feature."))
        {
            return value.Kind == PlanValueKind.Boolean;
        }

        return value.Kind == PlanValueKind.Text;
    }

    private static bool HasDynamic(string identifier, string prefix)
    {
        return identifier.StartsWith(prefix, StringComparison.Ordinal)
            && BlueprintIdentifierValidator.IsValid(identifier[prefix.Length..]);
    }
}
