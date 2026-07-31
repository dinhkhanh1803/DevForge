using System.Collections.Immutable;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Privacy;
using DevForge.Domain.Validation;

namespace DevForge.Application.Contracts;

public sealed record BlueprintReference
{
    private BlueprintReference(string id, string version)
    {
        Id = id;
        Version = version;
    }

    public string Id { get; }

    public string Version { get; }

    public static ValidationResult<BlueprintReference> Create(string? id, string? version)
    {
        var issues = new List<ValidationIssue>();
        if (string.IsNullOrWhiteSpace(id))
        {
            issues.Add(
                new ValidationIssue(
                    "blueprint.reference.id.required",
                    "A blueprint identifier is required.",
                    "id"));
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            issues.Add(
                new ValidationIssue(
                    "blueprint.reference.version.required",
                    "A blueprint version is required.",
                    "version"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new BlueprintReference(id!.Trim(), version!.Trim()))
            : ValidationResult.Failure<BlueprintReference>(issues);
    }
}

public sealed class TemplateRenderRequest
{
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
        var contextSnapshot = context?.ToImmutableArray() ?? [];
        var issues = new List<ValidationIssue>();
        if (string.IsNullOrWhiteSpace(template))
        {
            issues.Add(
                new ValidationIssue(
                    "template.value.required",
                    "A template is required.",
                    "template"));
        }

        if (context is null)
        {
            issues.Add(
                new ValidationIssue(
                    "template.context.required",
                    "A template context is required.",
                    "context"));
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < contextSnapshot.Length; index++)
        {
            var variable = contextSnapshot[index];
            if (string.IsNullOrWhiteSpace(variable.Key))
            {
                issues.Add(
                    new ValidationIssue(
                        "template.context.name.required",
                        "A template context name is required.",
                        $"context[{index}].name"));
            }
            else
            {
                var normalizedName = variable.Key.Trim();
                if (!names.Add(normalizedName))
                {
                    issues.Add(
                        new ValidationIssue(
                            "template.context.name.duplicate",
                            "Template context names must be unique.",
                            $"context[{index}].name"));
                }
                else if (RedactedText.IsSecretShapedKey(normalizedName))
                {
                    issues.Add(
                        new ValidationIssue(
                            "template.context.name.secret-shaped",
                            "Template context names cannot describe secrets.",
                            $"context[{index}].name"));
                }
            }

            if (variable.Value is null)
            {
                issues.Add(
                    new ValidationIssue(
                        "template.context.value.required",
                        "A template context value is required.",
                        $"context[{index}].value"));
            }
        }

        if (issues.Count != 0)
        {
            return ValidationResult.Failure<TemplateRenderRequest>(issues);
        }

        var normalizedContext = contextSnapshot.Select(
            variable => KeyValuePair.Create(variable.Key.Trim(), variable.Value!));
        return ValidationResult.Success(
            new TemplateRenderRequest(
                template!,
                normalizedContext.ToImmutableDictionary(StringComparer.Ordinal)));
    }
}

public interface ITemplateRenderer
{
    Task<string> RenderAsync(
        TemplateRenderRequest request,
        CancellationToken cancellationToken);
}

public interface IBlueprintCatalog
{
    Task<ImmutableArray<BlueprintManifest>> ListAsync(CancellationToken cancellationToken);

    Task<BlueprintManifest?> FindAsync(
        BlueprintReference reference,
        CancellationToken cancellationToken);
}
