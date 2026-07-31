using System.Collections.Immutable;
using DevForge.Domain.Validation;

namespace DevForge.Domain.Projects;

public sealed record ProjectRecipeDraft(
    string? Name,
    string? TargetPath,
    string? BlueprintId,
    string? BlueprintVersion,
    IReadOnlyDictionary<string, string?>? Inputs,
    IReadOnlyCollection<string?>? Features,
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

    private ProjectRecipe(
        ProjectRecipeDraft draft,
        ImmutableArray<KeyValuePair<string, string?>> inputs,
        ImmutableArray<string?> features)
    {
        Name = draft.Name!.Trim();
        TargetPath = draft.TargetPath!;
        BlueprintId = draft.BlueprintId!.Trim();
        BlueprintVersion = draft.BlueprintVersion!.Trim();
        Inputs = inputs.ToImmutableDictionary(
            input => input.Key,
            input => input.Value!,
            StringComparer.Ordinal);
        Features = [.. features.Select(feature => feature!)];
        TeamProfile = draft.TeamProfile;
        Git = draft.Git ?? GitOptions.Create().Value;
        Completion = draft.Completion ?? CompletionOptions.Create().Value;
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

    public static ValidationResult<ProjectRecipe> Create(ProjectRecipeDraft? draft)
    {
        if (draft is null)
        {
            return ValidationResult.Failure<ProjectRecipe>(
            [
                new ValidationIssue("project.draft.required", "A project recipe draft is required."),
            ]);
        }

        var issues = new List<ValidationIssue>();
        AddRequiredIssue(issues, draft.Name, "project.name.required", "Project name is required.", "name");
        var inputsSnapshot = draft.Inputs?.ToImmutableArray() ?? [];
        var featuresSnapshot = draft.Features?.ToImmutableArray() ?? [];


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

        if (draft.Inputs is null)
        {
            issues.Add(new ValidationIssue("project.inputs.required", "Recipe inputs are required.", "inputs"));
        }
        else
        {
            foreach (var input in inputsSnapshot)
            {
                if (string.IsNullOrWhiteSpace(input.Key))
                {
                    issues.Add(
                        new ValidationIssue(
                            "project.input.name.required",
                            "A recipe input name is required.",
                            "inputs"));
                }
                else if (IsSecretShaped(input.Key))
                {
                    issues.Add(
                        new ValidationIssue(
                            "project.input.secret-name",
                            "Recipe input names must not describe secrets.",
                            $"inputs.{input.Key}"));
                }

                if (input.Value is null)
                {
                    issues.Add(
                        new ValidationIssue(
                            "project.input.value.required",
                            "A recipe input value is required.",
                            string.IsNullOrWhiteSpace(input.Key) ? "inputs" : $"inputs.{input.Key}"));
                }
            }
        }

        if (draft.Features is null)
        {
            issues.Add(new ValidationIssue("project.features.required", "Recipe features are required.", "features"));
        }
        else
        {
            var index = 0;
            foreach (var feature in featuresSnapshot)
            {
                if (string.IsNullOrWhiteSpace(feature))
                {
                    issues.Add(
                        new ValidationIssue(
                            "project.feature.invalid",
                            "Recipe features cannot contain blank values.",
                            $"features[{index}]"));
                }

                index++;
            }
        }

        return issues.Count == 0
            ? ValidationResult.Success(new ProjectRecipe(draft, inputsSnapshot, featuresSnapshot))
            : ValidationResult.Failure<ProjectRecipe>(issues);
    }

    private static void AddRequiredIssue(
        List<ValidationIssue> issues,
        string? value,
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
