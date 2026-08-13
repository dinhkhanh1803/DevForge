using System.Collections.Immutable;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Blueprints.Abstractions.Validation;
using DevForge.Domain.Privacy;
using DevForge.Domain.Projects;
using DevForge.Domain.Validation;

namespace DevForge.Application.Contracts;

public enum ProjectCreationStage
{
    Configure = 1,
    ReviewPlan = 2,
    Execute = 3,
    LocalReady = 4,
}

public enum DynamicInputValueKind
{
    Text = 1,
    Boolean = 2,
    WholeNumber = 3,
}

public sealed class DynamicInputValue
{
    private const int MaximumTextLength = 4_096;

    private DynamicInputValue(
        DynamicInputValueKind kind,
        string? textValue,
        bool booleanValue,
        long wholeNumberValue)
    {
        Kind = kind;
        TextValue = textValue;
        BooleanValue = booleanValue;
        WholeNumberValue = wholeNumberValue;
    }

    public DynamicInputValueKind Kind { get; }

    public string? TextValue { get; }

    public bool BooleanValue { get; }

    public long WholeNumberValue { get; }

    public static ValidationResult<DynamicInputValue> Text(string? value)
    {
        var issues = new List<ValidationIssue>();
        if (value is null)
        {
            issues.Add(new ValidationIssue(
                "creation.input.text.required",
                "A text input value is required.",
                "value"));
        }
        else
        {
            if (value.Length > MaximumTextLength)
            {
                issues.Add(new ValidationIssue(
                    "creation.input.text.too-long",
                    "A text input value exceeds the supported length.",
                    "value"));
            }

            if (value.Any(char.IsControl))
            {
                issues.Add(new ValidationIssue(
                    "creation.input.text.control-character",
                    "A text input value contains an unsupported control character.",
                    "value"));
            }

            if (RedactedText.IsSecretShapedValue(value))
            {
                issues.Add(new ValidationIssue(
                    "creation.input.text.secret-shaped",
                    "Credential-shaped values cannot be retained in project creation inputs.",
                    "value"));
            }
        }

        return issues.Count == 0
            ? ValidationResult.Success(new DynamicInputValue(
                DynamicInputValueKind.Text,
                value,
                booleanValue: false,
                wholeNumberValue: 0))
            : ValidationResult.Failure<DynamicInputValue>(issues);
    }

    public static ValidationResult<DynamicInputValue> Boolean(bool value)
    {
        return ValidationResult.Success(new DynamicInputValue(
            DynamicInputValueKind.Boolean,
            textValue: null,
            value,
            wholeNumberValue: 0));
    }

    public static ValidationResult<DynamicInputValue> WholeNumber(long value)
    {
        return ValidationResult.Success(new DynamicInputValue(
            DynamicInputValueKind.WholeNumber,
            textValue: null,
            booleanValue: false,
            value));
    }
}

public sealed class ProjectCreationDraft
{
    private const int MaximumNameLength = 200;
    private const int MaximumInputs = 128;
    private const int MaximumFeatures = 128;

