using System.Collections.Immutable;
using DevForge.Domain.Validation;

namespace DevForge.Domain.Projects;

public sealed record ProjectRecipeDraft(
    string Name,
    string TargetPath,
    string BlueprintId,
    string BlueprintVersion,
    IReadOnlyDictionary<string, string> Inputs,
    IReadOnlyCollection<string> Features,
    TeamProfile? TeamProfile = null,
    GitOptions? Git = null,
    CompletionOptions? Completion = null);

public sealed class ProjectRecipe
{
    private static readonly string[] _secretNameFragments =
    [
        "apikey",
        "connectionstring",
        "credential",
        "password",
        "privatekey",
        "secret",
        "token",
    ];

    private ProjectRecipe(ProjectRecipeDraft draft)
    {
        Name = draft.Name.Trim();
        TargetPath = draft.TargetPath;
        BlueprintId = draft.BlueprintId.Trim();
        BlueprintVersion = draft.BlueprintVersion.Trim();
        Inputs = draft.Inputs.ToImmutableDictionary(StringComparer.Ordinal);
        Features = [.. draft.Features];
        TeamProfile = draft.TeamProfile;
        Git = draft.Git ?? new GitOptions();
        Completion = draft.Completion ?? new CompletionOptions();
    }

    public string Name { get; }

    public string TargetPath { get; }

    public string BlueprintId { get; }

    public string BlueprintVersion { get; }

    public ImmutableDictionary<string, string> Inputs { get; }

    public ImmutableArray<string> Features { get; }

    public TeamProfile? TeamProfile { get; }

    public GitOptions Git { get; }

    public CompletionOptions Completion { get; }

    public static ValidationResult<ProjectRecipe> Create(ProjectRecipeDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var issues = new List<ValidationIssue>();
        AddRequiredIssue(issues, draft.Name, "project.name.required", "Project name is required.", "name");

        if (string.IsNullOrWhiteSpace(draft.TargetPath) || !Path.IsPathFullyQualified(draft.TargetPath))
        {
            issues.Add(
                new ValidationIssue(
                    "project.target.absolute",
                    "The target path must be absolute.",
                    "targetPath"));
        }

        AddRequiredIssue(
            issues,
            draft.BlueprintId,
            "blueprint.id.required",
            "Blueprint identifier is required.",
            "blueprintId");
        AddRequiredIssue(
            issues,
            draft.BlueprintVersion,
            "blueprint.version.required",
            "Blueprint version is required.",
            "blueprintVersion");

        foreach (var inputName in draft.Inputs.Keys)
        {
            if (IsSecretShaped(inputName))
            {
                issues.Add(
                    new ValidationIssue(
                        "project.input.secret-name",
                        "Recipe input names must not describe secrets.",
                        $"inputs.{inputName}"));
            }
        }

        return issues.Count == 0
            ? ValidationResult.Success(new ProjectRecipe(draft))
            : ValidationResult.Failure<ProjectRecipe>(issues);
    }

    private static void AddRequiredIssue(
        List<ValidationIssue> issues,
        string value,
        string code,
        string message,
        string location)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(new ValidationIssue(code, message, location));
        }
    }

    private static bool IsSecretShaped(string inputName)
    {
        var normalized = string.Concat(inputName.Where(char.IsLetterOrDigit));
        return _secretNameFragments.Any(
            fragment => normalized.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }
}
