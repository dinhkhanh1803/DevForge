using System.Collections.Immutable;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Execution;
using DevForge.Domain.Privacy;
using DevForge.Domain.Projects;
using DevForge.Domain.Validation;

namespace DevForge.Application.Contracts;

public interface IProjectPlanner
{
    Task<ValidationResult<PlannedProject>> CreatePlanAsync(
        ProjectRecipe recipe,
        CancellationToken cancellationToken);
}

public sealed record PlanPreviewStep(
    string Id,
    string HandlerId,
    TimeSpan Timeout,
    RedactedText? ProcessPreview = null);

public sealed record PlanPreviewValidator(
    string Id,
    string HandlerId,
    TimeSpan Timeout,
    bool Required,
    RedactedText? ProcessPreview = null);

public sealed record PlanPreviewToolStatus(
    string Id,
    string VersionRange,
    bool Required,
    bool IsAvailable,
    bool IsCompatible,
    string? DetectedVersion);

public sealed class PlanPreview
{
    private const string HashPrefix = "sha256:";

    private PlanPreview(
        BlueprintReference blueprint,
        ImmutableArray<PlanPreviewStep> steps,
        ImmutableArray<PlanPreviewValidator> validators,
        ImmutableArray<ToolRequirement> requiredTools,
        ImmutableArray<PlanPreviewToolStatus> toolStatuses,
        ImmutableArray<BlueprintDependency> dependencies,
        ImmutableArray<BlueprintArtifact> artifacts,
        ImmutableArray<ValidationIssue> warnings,
        ImmutableSortedDictionary<string, PlanValue> effectiveInputs,
        ImmutableArray<string> enabledFeatures,
        GitOptions git,
        CompletionOptions completion,
        string planHash)
    {
        Blueprint = blueprint;
        Steps = steps;
        Validators = validators;
        RequiredTools = requiredTools;
        ToolStatuses = toolStatuses;
        Dependencies = dependencies;
        Artifacts = artifacts;
        Warnings = warnings;
        EffectiveInputs = effectiveInputs;
        EnabledFeatures = enabledFeatures;
        Git = git;
        Completion = completion;
        PlanHash = planHash;
    }

    public BlueprintReference Blueprint { get; }

    public ImmutableArray<PlanPreviewStep> Steps { get; }

    public ImmutableArray<PlanPreviewValidator> Validators { get; }

    public ImmutableArray<ToolRequirement> RequiredTools { get; }

    public ImmutableArray<PlanPreviewToolStatus> ToolStatuses { get; }

    public ImmutableArray<BlueprintDependency> Dependencies { get; }

    public ImmutableArray<BlueprintArtifact> Artifacts { get; }

    public ImmutableArray<ValidationIssue> Warnings { get; }

    public ImmutableSortedDictionary<string, PlanValue> EffectiveInputs { get; }

    public ImmutableArray<string> EnabledFeatures { get; }

    public GitOptions Git { get; }

