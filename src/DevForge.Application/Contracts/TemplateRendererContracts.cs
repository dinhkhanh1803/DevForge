using System.Collections.Immutable;
using System.Text.RegularExpressions;
using DevForge.Domain.Privacy;
using DevForge.Domain.Validation;

namespace DevForge.Application.Contracts;

public sealed partial class TemplateRenderRequest
{
    public const int MaxTemplateLength = 1024 * 1024;
    public const int MaxContextEntries = 256;
    public const int MaxContextNameLength = 256;
    public const int MaxContextValueLength = 64 * 1024;
    public const int MaxTotalContextValueLength = 2 * 1024 * 1024;

    private TemplateRenderRequest(
        string template,
        ImmutableDictionary<string, string> context)
    {
        Template = template;
        Context = context;
    }

    public string Template { get; }

    public ImmutableDictionary<string, string> Context { get; }

    public static ValidationResult<TemplateRenderRequest> Create(
        string? template,
        IEnumerable<KeyValuePair<string, string?>>? context)
    {
        var contextSnapshot = SnapshotContext(context);
        var issues = new List<ValidationIssue>();
        ValidateTemplate(template, issues);

        if (context is null)
        {
            issues.Add(
                new ValidationIssue(
                    "template.context.required",
                    "A template context is required.",
                    "context"));
        }
        else
        {
            ValidateContext(contextSnapshot, issues);
        }

        if (issues.Count != 0)
        {
            return ValidationResult.Failure<TemplateRenderRequest>(issues);
        }

        var immutableContext = contextSnapshot
            .Select(variable => KeyValuePair.Create(variable.Key.Trim(), variable.Value!))
            .ToImmutableDictionary(StringComparer.Ordinal);
        return ValidationResult.Success(
            new TemplateRenderRequest(template!, immutableContext));
    }

    private static ImmutableArray<KeyValuePair<string, string?>> SnapshotContext(
        IEnumerable<KeyValuePair<string, string?>>? context)
    {
        if (context is null)
        {
            return [];
        }

        var snapshot = ImmutableArray.CreateBuilder<KeyValuePair<string, string?>>(
            MaxContextEntries + 1);
        using var enumerator = context.GetEnumerator();
        while (snapshot.Count <= MaxContextEntries && enumerator.MoveNext())
        {
            snapshot.Add(enumerator.Current);
        }

        return snapshot.ToImmutable();
    }

    private static void ValidateTemplate(string? template, List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            issues.Add(
                new ValidationIssue(
                    "template.value.required",
                    "A template is required.",
                    "template"));
        }

        if (template is null)
        {
            return;
        }

        if (template.Length > MaxTemplateLength)
        {
            issues.Add(
                new ValidationIssue(
                    "template.value.too-large",
                    "The template exceeds the supported length.",
                    "template"));
        }

        if (template.Contains('\0', StringComparison.Ordinal))
        {
            issues.Add(
                new ValidationIssue(
                    "template.value.null-character",
                    "The template cannot contain null characters.",
                    "template"));
        }
    }

    private static void ValidateContext(
        ImmutableArray<KeyValuePair<string, string?>> context,
        List<ValidationIssue> issues)
    {
        if (context.Length > MaxContextEntries)
        {
            issues.Add(
                new ValidationIssue(
                    "template.context.too-many",
                    "The template context contains too many entries.",
                    "context"));
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        long totalValueLength = 0;
        for (var index = 0; index < context.Length; index++)
        {
            var variable = context[index];
            ValidateName(variable.Key, index, names, issues);
            ValidateValue(variable.Value, index, issues);
            totalValueLength += variable.Value?.Length ?? 0;
        }

        ValidatePathCollisions(names, issues);
        if (totalValueLength > MaxTotalContextValueLength)
        {
            issues.Add(
                new ValidationIssue(
                    "template.context.total.too-large",
                    "The template context exceeds the supported total length.",
                    "context"));
        }
    }

    private static void ValidateName(
        string? name,
        int index,
        HashSet<string> names,
        List<ValidationIssue> issues)
    {
        var location = $"context[{index}].name";
        if (string.IsNullOrWhiteSpace(name))
        {
            issues.Add(
                new ValidationIssue(
                    "template.context.name.required",
                    "A template context name is required.",
                    location));
            return;
        }

        var normalizedName = name.Trim();
        if (normalizedName.Length > MaxContextNameLength)
        {
            issues.Add(
                new ValidationIssue(
                    "template.context.name.too-long",
                    "A template context name exceeds the supported length.",
                    location));
        }

        if (!ContextNamePattern().IsMatch(normalizedName))
        {
            issues.Add(
                new ValidationIssue(
                    "template.context.name.invalid",
                    "A template context name must use dotted identifier segments.",
                    location));
        }

        if (!names.Add(normalizedName))
        {
            issues.Add(
                new ValidationIssue(
                    "template.context.name.duplicate",
                    "Template context names must be unique.",
                    location));
        }

        if (RedactedText.IsSecretShapedKey(normalizedName))
        {
            issues.Add(
                new ValidationIssue(
                    "template.context.name.secret-shaped",
                    "Template context names cannot describe secrets.",
                    location));
        }
    }

    private static void ValidateValue(
        string? value,
        int index,
        List<ValidationIssue> issues)
    {
        var location = $"context[{index}].value";
        if (value is null)
        {
            issues.Add(
                new ValidationIssue(
                    "template.context.value.required",
                    "A template context value is required.",
                    location));
            return;
        }

        if (value.Length > MaxContextValueLength)
        {
            issues.Add(
                new ValidationIssue(
                    "template.context.value.too-large",
                    "A template context value exceeds the supported length.",
                    location));
        }

        if (value.Contains('\0', StringComparison.Ordinal))
        {
            issues.Add(
                new ValidationIssue(
                    "template.context.value.null-character",
                    "A template context value cannot contain null characters.",
                    location));
        }

        if (value.Length <= MaxContextValueLength && RedactedText.IsSecretShapedValue(value))
        {
            issues.Add(
                new ValidationIssue(
                    "template.context.value.secret-shaped",
                    "A template context value resembles credential material.",
                    location));
        }
    }

    private static void ValidatePathCollisions(
        HashSet<string> names,
        List<ValidationIssue> issues)
    {
        var orderedNames = names.Order(StringComparer.Ordinal).ToArray();
        for (var index = 1; index < orderedNames.Length; index++)
        {
            if (orderedNames[index].StartsWith(
                    $"{orderedNames[index - 1]}.",
                    StringComparison.Ordinal))
            {
                issues.Add(
                    new ValidationIssue(
                        "template.context.name.path-conflict",
                        "Template context names cannot be both a value and a parent path.",
                        "context"));
            }
        }
    }

    [GeneratedRegex(
        "^[A-Za-z_][A-Za-z0-9_]*(?:\\.[A-Za-z_][A-Za-z0-9_]*)*$",
        RegexOptions.CultureInvariant,
        100)]
    private static partial Regex ContextNamePattern();
}

public interface ITemplateRenderer
{
    Task<string> RenderAsync(
        TemplateRenderRequest request,
        CancellationToken cancellationToken);
}