    private static readonly ImmutableHashSet<string> _supportedIdeIds =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "none",
            "vscode",
            "visual-studio",
            "rider",
            "unity");

    private ProjectCreationDraft(
        string name,
        string rootPath,
        string outputFolder,
        BlueprintReference blueprint,
        ImmutableSortedDictionary<string, DynamicInputValue> inputs,
        ImmutableArray<string> features,
        string ideId)
    {
        Name = name;
        RootPath = rootPath;
        OutputFolder = outputFolder;
        Blueprint = blueprint;
        Inputs = inputs;
        Features = features;
        IdeId = ideId;
    }

    public string Name { get; }

    public string RootPath { get; }

    public string OutputFolder { get; }

    public BlueprintReference Blueprint { get; }

    public ImmutableSortedDictionary<string, DynamicInputValue> Inputs { get; }

    public ImmutableArray<string> Features { get; }

    public string IdeId { get; }

    public static ValidationResult<ProjectCreationDraft> Create(
        string? name,
        string? rootPath,
        string? outputFolder,
        BlueprintReference? blueprint,
        IEnumerable<KeyValuePair<string, DynamicInputValue?>>? inputs,
        IEnumerable<string?>? features,
        string? ideId)
    {
        var inputSnapshot = inputs?.ToImmutableArray() ?? [];
        var featureSnapshot = features?.ToImmutableArray() ?? [];
        var issues = new List<ValidationIssue>();

        ValidateName(name, issues);
        if (!WorkspaceRoot.Create(rootPath).IsValid)
        {
            issues.Add(new ValidationIssue(
                "creation.root.invalid",
                "A canonical local Windows project root is required.",
                "rootPath"));
        }

        var targetDirectory = WorkspaceRelativePath.Create(outputFolder);
        if (!targetDirectory.IsValid || outputFolder!.Contains('\\'))
        {
            issues.Add(new ValidationIssue(
                "creation.output-folder.invalid",
                "A single guarded output folder segment is required.",
                "outputFolder"));
        }

        if (blueprint is null)
        {
            issues.Add(new ValidationIssue(
                "creation.blueprint.required",
                "An exact blueprint selection is required.",
                "blueprint"));
        }

        ValidateInputs(inputs, inputSnapshot, issues);
        ValidateFeatures(features, featureSnapshot, issues);

        var canonicalIdeId = ideId?.Trim();
        if (ideId is null
            || !StringComparer.Ordinal.Equals(ideId, canonicalIdeId)
            || !_supportedIdeIds.Contains(canonicalIdeId!))
        {
            issues.Add(new ValidationIssue(
                "creation.ide.invalid",
                "A supported IDE choice is required.",
                "ideId"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new ProjectCreationDraft(
                name!.Trim(),
                rootPath!,
                outputFolder!,
                blueprint!,
                inputSnapshot.ToImmutableSortedDictionary(
                    item => item.Key.Trim(),
                    item => item.Value!,
                    StringComparer.Ordinal),
                [.. featureSnapshot.Select(item => item!.Trim())],
                canonicalIdeId!))
            : ValidationResult.Failure<ProjectCreationDraft>(issues);
    }

    private static void ValidateName(string? name, List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            issues.Add(new ValidationIssue(
                "creation.name.required",
                "A project display name is required.",
                "name"));
            return;
        }

        if (name.Length > MaximumNameLength || name.Any(char.IsControl))
        {
            issues.Add(new ValidationIssue(
                "creation.name.invalid",
                "The project display name is invalid or too long.",
                "name"));
        }
    }

    private static void ValidateInputs(
        IEnumerable<KeyValuePair<string, DynamicInputValue?>>? source,
        ImmutableArray<KeyValuePair<string, DynamicInputValue?>> snapshot,
        List<ValidationIssue> issues)
    {
        if (source is null)
        {
            issues.Add(new ValidationIssue(
                "creation.inputs.required",
                "The blueprint input collection is required.",
                "inputs"));
        }

        if (snapshot.Length > MaximumInputs)
        {
            issues.Add(new ValidationIssue(
                "creation.inputs.too-many",
                "The blueprint input collection exceeds the supported count.",
                "inputs"));
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < snapshot.Length; index++)
        {
            var item = snapshot[index];
            var key = item.Key?.Trim();
            if (item.Key is null
                || !StringComparer.Ordinal.Equals(item.Key, key)
                || !BlueprintIdentifierValidator.IsValid(key)
                || RedactedText.IsSecretShapedKey(key)
                || !keys.Add(key!))
            {
                issues.Add(new ValidationIssue(
                    "creation.input.key.invalid",
                    "A unique non-sensitive canonical input identifier is required.",
                    $"inputs[{index}].key"));
            }

            if (item.Value is null)
            {
                issues.Add(new ValidationIssue(
                    "creation.input.value.required",
                    "A typed blueprint input value is required.",
                    $"inputs[{index}].value"));
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
                "creation.features.required",
                "The enabled feature collection is required.",
                "features"));
        }

        if (snapshot.Length > MaximumFeatures)
        {
            issues.Add(new ValidationIssue(
                "creation.features.too-many",
                "The enabled feature collection exceeds the supported count.",
                "features"));
        }

        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < snapshot.Length; index++)
        {
            var item = snapshot[index];
            var identifier = item?.Trim();
            if (item is null
                || !StringComparer.Ordinal.Equals(item, identifier)
                || !BlueprintIdentifierValidator.IsValid(identifier)
                || !identifiers.Add(identifier!))
            {
                issues.Add(new ValidationIssue(
                    "creation.feature.invalid",
                    "A unique canonical feature identifier is required.",
                    $"features[{index}]"));
            }
        }
    }
}

public sealed class ProjectTargetDescriptor
{
    private ProjectTargetDescriptor(
        WorkspaceRoot parentRoot,
        WorkspaceRelativePath targetDirectory)
    {
        ParentRoot = parentRoot;
        TargetDirectory = targetDirectory;
    }

    public WorkspaceRoot ParentRoot { get; }

    public WorkspaceRelativePath TargetDirectory { get; }

