using System.Collections.Immutable;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Execution;
using DevForge.Domain.Projects;
using DevForge.Domain.Validation;

namespace DevForge.Application.Contracts;

public interface IProjectPlanner
{
    Task<ValidationResult<PlannedProject>> CreatePlanAsync(
        ProjectRecipe recipe,
        CancellationToken cancellationToken);
}

public sealed record PlanPreviewStep(string Id, string HandlerId, TimeSpan Timeout);

public sealed class PlanPreview
{
    private const string HashPrefix = "sha256:";

    private PlanPreview(
        BlueprintReference blueprint,
        ImmutableArray<PlanPreviewStep> steps,
        ImmutableArray<ToolRequirement> requiredTools,
        ImmutableArray<BlueprintDependency> dependencies,
        ImmutableArray<BlueprintArtifact> artifacts,
        ImmutableArray<ValidationIssue> warnings,
        string planHash)
    {
        Blueprint = blueprint;
        Steps = steps;
        RequiredTools = requiredTools;
        Dependencies = dependencies;
        Artifacts = artifacts;
        Warnings = warnings;
        PlanHash = planHash;
    }

    public BlueprintReference Blueprint { get; }

    public ImmutableArray<PlanPreviewStep> Steps { get; }

    public ImmutableArray<ToolRequirement> RequiredTools { get; }

    public ImmutableArray<BlueprintDependency> Dependencies { get; }

    public ImmutableArray<BlueprintArtifact> Artifacts { get; }

    public ImmutableArray<ValidationIssue> Warnings { get; }

    public string PlanHash { get; }

    public static ValidationResult<PlanPreview> Create(
        BlueprintReference? blueprint,
        IEnumerable<PlanPreviewStep?>? steps,
        IEnumerable<ToolRequirement?>? requiredTools,
        IEnumerable<BlueprintDependency?>? dependencies,
        IEnumerable<BlueprintArtifact?>? artifacts,
        IEnumerable<ValidationIssue?>? warnings,
        string? planHash)
    {
        var stepSnapshot = steps?.ToImmutableArray() ?? [];
        var toolSnapshot = requiredTools?.ToImmutableArray() ?? [];
        var dependencySnapshot = dependencies?.ToImmutableArray() ?? [];
        var artifactSnapshot = artifacts?.ToImmutableArray() ?? [];
        var warningSnapshot = warnings?.ToImmutableArray() ?? [];
        var issues = new List<ValidationIssue>();
        if (blueprint is null)
        {
            issues.Add(new ValidationIssue(
                "plan.preview.blueprint.required",
                "A blueprint reference is required for plan preview.",
                "blueprint"));
        }

        ValidateSteps(steps, stepSnapshot, issues);
        AddCollectionIssues(requiredTools, toolSnapshot, "required-tools", "requiredTools", issues);
        AddCollectionIssues(dependencies, dependencySnapshot, "dependencies", "dependencies", issues);
        AddCollectionIssues(artifacts, artifactSnapshot, "artifacts", "artifacts", issues);
        AddCollectionIssues(warnings, warningSnapshot, "warnings", "warnings", issues);
        if (!IsHash(planHash))
        {
            issues.Add(new ValidationIssue(
                "plan.preview.hash.invalid",
                "A lowercase SHA-256 plan hash is required.",
                "planHash"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new PlanPreview(
                blueprint!,
                [.. stepSnapshot.Select(item => item!)],
                [.. toolSnapshot.Select(item => item!)],
                [.. dependencySnapshot.Select(item => item!)],
                [.. artifactSnapshot.Select(item => item!)],
                [.. warningSnapshot.Select(item => item!)],
                planHash!))
            : ValidationResult.Failure<PlanPreview>(issues);
    }

    private static void ValidateSteps(
        IEnumerable<PlanPreviewStep?>? source,
        ImmutableArray<PlanPreviewStep?> snapshot,
        List<ValidationIssue> issues)
    {
        AddCollectionIssues(source, snapshot, "steps", "steps", issues);
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < snapshot.Length; index++)
        {
            var step = snapshot[index];
            if (step is null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(step.Id))
            {
                issues.Add(new ValidationIssue(
                    "plan.preview.step.id.required",
                    "A plan preview step identifier is required.",
                    $"steps[{index}].id"));
            }
            else if (!identifiers.Add(step.Id.Trim()))
            {
                issues.Add(new ValidationIssue(
                    "plan.preview.step.id.duplicate",
                    "Plan preview step identifiers must be unique.",
                    $"steps[{index}].id"));
            }

            if (string.IsNullOrWhiteSpace(step.HandlerId))
            {
                issues.Add(new ValidationIssue(
                    "plan.preview.step.handler.required",
                    "A plan preview step handler is required.",
                    $"steps[{index}].handlerId"));
            }

            if (step.Timeout <= TimeSpan.Zero)
            {
                issues.Add(new ValidationIssue(
                    "plan.preview.step.timeout.invalid",
                    "A plan preview step timeout must be positive.",
                    $"steps[{index}].timeout"));
            }
        }
    }

    private static void AddCollectionIssues<T>(
        IEnumerable<T?>? source,
        ImmutableArray<T?> snapshot,
        string codePart,
        string location,
        List<ValidationIssue> issues)
        where T : class
    {
        if (source is null)
        {
            issues.Add(new ValidationIssue(
                $"plan.preview.{codePart}.required",
                "A plan preview collection is required.",
                location));
        }

        for (var index = 0; index < snapshot.Length; index++)
        {
            if (snapshot[index] is null)
            {
                issues.Add(new ValidationIssue(
                    $"plan.preview.{codePart}.item.required",
                    "A plan preview collection item is required.",
                    $"{location}[{index}]"));
            }
        }
    }

    private static bool IsHash(string? value)
    {
        return value is not null
            && value.Length == HashPrefix.Length + 64
            && value.StartsWith(HashPrefix, StringComparison.Ordinal)
            && value.AsSpan(HashPrefix.Length).ToArray().All(character =>
                character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }
}

public sealed class PlannedProject
{
    private PlannedProject(ExecutionPlan plan, PlanPreview preview)
    {
        Plan = plan;
        Preview = preview;
    }

    public ExecutionPlan Plan { get; }

    public PlanPreview Preview { get; }

    public static ValidationResult<PlannedProject> Create(
        ExecutionPlan? plan,
        PlanPreview? preview)
    {
        var issues = new List<ValidationIssue>();
        if (plan is null)
        {
            issues.Add(new ValidationIssue(
                "planned-project.plan.required",
                "An execution plan is required.",
                "plan"));
        }

        if (preview is null)
        {
            issues.Add(new ValidationIssue(
                "planned-project.preview.required",
                "A plan preview is required.",
                "preview"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new PlannedProject(plan!, preview!))
            : ValidationResult.Failure<PlannedProject>(issues);
    }
}
