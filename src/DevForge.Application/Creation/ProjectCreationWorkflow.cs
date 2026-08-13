using System.Globalization;
using DevForge.Application.Contracts;
using DevForge.Domain.Projects;
using DevForge.Domain.Runs;
using DevForge.Domain.Validation;

namespace DevForge.Application.Creation;

public sealed class ProjectCreationWorkflow : IProjectCreationWorkflow
{
    private readonly IBlueprintCatalog _catalog;
    private readonly IProjectPlanner _planner;
    private readonly IProjectTargetPreflight _targetPreflight;
    private readonly IProjectExecutionWorkspaceFactory _workspaceFactory;
    private readonly IRunIdentityGenerator _identities;
    private readonly IExecutionOrchestrator _orchestrator;
    private readonly TimeProvider _timeProvider;

    public ProjectCreationWorkflow(
        IBlueprintCatalog catalog,
        IProjectPlanner planner,
        IProjectTargetPreflight targetPreflight,
        IProjectExecutionWorkspaceFactory workspaceFactory,
        IRunIdentityGenerator identities,
        IExecutionOrchestrator orchestrator,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(targetPreflight);
        ArgumentNullException.ThrowIfNull(workspaceFactory);
        ArgumentNullException.ThrowIfNull(identities);
        ArgumentNullException.ThrowIfNull(orchestrator);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _catalog = catalog;
        _planner = planner;
        _targetPreflight = targetPreflight;
        _workspaceFactory = workspaceFactory;
        _identities = identities;
        _orchestrator = orchestrator;
        _timeProvider = timeProvider;
    }

    public async Task<BlueprintCatalogSnapshot> LoadCatalogAsync(
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (forceRefresh)
        {
            await _catalog.RefreshAsync(cancellationToken).ConfigureAwait(false);
        }

        return await _catalog.InspectAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ValidationResult<ProjectCreationPlanSnapshot>> CreatePlanAsync(
        ProjectCreationDraft draft,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        cancellationToken.ThrowIfCancellationRequested();

        var resolvedBlueprint = await _catalog.FindAsync(
            draft.Blueprint,
            cancellationToken).ConfigureAwait(false);
        if (resolvedBlueprint is null)
        {
            return Failure<ProjectCreationPlanSnapshot>(
                "creation.plan.blueprint.unavailable",
                "The exact trusted blueprint version is not available.",
                "blueprint");
        }

        var target = await _targetPreflight.PreflightAsync(
            draft.RootPath,
            draft.OutputFolder,
            cancellationToken).ConfigureAwait(false);
        if (!target.IsValid)
        {
            return ValidationResult.Failure<ProjectCreationPlanSnapshot>(target.Issues);
        }

        var recipe = CreateRecipe(draft);
        if (!recipe.IsValid)
        {
            return ValidationResult.Failure<ProjectCreationPlanSnapshot>(recipe.Issues);
        }

        var planned = await _planner.CreatePlanAsync(
            recipe.Value,
            cancellationToken).ConfigureAwait(false);
        if (!planned.IsValid)
        {
            return ValidationResult.Failure<ProjectCreationPlanSnapshot>(planned.Issues);
        }

        if (!planned.Value.BlueprintFingerprint.Equals(resolvedBlueprint.Fingerprint))
        {
            return Failure<ProjectCreationPlanSnapshot>(
                "creation.plan.blueprint-fingerprint.mismatch",
                "The planned blueprint fingerprint changed after selection.",
                "plannedProject.blueprintFingerprint");
        }

        if (!StringComparer.Ordinal.Equals(
                planned.Value.Preview.Blueprint.Id,
                draft.Blueprint.Id)
            || !StringComparer.Ordinal.Equals(
                planned.Value.Preview.Blueprint.Version,
                draft.Blueprint.Version))
        {
            return Failure<ProjectCreationPlanSnapshot>(
                "creation.plan.blueprint.mismatch",
                "The planned blueprint identity changed after selection.",
                "plannedProject.preview.blueprint");
        }

        return ProjectCreationPlanSnapshot.Create(
            draft,
            target.Value,
            recipe.Value,
            planned.Value,
            _identities.CreateRunId(),
            _identities.CreateRecipeId(),
            _timeProvider.GetUtcNow());
    }

    public async Task<ValidationResult<ProjectCreationExecutionSnapshot>> ExecuteAsync(
        ProjectCreationPlanSnapshot plan,
        IProgress<ExecutionProgressLine>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();
        var workspaces = await _workspaceFactory.OpenAsync(
            plan.Target,
            plan.RunId,
            cancellationToken).ConfigureAwait(false);
        if (!workspaces.IsValid)
        {
            return ValidationResult.Failure<ProjectCreationExecutionSnapshot>(workspaces.Issues);
        }

        var run = ProjectRun.Create(plan.RunId, plan.RecipeId);
        if (!run.IsValid)
        {
            return ValidationResult.Failure<ProjectCreationExecutionSnapshot>(run.Issues);
        }

        var request = ExecutionRequest.Create(
            plan.PlannedProject,
            run.Value,
            workspaces.Value.TargetParent,
            workspaces.Value.TargetDirectory,
            workspaces.Value.RunArtifacts,
            ExecutionMode.Fresh);
        if (!request.IsValid)
        {
            return ValidationResult.Failure<ProjectCreationExecutionSnapshot>(request.Issues);
        }

        var checkpoint = await _orchestrator.ExecuteAsync(
            request.Value,
            progress,
            cancellationToken).ConfigureAwait(false);
        return ProjectCreationExecutionSnapshot.Create(plan, checkpoint);
    }

    private static ValidationResult<ProjectRecipe> CreateRecipe(ProjectCreationDraft draft)
    {
        var git = GitOptions.Create(
            initializeRepository: false,
            primaryBranch: "main",
            useDevelopBranch: false,
            publishToGitHub: false,
            isPrivate: true);
        var openIde = !StringComparer.Ordinal.Equals(draft.IdeId, "none");
        var completion = CompletionOptions.Create(
            writeGenerationReport: true,
            writeHandoffDocument: true,
            openIde,
            openIde ? draft.IdeId : null);
        if (!git.IsValid || !completion.IsValid)
        {
            return Failure<ProjectRecipe>(
                "creation.recipe.options.invalid",
                "The M7 Git or completion options are invalid.",
                "draft");
        }

        var targetPath = Path.GetFullPath(Path.Combine(draft.RootPath, draft.OutputFolder));
        var inputs = draft.Inputs.ToDictionary(
            item => item.Key,
            item => (string?)ToRecipeValue(item.Value),
            StringComparer.Ordinal);
        return ProjectRecipe.Create(new ProjectRecipeDraft(
            draft.Name,
            targetPath,
            draft.Blueprint.Id,
            draft.Blueprint.Version,
            inputs,
            draft.Features.Select(feature => (string?)feature).ToArray(),
            TeamProfile: null,
            Git: git.Value,
            Completion: completion.Value));
    }

    private static string ToRecipeValue(DynamicInputValue value)
    {
        return value.Kind switch
        {
            DynamicInputValueKind.Text => value.TextValue!,
            DynamicInputValueKind.Boolean => value.BooleanValue ? "true" : "false",
            DynamicInputValueKind.WholeNumber => value.WholeNumberValue.ToString(
                CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException("An unsupported dynamic input kind cannot be planned."),
        };
    }

    private static ValidationResult<T> Failure<T>(string code, string message, string location)
    {
        return ValidationResult.Failure<T>([new ValidationIssue(code, message, location)]);
    }
}