    public static ValidationResult<ProjectTargetDescriptor> Create(
        WorkspaceRoot? parentRoot,
        WorkspaceRelativePath? targetDirectory)
    {
        var issues = new List<ValidationIssue>();
        if (parentRoot is null)
        {
            issues.Add(new ValidationIssue(
                "creation.target.root.required",
                "A guarded target parent root is required.",
                "parentRoot"));
        }

        if (targetDirectory is null || targetDirectory.Value.Contains('\\'))
        {
            issues.Add(new ValidationIssue(
                "creation.target.directory.invalid",
                "A single guarded target directory segment is required.",
                "targetDirectory"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new ProjectTargetDescriptor(parentRoot!, targetDirectory!))
            : ValidationResult.Failure<ProjectTargetDescriptor>(issues);
    }
}

public sealed class ProjectExecutionWorkspaces
{
    private ProjectExecutionWorkspaces(
        IWorkspaceFileSystem targetParent,
        WorkspaceRelativePath targetDirectory,
        IWorkspaceFileSystem runArtifacts)
    {
        TargetParent = targetParent;
        TargetDirectory = targetDirectory;
        RunArtifacts = runArtifacts;
    }

    public IWorkspaceFileSystem TargetParent { get; }

    public WorkspaceRelativePath TargetDirectory { get; }

    public IWorkspaceFileSystem RunArtifacts { get; }

    public static ValidationResult<ProjectExecutionWorkspaces> Create(
        ProjectTargetDescriptor? target,
        IWorkspaceFileSystem? targetParent,
        IWorkspaceFileSystem? runArtifacts)
    {
        var issues = new List<ValidationIssue>();
        if (target is null)
        {
            issues.Add(new ValidationIssue(
                "creation.workspaces.target.required",
                "A guarded target descriptor is required.",
                "target"));
        }

        if (targetParent is null)
        {
            issues.Add(new ValidationIssue(
                "creation.workspaces.target-parent.required",
                "A guarded target parent workspace is required.",
                "targetParent"));
        }
        else if (target is not null && !targetParent.Root.Equals(target.ParentRoot))
        {
            issues.Add(new ValidationIssue(
                "creation.workspaces.target-parent.mismatch",
                "The target parent workspace does not match the reviewed target.",
                "targetParent.root"));
        }

        if (runArtifacts is null)
        {
            issues.Add(new ValidationIssue(
                "creation.workspaces.run-artifacts.required",
                "A guarded run artifact workspace is required.",
                "runArtifacts"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new ProjectExecutionWorkspaces(
                targetParent!, target!.TargetDirectory, runArtifacts!))
            : ValidationResult.Failure<ProjectExecutionWorkspaces>(issues);
    }
}

public sealed class ProjectCreationPlanSnapshot
{
    private ProjectCreationPlanSnapshot(
        ProjectCreationDraft draft,
        ProjectTargetDescriptor target,
        ProjectRecipe recipe,
        PlannedProject plannedProject,
        string runId,
        string recipeId,
        DateTimeOffset createdAtUtc)
    {
        Draft = draft;
        Target = target;
        Recipe = recipe;
        PlannedProject = plannedProject;
        RunId = runId;
        RecipeId = recipeId;
        CreatedAtUtc = createdAtUtc;
    }

    public ProjectCreationDraft Draft { get; }

    public ProjectTargetDescriptor Target { get; }

    public ProjectRecipe Recipe { get; }

    public PlannedProject PlannedProject { get; }

    public string RunId { get; }

    public string RecipeId { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public static ValidationResult<ProjectCreationPlanSnapshot> Create(
        ProjectCreationDraft? draft,
        ProjectTargetDescriptor? target,
        ProjectRecipe? recipe,
        PlannedProject? plannedProject,
        string? runId,
        string? recipeId,
        DateTimeOffset createdAtUtc)
    {
        var issues = new List<ValidationIssue>();
        AddRequired(draft, "creation.plan.draft.required", "draft", issues);
        AddRequired(target, "creation.plan.target.required", "target", issues);
        AddRequired(recipe, "creation.plan.recipe.required", "recipe", issues);
        AddRequired(plannedProject, "creation.plan.project.required", "plannedProject", issues);
        ValidateIdentity(runId, "run-", "creation.plan.run-id.invalid", "runId", issues);
        ValidateIdentity(recipeId, "recipe-", "creation.plan.recipe-id.invalid", "recipeId", issues);
        if (createdAtUtc == default || createdAtUtc.Offset != TimeSpan.Zero)
        {
            issues.Add(new ValidationIssue(
                "creation.plan.created-at.invalid",
                "A non-default UTC creation timestamp is required.",
                "createdAtUtc"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new ProjectCreationPlanSnapshot(
                draft!, target!, recipe!, plannedProject!, runId!, recipeId!, createdAtUtc))
            : ValidationResult.Failure<ProjectCreationPlanSnapshot>(issues);
    }

    private static void AddRequired<T>(
        T? value,
        string code,
        string location,
        List<ValidationIssue> issues)
        where T : class
    {
        if (value is null)
        {
            issues.Add(new ValidationIssue(code, "A required creation plan component is missing.", location));
        }
    }

    private static void ValidateIdentity(
        string? value,
        string prefix,
        string code,
        string location,
        List<ValidationIssue> issues)
    {
        if (value is null
            || value.Length != prefix.Length + 32
            || !value.StartsWith(prefix, StringComparison.Ordinal)
            || value.AsSpan(prefix.Length).ToArray().Any(character =>
                character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
        {
            issues.Add(new ValidationIssue(code, "A canonical generated identity is required.", location));
        }
    }
}

public sealed class ProjectCreationExecutionSnapshot
{
    private ProjectCreationExecutionSnapshot(
        ProjectCreationPlanSnapshot plan,
        RunCheckpoint checkpoint)
    {
        Plan = plan;
        Checkpoint = checkpoint;
    }

    public ProjectCreationPlanSnapshot Plan { get; }

    public RunCheckpoint Checkpoint { get; }

    public static ValidationResult<ProjectCreationExecutionSnapshot> Create(
        ProjectCreationPlanSnapshot? plan,
        RunCheckpoint? checkpoint)
    {
        var issues = new List<ValidationIssue>();
        if (plan is null)
        {
            issues.Add(new ValidationIssue(
                "creation.execution.plan.required",
                "A reviewed creation plan is required.",
                "plan"));
        }

        if (checkpoint is null)
        {
            issues.Add(new ValidationIssue(
                "creation.execution.checkpoint.required",
                "A durable run checkpoint is required.",
                "checkpoint"));
        }

        if (plan is not null
            && checkpoint is not null
            && (!StringComparer.Ordinal.Equals(plan.RunId, checkpoint.Run.Id)
                || !StringComparer.Ordinal.Equals(plan.RecipeId, checkpoint.Run.RecipeId)
                || !StringComparer.Ordinal.Equals(
                    plan.PlannedProject.Plan.Id,
                    checkpoint.PlanHash)))
        {
            issues.Add(new ValidationIssue(
                "creation.execution.checkpoint.mismatch",
                "The durable checkpoint does not match the reviewed creation plan.",
                "checkpoint"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new ProjectCreationExecutionSnapshot(plan!, checkpoint!))
            : ValidationResult.Failure<ProjectCreationExecutionSnapshot>(issues);
    }
}

public sealed class ProjectCreationPresetDraft
{
    private ProjectCreationPresetDraft(
        BlueprintReference blueprint,
        ImmutableSortedDictionary<string, DynamicInputValue> inputs,
        ImmutableArray<string> features,
        string ideId)
    {
        Blueprint = blueprint;
        Inputs = inputs;
        Features = features;
        IdeId = ideId;
    }

    public BlueprintReference Blueprint { get; }

    public ImmutableSortedDictionary<string, DynamicInputValue> Inputs { get; }

    public ImmutableArray<string> Features { get; }

    public string IdeId { get; }

    public static ValidationResult<ProjectCreationPresetDraft> Create(
        BlueprintReference? blueprint,
        IEnumerable<KeyValuePair<string, DynamicInputValue?>>? inputs,
        IEnumerable<string?>? features,
        string? ideId)
    {
        var draft = ProjectCreationDraft.Create(
            "Preset",
            @"C:\DevForgePreset",
            "preset",
            blueprint,
            inputs,
            features,
            ideId);
        if (!draft.IsValid)
        {
            return ValidationResult.Failure<ProjectCreationPresetDraft>(draft.Issues);
        }

        return ValidationResult.Success(new ProjectCreationPresetDraft(
            draft.Value.Blueprint,
            draft.Value.Inputs,
            [.. draft.Value.Features.Order(StringComparer.Ordinal)],
            draft.Value.IdeId));
    }
}

public interface IProjectTargetPreflight
{
    Task<ValidationResult<ProjectTargetDescriptor>> PreflightAsync(
        string rootPath,
        string outputFolder,
        CancellationToken cancellationToken);
}

public interface IProjectExecutionWorkspaceFactory
{
    Task<ValidationResult<ProjectExecutionWorkspaces>> OpenAsync(
        ProjectTargetDescriptor target,
        string runId,
        CancellationToken cancellationToken);
}

public interface IRunIdentityGenerator
{
    string CreateRunId();

    string CreateRecipeId();
}

public interface IProjectCreationWorkflow
{
    Task<BlueprintCatalogSnapshot> LoadCatalogAsync(
        bool forceRefresh,
        CancellationToken cancellationToken);

    Task<ValidationResult<ProjectCreationPlanSnapshot>> CreatePlanAsync(
        ProjectCreationDraft draft,
        CancellationToken cancellationToken);

    Task<ProjectCreationExecutionSnapshot> ExecuteAsync(
        ProjectCreationPlanSnapshot plan,
        IProgress<ExecutionProgressLine>? progress,
        CancellationToken cancellationToken);
}
