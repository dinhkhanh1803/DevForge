using System.Collections.Immutable;
using DevForge.Blueprints.Abstractions.Models;
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

public interface IBlueprintCatalog
{
    Task<ImmutableArray<BlueprintManifest>> ListAsync(CancellationToken cancellationToken);

    Task<BlueprintManifest?> FindAsync(
        BlueprintReference reference,
        CancellationToken cancellationToken);
}