    public CompletionOptions Completion { get; }

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
        var toolSnapshot = requiredTools?.ToImmutableArray();
        var statuses = toolSnapshot?.Where(tool => tool is not null).Select(tool =>
            new PlanPreviewToolStatus(
                tool!.Id,
                tool.VersionRange,
                tool.Required,
                IsAvailable: false,
                IsCompatible: false,
                DetectedVersion: null));
        return Create(
            blueprint,
            steps,
            [],
            toolSnapshot,
            statuses,
            dependencies,
            artifacts,
            warnings,
            [],
            [],
            GitOptions.Create().Value,
            CompletionOptions.Create().Value,
            planHash);
    }

    public static ValidationResult<PlanPreview> Create(
        BlueprintReference? blueprint,
        IEnumerable<PlanPreviewStep?>? steps,
        IEnumerable<PlanPreviewValidator?>? validators,
        IEnumerable<ToolRequirement?>? requiredTools,
        IEnumerable<PlanPreviewToolStatus?>? toolStatuses,
        IEnumerable<BlueprintDependency?>? dependencies,
        IEnumerable<BlueprintArtifact?>? artifacts,
        IEnumerable<ValidationIssue?>? warnings,
        IEnumerable<KeyValuePair<string, PlanValue?>>? effectiveInputs,
        IEnumerable<string?>? enabledFeatures,
        GitOptions? git,
        CompletionOptions? completion,
        string? planHash)
    {
        var stepSnapshot = steps?.ToImmutableArray() ?? [];
        var validatorSnapshot = validators?.ToImmutableArray() ?? [];
        var toolSnapshot = requiredTools?.ToImmutableArray() ?? [];
        var toolStatusSnapshot = toolStatuses?.ToImmutableArray() ?? [];
        var dependencySnapshot = dependencies?.ToImmutableArray() ?? [];
        var artifactSnapshot = artifacts?.ToImmutableArray() ?? [];
        var warningSnapshot = warnings?.ToImmutableArray() ?? [];
        var inputSnapshot = effectiveInputs?.ToImmutableArray() ?? [];
        var featureSnapshot = enabledFeatures?.ToImmutableArray() ?? [];
        var issues = new List<ValidationIssue>();
        if (blueprint is null)
        {
            issues.Add(new ValidationIssue(
                "plan.preview.blueprint.required",
                "A blueprint reference is required for plan preview.",
                "blueprint"));
        }

        ValidateSteps(steps, stepSnapshot, issues);
        ValidateValidators(validators, validatorSnapshot, issues);
        AddCollectionIssues(requiredTools, toolSnapshot, "required-tools", "requiredTools", issues);
        ValidateToolStatuses(toolStatuses, toolStatusSnapshot, issues);
        if (toolSnapshot.All(item => item is not null)
            && toolStatusSnapshot.All(item => item is not null)
            && (toolSnapshot.Length != toolStatusSnapshot.Length
                || toolSnapshot.Select((tool, index) => (Tool: tool!, Status: toolStatusSnapshot[index]!))
                    .Any(item =>
                        !StringComparer.Ordinal.Equals(item.Tool.Id, item.Status.Id)
                        || !StringComparer.Ordinal.Equals(
                            item.Tool.VersionRange,
                            item.Status.VersionRange)
                        || item.Tool.Required != item.Status.Required)))
        {
            issues.Add(new ValidationIssue(
                "plan.preview.tool-status.mismatch",
                "Tool preview statuses must exactly match declared requirements.",
                "toolStatuses"));
        }
        AddCollectionIssues(dependencies, dependencySnapshot, "dependencies", "dependencies", issues);
        AddCollectionIssues(artifacts, artifactSnapshot, "artifacts", "artifacts", issues);
        AddCollectionIssues(warnings, warningSnapshot, "warnings", "warnings", issues);
        ValidateEffectiveInputs(effectiveInputs, inputSnapshot, issues);
        ValidateFeatures(enabledFeatures, featureSnapshot, issues);
        if (git is null)
        {
            issues.Add(new ValidationIssue(
                "plan.preview.git.required",
                "A Git intent summary is required.",
                "git"));
        }

        if (completion is null)
        {
            issues.Add(new ValidationIssue(
                "plan.preview.completion.required",
                "A completion intent summary is required.",
                "completion"));
        }
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
                [.. validatorSnapshot.Select(item => item!)],
                [.. toolSnapshot.Select(item => item!)],
                [.. toolStatusSnapshot.Select(item => item!)],
                [.. dependencySnapshot.Select(item => item!)],
                [.. artifactSnapshot.Select(item => item!)],
                [.. warningSnapshot.Select(item => item!)],
                inputSnapshot.ToImmutableSortedDictionary(
                    item => item.Key.Trim(),
                    item => item.Value!,
                    StringComparer.Ordinal),
                [.. featureSnapshot.Select(item => item!.Trim())],
                git!,
                completion!,
                planHash!))
            : ValidationResult.Failure<PlanPreview>(issues);
    }

    private static void ValidateToolStatuses(
        IEnumerable<PlanPreviewToolStatus?>? source,
        ImmutableArray<PlanPreviewToolStatus?> snapshot,
        List<ValidationIssue> issues)
    {
        AddCollectionIssues(source, snapshot, "tool-statuses", "toolStatuses", issues);
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < snapshot.Length; index++)
        {
            var status = snapshot[index];
            if (status is null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(status.Id)
                || string.IsNullOrWhiteSpace(status.VersionRange)
                || !identifiers.Add(status.Id.Trim())
                || !status.IsAvailable && status.IsCompatible
                || status.DetectedVersion is not null
                    && !SemanticVersion.TryParse(status.DetectedVersion, out _))
            {
                issues.Add(new ValidationIssue(
                    "plan.preview.tool-status.invalid",
                    "A plan preview tool status is invalid.",
                    $"toolStatuses[{index}]"));
            }
        }
    }

    private static void ValidateValidators(
        IEnumerable<PlanPreviewValidator?>? source,
        ImmutableArray<PlanPreviewValidator?> snapshot,
        List<ValidationIssue> issues)
    {
        AddCollectionIssues(source, snapshot, "validators", "validators", issues);
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < snapshot.Length; index++)
        {
            var validator = snapshot[index];
            if (validator is null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(validator.Id)
                || !identifiers.Add(validator.Id.Trim()))
            {
                issues.Add(new ValidationIssue(
                    "plan.preview.validator.id.invalid",
                    "A unique plan preview validator identifier is required.",
                    $"validators[{index}].id"));
            }

            if (string.IsNullOrWhiteSpace(validator.HandlerId))
            {
                issues.Add(new ValidationIssue(
                    "plan.preview.validator.handler.required",
                    "A plan preview validator handler is required.",
                    $"validators[{index}].handlerId"));
            }

            if (validator.Timeout <= TimeSpan.Zero)
            {
                issues.Add(new ValidationIssue(
                    "plan.preview.validator.timeout.invalid",
                    "A plan preview validator timeout must be positive.",
                    $"validators[{index}].timeout"));
            }
        }
    }

    private static void ValidateEffectiveInputs(
        IEnumerable<KeyValuePair<string, PlanValue?>>? source,
        ImmutableArray<KeyValuePair<string, PlanValue?>> snapshot,
        List<ValidationIssue> issues)
    {
        if (source is null)
        {
            issues.Add(new ValidationIssue(
                "plan.preview.inputs.required",
                "Effective plan inputs are required.",
                "effectiveInputs"));
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < snapshot.Length; index++)
        {
            var item = snapshot[index];
            if (string.IsNullOrWhiteSpace(item.Key)
                || item.Value is null
                || !keys.Add(item.Key.Trim()))
            {
                issues.Add(new ValidationIssue(
                    "plan.preview.input.invalid",
                    "Effective plan inputs must have unique names and typed values.",
                    $"effectiveInputs[{index}]"));
            }
        }
    }

    private static void ValidateFeatures(
        IEnumerable<string?>? source,
        ImmutableArray<string?> snapshot,
        List<ValidationIssue> issues)
    {
        if (source is null)
        {
            issues.Add(new ValidationIssue(
                "plan.preview.features.required",
                "Enabled plan features are required.",
                "enabledFeatures"));
        }

        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < snapshot.Length; index++)
        {
            var feature = snapshot[index];
            if (string.IsNullOrWhiteSpace(feature) || !identifiers.Add(feature.Trim()))
            {
                issues.Add(new ValidationIssue(
                    "plan.preview.feature.invalid",
                    "Enabled plan features must have unique identifiers.",
                    $"enabledFeatures[{index}]"));
            }
        }
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
    private PlannedProject(
        ExecutionPlan plan,
        PlanPreview preview,
        BlueprintFingerprint blueprintFingerprint)
    {
        Plan = plan;
        Preview = preview;
        BlueprintFingerprint = blueprintFingerprint;
    }

    public ExecutionPlan Plan { get; }

    public PlanPreview Preview { get; }

    public BlueprintFingerprint BlueprintFingerprint { get; }

    public static ValidationResult<PlannedProject> Create(
        ExecutionPlan? plan,
        PlanPreview? preview,
        BlueprintFingerprint? blueprintFingerprint)
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

        if (blueprintFingerprint is null)
        {
            issues.Add(new ValidationIssue(
                "planned-project.blueprint-fingerprint.required",
                "The exact blueprint fingerprint is required.",
                "blueprintFingerprint"));
        }

        if (plan is not null
            && preview is not null
            && !StringComparer.Ordinal.Equals(plan.Id, preview.PlanHash))
        {
            issues.Add(new ValidationIssue(
                "planned-project.hash.mismatch",
                "The execution plan and preview hashes must match exactly.",
                "preview.planHash"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new PlannedProject(plan!, preview!, blueprintFingerprint!))
            : ValidationResult.Failure<PlannedProject>(issues);
    }
}
