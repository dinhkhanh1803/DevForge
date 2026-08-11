using System.Collections.Immutable;
using DevForge.Application.Contracts;
using DevForge.Application.Planning.CompatibilityRules;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Environment;
using DevForge.Domain.Execution;
using DevForge.Domain.Privacy;
using DevForge.Domain.Projects;
using DevForge.Domain.Validation;

namespace DevForge.Application.Planning;

public sealed class ProjectPlanner : IProjectPlanner
{
    private readonly IBlueprintCatalog _catalog;
    private readonly IEnvironmentDoctor _environmentDoctor;
    private readonly IPlanningRuntimeContextProvider _runtimeProvider;
    private readonly IInputSchemaValidator _schemaValidator;
    private readonly ICompatibilityRuleEvaluator _ruleEvaluator;
    private readonly IVariableTemplateResolver _variableResolver;
    private readonly PlanHasher _hasher = new(new CanonicalPlanSerializer());

    public ProjectPlanner(
        IBlueprintCatalog catalog,
        IEnvironmentDoctor environmentDoctor,
        IPlanningRuntimeContextProvider runtimeProvider,
        IInputSchemaValidator schemaValidator,
        ICompatibilityRuleEvaluator ruleEvaluator,
        IVariableTemplateResolver variableResolver)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(environmentDoctor);
        ArgumentNullException.ThrowIfNull(runtimeProvider);
        ArgumentNullException.ThrowIfNull(schemaValidator);
        ArgumentNullException.ThrowIfNull(ruleEvaluator);
        ArgumentNullException.ThrowIfNull(variableResolver);
        _catalog = catalog;
        _environmentDoctor = environmentDoctor;
        _runtimeProvider = runtimeProvider;
        _schemaValidator = schemaValidator;
        _ruleEvaluator = ruleEvaluator;
        _variableResolver = variableResolver;
    }

    public async Task<ValidationResult<PlannedProject>> CreatePlanAsync(
        ProjectRecipe recipe,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (recipe is null)
        {
            return Failure<PlannedProject>("A project recipe is required.", "recipe");
        }

        var reference = BlueprintReference.Create(recipe.BlueprintId, recipe.BlueprintVersion);
        if (!reference.IsValid)
        {
            return Failure<PlannedProject>("The recipe blueprint reference is invalid.", "blueprint");
        }

        var blueprint = await _catalog.FindAsync(reference.Value, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (blueprint is null)
        {
            return Failure<PlannedProject>(
                "The exact trusted blueprint version is not available.",
                "blueprint");
        }

        var runtime = _runtimeProvider.GetCurrent();
        if (runtime is null)
        {
            return Failure<PlannedProject>(
                "A fixed planning runtime context is required.",
                "runtime");
        }

        var environment = await _environmentDoctor.InspectAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (environment is null)
        {
            return Failure<PlannedProject>(
                "An environment snapshot is required for planning.",
                "environment");
        }

        var issues = new List<ValidationIssue>();
        ValidateEngine(blueprint, runtime, issues);
        ValidateTools(blueprint, environment, issues);
        var configuration = _schemaValidator.Validate(recipe, blueprint, cancellationToken);
        if (!configuration.IsValid)
        {
            issues.AddRange(configuration.Issues);
        }

        var ruleContext = CreateRuleContext(
            recipe,
            blueprint,
            runtime,
            environment,
            configuration.IsValid ? configuration.Value : null);
        if (!ruleContext.IsValid)
        {
            issues.AddRange(ruleContext.Issues);
        }
        else
        {
            var evaluation = _ruleEvaluator.EvaluateRules(
                blueprint.Manifest.CompatibilityRules,
                ruleContext.Value,
                cancellationToken);
            if (!evaluation.IsValid)
            {
                issues.AddRange(evaluation.Issues);
            }
            else
            {
                issues.AddRange(evaluation.Value.BlockingFailures.Select(ToBlockingIssue));
                if (issues.Count == 0 && configuration.IsValid)
                {
                    return BuildPlan(
                        recipe,
                        blueprint,
                        configuration.Value,
                        evaluation.Value,
                        environment,
                        cancellationToken);
                }
            }
        }

        return ValidationResult.Failure<PlannedProject>(issues);
    }

    private ValidationResult<PlannedProject> BuildPlan(
        ProjectRecipe recipe,
        ResolvedBlueprint blueprint,
        EffectiveRecipeConfiguration configuration,
        CompatibilityRuleEvaluation evaluation,
        EnvironmentSnapshot environment,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var teamStandards = recipe.TeamProfile?.Standards
            .ToImmutableSortedDictionary(StringComparer.Ordinal)
            ?? ImmutableSortedDictionary<string, string>.Empty.WithComparers(StringComparer.Ordinal);
        if (!PlanValue.FromString(recipe.Name).IsValid
            || recipe.Completion.IdeId is not null
                && !PlanValue.FromString(recipe.Completion.IdeId).IsValid
            || teamStandards.Any(item => !PlanValue.FromString(item.Value).IsValid))
        {
            return Failure<PlannedProject>(
                "A project or team value is unsafe for deterministic planning.",
                "recipe");
        }

        var variableContext = CreateVariableContext(recipe, blueprint, configuration);
        if (!variableContext.IsValid)
        {
            return ValidationResult.Failure<PlannedProject>(variableContext.Issues);
        }

        var steps = ImmutableArray.CreateBuilder<ExecutionStep>();
        foreach (var action in blueprint.Manifest.Actions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var inputs = ResolveParameters(action.Parameters, variableContext.Value, cancellationToken);
            if (!inputs.IsValid)
            {
                return ValidationResult.Failure<PlannedProject>(inputs.Issues);
            }

            var step = ExecutionStep.Create(
                action.Id,
                action.Id,
                action.HandlerId,
                inputs.Value.Select(item => KeyValuePair.Create<string, PlanValue?>(item.Key, item.Value)),
                action.Timeout,
                RetryPolicy.None);
            if (!step.IsValid)
            {
                return HashFailure<PlannedProject>();
            }

            steps.Add(step.Value);
        }

        var validators = ImmutableArray.CreateBuilder<ExecutionValidator>();
        foreach (var definition in blueprint.Manifest.Validators)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var inputs = ResolveParameters(definition.Parameters, variableContext.Value, cancellationToken);
            if (!inputs.IsValid)
            {
                return ValidationResult.Failure<PlannedProject>(inputs.Issues);
            }

            var validator = ExecutionValidator.Create(
                definition.Id,
                definition.HandlerId,
                inputs.Value.Select(item => KeyValuePair.Create<string, PlanValue?>(item.Key, item.Value)),
                definition.Timeout,
                definition.Required);
            if (!validator.IsValid)
            {
                return HashFailure<PlannedProject>();
            }

            validators.Add(validator.Value);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var stepSnapshot = steps.ToImmutable();
        var validatorSnapshot = validators.ToImmutable();
        var hashInput = new PlanHashInput(
            blueprint.Manifest.Id,
            blueprint.Manifest.Version,
            blueprint.Fingerprint.AggregateChecksum,
            configuration.Inputs,
            configuration.EnabledFeatures,
            teamStandards,
            recipe.Git,
            recipe.Completion,
            stepSnapshot,
            validatorSnapshot,
            blueprint.Manifest.Tools,
            blueprint.Manifest.Dependencies,
            blueprint.Manifest.Artifacts);
        string planHash;
        try
        {
            planHash = _hasher.Compute(hashInput);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HashFailure<PlannedProject>();
        }

        var plan = ExecutionPlan.Create(planHash, stepSnapshot, validatorSnapshot);
        if (!plan.IsValid)
        {
            return HashFailure<PlannedProject>();
        }

        var preview = PlanPreview.Create(
            BlueprintReference.Create(blueprint.Manifest.Id, blueprint.Manifest.Version).Value,
            stepSnapshot.Select(step => new PlanPreviewStep(
                step.Id,
                step.Handler,
                step.Timeout,
                CreateProcessPreview(step.Handler))),
            validatorSnapshot.Select(validator => new PlanPreviewValidator(
                validator.Id,
                validator.Handler,
                validator.Timeout,
                validator.Required,
                CreateProcessPreview(validator.Handler))),
            blueprint.Manifest.Tools,
            CreateToolStatuses(blueprint.Manifest.Tools, environment),
            blueprint.Manifest.Dependencies,
            blueprint.Manifest.Artifacts,
            evaluation.Warnings.Select(ToWarningIssue),
            configuration.Inputs.Select(item => KeyValuePair.Create<string, PlanValue?>(item.Key, item.Value)),
            configuration.EnabledFeatures,
            recipe.Git,
            recipe.Completion,
            planHash);
        if (!preview.IsValid)
        {
            return HashFailure<PlannedProject>();
        }

        return PlannedProject.Create(plan.Value, preview.Value, blueprint.Fingerprint);
    }

    private static ImmutableArray<PlanPreviewToolStatus> CreateToolStatuses(
        ImmutableArray<ToolRequirement> requirements,
        EnvironmentSnapshot environment)
    {
        var tools = environment.Tools.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
        return
        [
            .. requirements.Select(requirement =>
            {
                var available = tools.TryGetValue(requirement.Id, out var tool) && tool.IsAvailable;
                SemanticVersion? version = null;
                var hasVersion = available && SemanticVersion.TryParse(tool!.Version, out version);
                var compatible = hasVersion
                    && SemanticVersionRange.TryParse(requirement.VersionRange, out var range)
                    && range.Contains(version!);
                return new PlanPreviewToolStatus(
                    requirement.Id,
                    requirement.VersionRange,
                    requirement.Required,
                    available,
                    compatible,
                    hasVersion ? version!.Normalized : null);
            }),
        ];
    }

    private static RedactedText? CreateProcessPreview(string handler)
    {
        if (handler is not ("run-process" or "package-install" or "validate-command"))
        {
            return null;
        }

        var result = RedactedText.FromTrustedRedaction($"{handler}: process arguments redacted");
        return result.IsValid ? result.Value : null;
    }

    private ValidationResult<ImmutableSortedDictionary<string, PlanValue>> ResolveParameters(
        ImmutableDictionary<string, BlueprintValue> parameters,
        PlanningVariableContext context,
        CancellationToken cancellationToken)
    {
        var output = ImmutableSortedDictionary.CreateBuilder<string, PlanValue>(StringComparer.Ordinal);
        foreach (var parameter in parameters.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolved = _variableResolver.Resolve(parameter.Value, context, cancellationToken);
            if (!resolved.IsValid)
            {
                return ValidationResult.Failure<ImmutableSortedDictionary<string, PlanValue>>(resolved.Issues);
            }

            output.Add(parameter.Key, resolved.Value);
        }

        return ValidationResult.Success(output.ToImmutable());
    }

    private static void ValidateEngine(
        ResolvedBlueprint blueprint,
        PlanningRuntimeContext runtime,
        List<ValidationIssue> issues)
    {
        if (!SemanticVersionRange.TryParse(blueprint.Manifest.EngineVersionRange, out var range)
            || !range.Contains(runtime.EngineVersion))
        {
            issues.Add(Issue(
                "The blueprint is incompatible with this DevForge engine version.",
                "engine.version"));
        }
    }

    private static void ValidateTools(
        ResolvedBlueprint blueprint,
        EnvironmentSnapshot environment,
        List<ValidationIssue> issues)
    {
        var detected = environment.Tools.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var requirement in blueprint.Manifest.Tools)
        {
            var compatible = detected.TryGetValue(requirement.Id, out var tool)
                && tool.IsAvailable
                && SemanticVersion.TryParse(tool.Version, out var version)
                && SemanticVersionRange.TryParse(requirement.VersionRange, out var range)
                && range.Contains(version);
            if (!compatible && requirement.Required)
            {
                issues.Add(Issue(
                    "A required compatible tool is unavailable.",
                    $"tools.{requirement.Id}"));
            }
        }
    }

    private static ValidationResult<PlanningRuleContext> CreateRuleContext(
        ProjectRecipe recipe,
        ResolvedBlueprint blueprint,
        PlanningRuntimeContext runtime,
        EnvironmentSnapshot environment,
        EffectiveRecipeConfiguration? configuration)
    {
        var values = new List<KeyValuePair<string, PlanningRuleValue?>>
        {
            Pair("runtime.os", PlanningRuleValue.FromText(runtime.OperatingSystem).Value),
            Pair("runtime.arch", PlanningRuleValue.FromText(runtime.Architecture).Value),
            Pair("engine.version", PlanningRuleValue.FromSemanticVersion(runtime.EngineVersion.Normalized).Value),
            Pair("blueprint.id", PlanningRuleValue.FromText(blueprint.Manifest.Id).Value),
            Pair("blueprint.version", PlanningRuleValue.FromSemanticVersion(blueprint.Manifest.Version).Value),
            Pair("git.branch-policy", PlanningRuleValue.FromText(
                recipe.Git.UseDevelopBranch ? "main-and-develop" : "main").Value),
        };
        if (recipe.TeamProfile?.Standards.TryGetValue("package-manager", out var packageManager) == true)
        {
            var value = PlanningRuleValue.FromText(packageManager);
            if (!value.IsValid)
            {
                return ValidationResult.Failure<PlanningRuleContext>(value.Issues);
            }

            values.Add(Pair("team.package-manager", value.Value));
        }

        if (configuration is not null)
        {
            values.AddRange(configuration.Inputs.Select(item => Pair(
                $"recipe.input.{item.Key}",
                ToRuleValue(item.Value))));
        }

        var enabled = configuration?.EnabledFeatures.ToHashSet(StringComparer.Ordinal)
            ?? recipe.Features.ToHashSet(StringComparer.Ordinal);
        values.AddRange(blueprint.Manifest.Features.Select(feature => Pair(
            $"recipe.feature.{feature.Id}",
            PlanningRuleValue.FromBoolean(enabled.Contains(feature.Id)))));

        var tools = environment.Tools.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var requirement in blueprint.Manifest.Tools)
        {
            var available = tools.TryGetValue(requirement.Id, out var tool) && tool.IsAvailable;
            values.Add(Pair($"tool.{requirement.Id}.available", PlanningRuleValue.FromBoolean(available)));
            if (available && SemanticVersion.TryParse(tool!.Version, out var version))
            {
                values.Add(Pair(
                    $"tool.{requirement.Id}.version",
                    PlanningRuleValue.FromSemanticVersion(version.Normalized).Value));
            }
        }

        return PlanningRuleContext.Create(values);
    }

    private static ValidationResult<PlanningVariableContext> CreateVariableContext(
        ProjectRecipe recipe,
        ResolvedBlueprint blueprint,
        EffectiveRecipeConfiguration configuration)
    {
        var values = new List<KeyValuePair<string, PlanningVariableValue?>>
        {
            Variable("project.name", Text(recipe.Name)),
            Variable("project.safe-name", Text(CreateSafeName(recipe.Name))),
            Variable("project.target-path", PlanningVariableValue.Placeholder("project.target-path").Value),
            Variable("blueprint.id", Text(blueprint.Manifest.Id)),
            Variable("blueprint.version", Text(blueprint.Manifest.Version)),
            Variable("git.primary-branch", Text(recipe.Git.PrimaryBranch)),
            Variable("git.develop-branch", Text(recipe.Git.UseDevelopBranch ? "develop" : string.Empty)),
            Variable("runtime.staging-path", PlanningVariableValue.Placeholder("runtime.staging-path").Value),
            Variable("runtime.run-id", PlanningVariableValue.Placeholder("runtime.run-id").Value),
        };
        values.AddRange(configuration.Inputs.Select(item => Variable(
            $"recipe.input.{item.Key}",
            PlanningVariableValue.FromValue(item.Value).Value)));
        var enabled = configuration.EnabledFeatures.ToHashSet(StringComparer.Ordinal);
        values.AddRange(blueprint.Manifest.Features.Select(feature => Variable(
            $"recipe.feature.{feature.Id}",
            PlanningVariableValue.FromValue(PlanValue.FromBoolean(enabled.Contains(feature.Id))).Value)));

        AddTeamVariable(values, recipe.TeamProfile, "company-name", "team.company-name");
        AddTeamVariable(values, recipe.TeamProfile, "root-namespace", "team.root-namespace");
        AddTeamVariable(values, recipe.TeamProfile, "package-manager", "team.package-manager");
        return PlanningVariableContext.Create(values);
    }

    private static void AddTeamVariable(
        List<KeyValuePair<string, PlanningVariableValue?>> values,
        TeamProfile? team,
        string standard,
        string variable)
    {
        if (team?.Standards.TryGetValue(standard, out var value) == true)
        {
            values.Add(Variable(variable, Text(value)));
        }
    }

    private static string CreateSafeName(string value)
    {
        var output = new List<char>(value.Length);
        var separator = false;
        foreach (var character in value)
        {
            var normalized = character is >= 'A' and <= 'Z' ? (char)(character + 32) : character;
            if (normalized is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                output.Add(normalized);
                separator = false;
            }
            else if (!separator && output.Count > 0)
            {
                output.Add('-');
                separator = true;
            }
        }

        if (output.Count > 0 && output[^1] == '-')
        {
            output.RemoveAt(output.Count - 1);
        }

        return output.Count == 0 ? "project" : new string([.. output]);
    }

    private static PlanningVariableValue Text(string value) =>
        PlanningVariableValue.FromValue(PlanValue.FromString(value).Value).Value;

    private static PlanningRuleValue ToRuleValue(PlanValue value)
    {
        return value.Kind switch
        {
            PlanValueKind.Text => PlanningRuleValue.FromText(value.StringValue).Value,
            PlanValueKind.Boolean => PlanningRuleValue.FromBoolean(value.BooleanValue),
            PlanValueKind.WholeNumber => PlanningRuleValue.FromInteger(value.IntegerValue),
            _ => throw new InvalidOperationException("A recipe input has an unsupported rule value kind."),
        };
    }

    private static KeyValuePair<string, PlanningRuleValue?> Pair(
        string key,
        PlanningRuleValue value) => KeyValuePair.Create<string, PlanningRuleValue?>(key, value);

    private static KeyValuePair<string, PlanningVariableValue?> Variable(
        string key,
        PlanningVariableValue value) => KeyValuePair.Create<string, PlanningVariableValue?>(key, value);

    private static ValidationIssue ToBlockingIssue(CompatibilityRuleFinding finding) =>
        Issue(finding.Message.Value, $"rules.{finding.RuleId}");

    private static ValidationIssue ToWarningIssue(CompatibilityRuleFinding finding) =>
        Issue(finding.Message.Value, $"rules.{finding.RuleId}");

    private static ValidationIssue Issue(string message, string location) =>
        new("DF-PLAN-001", message, location);

    private static ValidationResult<T> Failure<T>(string message, string location) =>
        ValidationResult.Failure<T>([Issue(message, location)]);

    private static ValidationResult<T> HashFailure<T>() =>
        ValidationResult.Failure<T>(
        [
            new ValidationIssue(
                "DF-PLAN-002",
                "The deterministic execution plan could not be constructed.",
                "plan"),
        ]);
}
