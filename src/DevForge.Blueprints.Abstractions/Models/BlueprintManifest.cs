using System.Collections.Immutable;
using DevForge.Blueprints.Abstractions.Validation;

namespace DevForge.Blueprints.Abstractions.Models;

public sealed class BlueprintManifest
{
    private BlueprintManifest(
        BlueprintManifestDraft draft,
        BlueprintTrustAssignment trustAssignment,
        SemanticVersionRange engineVersionRange,
        ImmutableArray<ToolRequirement?> tools,
        ImmutableArray<SemanticVersionRange?> toolVersionRanges,
        ImmutableArray<InputDefinition?> inputs,
        ImmutableArray<CompatibilityRule?> compatibilityRules,
        ImmutableArray<BlueprintStepDefinition?> steps,
        ImmutableArray<ValidatorDefinition?> validators,
        ImmutableArray<BlueprintFeatureDefinition?> features,
        ImmutableArray<BlueprintActionDefinition?> actions,
        ImmutableArray<BlueprintDependency?> dependencies,
        ImmutableArray<BlueprintArtifact?> artifacts)
    {
        Id = draft.Id!.Trim();
        Name = draft.Name?.Trim() ?? Id;
        Version = draft.Version!.Trim();
        EngineVersionRange = engineVersionRange.Expression;
        Trust = trustAssignment.Trust;
        Tools =
        [
            .. tools.Select(
                (tool, index) => NormalizeTool(tool, toolVersionRanges[index]!)),
        ];
        Inputs = [.. inputs.Select(NormalizeInput)];
        CompatibilityRules = [.. compatibilityRules.Select(NormalizeCompatibilityRule)];
        Steps = [.. steps.Select(NormalizeStep)];
        Validators = [.. validators.Select(NormalizeValidator)];
        Features = [.. features.Select(NormalizeFeature)];
        Actions = [.. actions.Select(NormalizeAction)];
        Dependencies = [.. dependencies.Select(NormalizeDependency)];
        Artifacts = [.. artifacts.Select(NormalizeArtifact)];
    }

    public string Id { get; }

    public string Name { get; }

    public string Version { get; }

    public string EngineVersionRange { get; }

    public BlueprintTrust Trust { get; }

    public ImmutableArray<ToolRequirement> Tools { get; }

    public ImmutableArray<InputDefinition> Inputs { get; }

    public ImmutableArray<CompatibilityRule> CompatibilityRules { get; }

    public ImmutableArray<BlueprintStepDefinition> Steps { get; }

    public ImmutableArray<ValidatorDefinition> Validators { get; }

    public ImmutableArray<BlueprintFeatureDefinition> Features { get; }

    public ImmutableArray<BlueprintActionDefinition> Actions { get; }

    public ImmutableArray<BlueprintDependency> Dependencies { get; }

    public ImmutableArray<BlueprintArtifact> Artifacts { get; }

    public static BlueprintValidationResult<BlueprintManifest> Create(
        BlueprintManifestDraft? draft,
        BlueprintTrustAssignment? trustAssignment)
    {
        if (draft is null)
        {
            return BlueprintValidationResult.Failure<BlueprintManifest>(
            [
                new BlueprintValidationIssue(
                    "blueprint.manifest.required",
                    "A blueprint manifest draft is required."),
            ]);
        }

        var tools = draft.Tools?.ToImmutableArray() ?? [];
        var inputs = draft.Inputs?.ToImmutableArray() ?? [];
        var compatibilityRules = draft.CompatibilityRules?.ToImmutableArray() ?? [];
        var steps = draft.Steps?.ToImmutableArray() ?? [];
        var validators = draft.Validators?.ToImmutableArray() ?? [];
        var features = draft.Features?.ToImmutableArray() ?? [];
        var actions = draft.Actions?.ToImmutableArray() ?? [];
        var dependencies = draft.Dependencies?.ToImmutableArray() ?? [];
        var artifacts = draft.Artifacts?.ToImmutableArray() ?? [];
        var engineVersionRange =
            SemanticVersionRange.TryParse(draft.EngineVersionRange, out var parsedEngineVersionRange)
                ? parsedEngineVersionRange
                : null;
        var toolVersionRanges = tools
            .Select(
                tool => tool is not null
                    && SemanticVersionRange.TryParse(tool.VersionRange, out var versionRange)
                        ? versionRange
                        : null)
            .ToImmutableArray();
        var issues = new List<BlueprintValidationIssue>();

        ValidateManifestIdentity(draft, engineVersionRange, issues);
        ValidateTrustAssignment(trustAssignment, issues);
        ValidateTools(draft.Tools, tools, toolVersionRanges, issues);
        ValidateInputs(draft.Inputs, inputs, issues);
        ValidateFeatures(draft.Features, features, issues);
        ValidateCompatibilityRules(
            draft.CompatibilityRules,
            compatibilityRules,
            issues);
        ValidateSteps(draft.Steps, steps, issues);
        ValidateActions(draft.Actions, actions, issues);
        ValidateValidators(draft.Validators, validators, issues);
        ValidateDependencies(draft.Dependencies, dependencies, issues);
        ValidateArtifacts(draft.Artifacts, artifacts, issues);

        return issues.Count == 0
            ? BlueprintValidationResult.Success(
                new BlueprintManifest(
                    draft,
                    trustAssignment!,
                    engineVersionRange!,
                    tools,
                    toolVersionRanges,
                    inputs,
                    compatibilityRules,
                    steps,
                    validators,
                    features,
                    actions,
                    dependencies,
                    artifacts))
            : BlueprintValidationResult.Failure<BlueprintManifest>(issues);
    }

    private static void ValidateManifestIdentity(
        BlueprintManifestDraft draft,
        SemanticVersionRange? engineVersionRange,
        List<BlueprintValidationIssue> issues)
    {
        if (!BlueprintIdentifierValidator.IsValid(draft.Id))
        {
            issues.Add(
                new BlueprintValidationIssue(
                    "blueprint.id.invalid",
                    "The blueprint identifier must use lowercase dot- or hyphen-separated segments.",
                    "id"));
        }

        if (draft.Name is not null && string.IsNullOrWhiteSpace(draft.Name))
        {
            issues.Add(
                new BlueprintValidationIssue(
                    "blueprint.name.required",
                    "A blueprint name cannot be blank.",
                    "name"));
        }

        if (!SemanticVersionRange.IsSemanticVersion(draft.Version))
        {
            issues.Add(
                new BlueprintValidationIssue(
                    "blueprint.version.invalid",
                    "The blueprint version must be a valid semantic version.",
                    "version"));
        }

        if (engineVersionRange is null)
        {
            issues.Add(
                new BlueprintValidationIssue(
                    "blueprint.engine-range.invalid",
                    "The engine version range must contain valid semantic-version comparators.",
                    "engineVersionRange"));
        }

    }

    private static void ValidateTrustAssignment(
        BlueprintTrustAssignment? trustAssignment,
        List<BlueprintValidationIssue> issues)
    {
        if (trustAssignment is null)
        {
            issues.Add(
                new BlueprintValidationIssue(
                    "blueprint.trust-assignment.required",
                    "A trust assignment from the catalog boundary is required.",
                    "trustAssignment"));
        }
        else if (!Enum.IsDefined(trustAssignment.Trust))
        {
            issues.Add(
                new BlueprintValidationIssue(
                    "blueprint.trust.invalid",
                    "The assigned blueprint trust level is not defined.",
                    "trustAssignment.trust"));
        }
    }
    private static void ValidateTools(
        IReadOnlyCollection<ToolRequirement?>? source,
        ImmutableArray<ToolRequirement?> tools,
        ImmutableArray<SemanticVersionRange?> toolVersionRanges,
        List<BlueprintValidationIssue> issues)
    {
        if (source is null)
        {
            issues.Add(
                new BlueprintValidationIssue(
                    "blueprint.tools.required",
                    "The blueprint tool collection is required.",
                    "tools"));
            return;
        }

        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < tools.Length; index++)
        {
            var tool = tools[index];
            if (tool is null)
            {
                issues.Add(
                    new BlueprintValidationIssue(
                        "blueprint.tool.required",
                        "A blueprint tool definition is required.",
                        $"tools[{index}]"));
                continue;
            }

            if (!BlueprintIdentifierValidator.IsValid(tool.Id))
            {
                issues.Add(
                    new BlueprintValidationIssue(
                        "blueprint.tool.id.invalid",
                        "A tool identifier must use lowercase dot- or hyphen-separated segments.",
                        $"tools[{index}].id"));
            }
            else if (!identifiers.Add(tool.Id.Trim()))
            {
                issues.Add(
                    new BlueprintValidationIssue(
                        "blueprint.tool.id.duplicate",
                        "Blueprint tool identifiers must be unique.",
                        $"tools[{index}].id"));
            }

            if (toolVersionRanges[index] is null)
            {
                issues.Add(
                    new BlueprintValidationIssue(
                        "blueprint.tool.version-range.invalid",
                        "A tool version range must contain valid semantic-version comparators.",
                        $"tools[{index}].versionRange"));
            }
        }
    }

    private static void ValidateInputs(
        IReadOnlyCollection<InputDefinition?>? source,
        ImmutableArray<InputDefinition?> inputs,
        List<BlueprintValidationIssue> issues)
    {
        if (source is null)
        {
            issues.Add(
                new BlueprintValidationIssue(
                    "blueprint.inputs.required",
                    "The blueprint input collection is required.",
                    "inputs"));
            return;
        }

        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < inputs.Length; index++)
        {
            var input = inputs[index];
            if (input is null)
            {
                issues.Add(
                    new BlueprintValidationIssue(
                        "blueprint.input.required",
                        "A blueprint input definition is required.",
                        $"inputs[{index}]"));
                continue;
            }

            var normalizedId = input.Id?.Trim();
            if (!BlueprintIdentifierValidator.IsValid(input.Id))
            {
                issues.Add(
                    new BlueprintValidationIssue(
                        "blueprint.input.id.invalid",
                        "An input identifier must use lowercase dot- or hyphen-separated segments.",
                        $"inputs[{index}].id"));
            }
            else if (!identifiers.Add(normalizedId!))
            {
                issues.Add(
                    new BlueprintValidationIssue(
                        "blueprint.input.id.duplicate",
                        "Blueprint input identifiers must be unique.",
                        $"inputs[{index}].id"));
            }

            if (BlueprintPrivacyPolicy.IsSensitiveIdentifier(input.Id))
            {
                issues.Add(
                    new BlueprintValidationIssue(
                        "blueprint.input.id.secret-shaped",
                        "Blueprint input identifiers must not describe secrets.",
                        $"inputs[{index}].id"));
            }

            if (!Enum.IsDefined(input.Kind))
            {
                issues.Add(
                    new BlueprintValidationIssue(
                        "blueprint.input.kind.invalid",
                        "The blueprint input kind is not defined.",
                        $"inputs[{index}].kind"));
            }

            if (input.DefaultValue is not null
                && BlueprintPrivacyPolicy.ContainsSensitiveDefault(input.DefaultValue))
            {
                issues.Add(
                    new BlueprintValidationIssue(
                        "blueprint.input.default.secret-shaped",
                        "Blueprint input defaults must not contain secret-shaped data.",
                        $"inputs[{index}].defaultValue"));
            }
        }
    }

    private static void ValidateCompatibilityRules(
        IReadOnlyCollection<CompatibilityRule?>? source,
        ImmutableArray<CompatibilityRule?> compatibilityRules,
        List<BlueprintValidationIssue> issues)
    {
        if (source is null)
        {
            issues.Add(
                new BlueprintValidationIssue(
                    "blueprint.compatibility-rules.required",
                    "The blueprint compatibility-rule collection is required.",
                    "compatibilityRules"));
            return;
        }

        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < compatibilityRules.Length; index++)
        {
            var rule = compatibilityRules[index];
            if (rule is null)
            {
                issues.Add(
                    new BlueprintValidationIssue(
                        "blueprint.compatibility-rule.required",
                        "A blueprint compatibility rule is required.",
                        $"compatibilityRules[{index}]"));
                continue;
            }
            var normalizedId = rule.Id?.Trim();
            if (!BlueprintIdentifierValidator.IsValid(rule.Id))
            {
                issues.Add(
                    new BlueprintValidationIssue(
                        "blueprint.rule.id.invalid",
                        "A rule identifier must use lowercase dot- or hyphen-separated segments.",
                        $"compatibilityRules[{index}].id"));
            }
            else if (!identifiers.Add(normalizedId!))
            {
                issues.Add(
                    new BlueprintValidationIssue(
                        "blueprint.rule.id.duplicate",
                        "Blueprint rule identifiers must be unique.",
                        $"compatibilityRules[{index}].id"));
            }

            if (string.IsNullOrWhiteSpace(rule.Expression))
            {
                issues.Add(
                    new BlueprintValidationIssue(
                        "blueprint.compatibility-rule.expression.required",
                        "A compatibility-rule expression is required.",
                        $"compatibilityRules[{index}].expression"));
            }

            if (string.IsNullOrWhiteSpace(rule.Message))
            {
                issues.Add(
                    new BlueprintValidationIssue(
                        "blueprint.compatibility-rule.message.required",
                        "A compatibility-rule message is required.",
                        $"compatibilityRules[{index}].message"));
            }

            if (!Enum.IsDefined(rule.Severity))
            {
                issues.Add(
                    new BlueprintValidationIssue(
                        "blueprint.rule.severity.invalid",
                        "The compatibility-rule severity is not defined.",
                        $"compatibilityRules[{index}].severity"));
            }

            if (!Enum.IsDefined(rule.Override))
            {
                issues.Add(
                    new BlueprintValidationIssue(
                        "blueprint.rule.override.invalid",
                        "The compatibility-rule override policy is not defined.",
                        $"compatibilityRules[{index}].override"));
            }

            if (rule.Remediation is not null && string.IsNullOrWhiteSpace(rule.Remediation))
            {
                issues.Add(
                    new BlueprintValidationIssue(
                        "blueprint.rule.remediation.invalid",
                        "A compatibility-rule remediation cannot be blank.",
                        $"compatibilityRules[{index}].remediation"));
            }
        }
    }

    private static void ValidateFeatures(
        IReadOnlyCollection<BlueprintFeatureDefinition?>? source,
        ImmutableArray<BlueprintFeatureDefinition?> features,
        List<BlueprintValidationIssue> issues)
    {
        if (source is null)
        {
            return;
        }

        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < features.Length; index++)
        {
            var feature = features[index];
            if (feature is null)
            {
                issues.Add(new BlueprintValidationIssue(
                    "blueprint.feature.required",
                    "A blueprint feature definition is required.",
                    $"features[{index}]"));
                continue;
            }

            var normalizedId = feature.Id?.Trim();
            if (!BlueprintIdentifierValidator.IsValid(feature.Id))
            {
                issues.Add(new BlueprintValidationIssue(
                    "blueprint.feature.id.invalid",
                    "A feature identifier must use lowercase dot- or hyphen-separated segments.",
                    $"features[{index}].id"));
            }
            else if (!identifiers.Add(normalizedId!))
            {
                issues.Add(new BlueprintValidationIssue(
                    "blueprint.feature.id.duplicate",
                    "Blueprint feature identifiers must be unique.",
                    $"features[{index}].id"));
            }
        }
    }

    private static void ValidateActions(
        IReadOnlyCollection<BlueprintActionDefinition?>? source,
        ImmutableArray<BlueprintActionDefinition?> actions,
        List<BlueprintValidationIssue> issues)
    {
        if (source is null)
        {
            return;
        }

        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < actions.Length; index++)
        {
            var action = actions[index];
            if (action is null)
            {
                issues.Add(new BlueprintValidationIssue(
                    "blueprint.action.required",
                    "A blueprint action definition is required.",
                    $"actions[{index}]"));
                continue;
            }

            var normalizedId = action.Id?.Trim();
            if (!BlueprintIdentifierValidator.IsValid(action.Id))
            {
                issues.Add(new BlueprintValidationIssue(
                    "blueprint.action.id.invalid",
                    "An action identifier must use lowercase dot- or hyphen-separated segments.",
                    $"actions[{index}].id"));
            }
            else if (!identifiers.Add(normalizedId!))
            {
                issues.Add(new BlueprintValidationIssue(
                    "blueprint.action.id.duplicate",
                    "Blueprint action identifiers must be unique.",
                    $"actions[{index}].id"));
            }

            AddHandlerIssue(
                issues,
                action.HandlerId,
                "blueprint.action",
                $"actions[{index}].handlerId");
            if (action.Parameters is null)
            {
                issues.Add(new BlueprintValidationIssue(
                    "blueprint.action.parameters.required",
                    "An action parameter map is required.",
                    $"actions[{index}].parameters"));
            }
            else
            {
                ValidateParameterKeys(
                    action.Parameters,
                    $"actions[{index}].parameters",
                    "blueprint.action.parameter",
                    issues);
            }

            if (action.Timeout <= TimeSpan.Zero)
            {
                issues.Add(new BlueprintValidationIssue(
                    "blueprint.action.timeout.invalid",
                    "A blueprint action timeout must be positive.",
                    $"actions[{index}].timeout"));
            }
        }
    }

    private static void ValidateDependencies(
        IReadOnlyCollection<BlueprintDependency?>? source,
        ImmutableArray<BlueprintDependency?> dependencies,
        List<BlueprintValidationIssue> issues)
    {
        if (source is null)
        {
            return;
        }

        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < dependencies.Length; index++)
        {
            var dependency = dependencies[index];
            if (dependency is null)
            {
                issues.Add(new BlueprintValidationIssue(
                    "blueprint.dependency.required",
                    "A blueprint dependency definition is required.",
                    $"dependencies[{index}]"));
                continue;
            }

            var normalizedId = dependency.Id?.Trim();
            if (!BlueprintIdentifierValidator.IsValid(dependency.Id))
            {
                issues.Add(new BlueprintValidationIssue(
                    "blueprint.dependency.id.invalid",
                    "A dependency identifier must use lowercase dot- or hyphen-separated segments.",
                    $"dependencies[{index}].id"));
            }
            else if (!identifiers.Add(normalizedId!))
            {
                issues.Add(new BlueprintValidationIssue(
                    "blueprint.dependency.id.duplicate",
                    "Blueprint dependency identifiers must be unique.",
                    $"dependencies[{index}].id"));
            }

            if (!SemanticVersion.TryParse(dependency.Version, out _))
            {
                issues.Add(new BlueprintValidationIssue(
                    "blueprint.dependency.version.invalid",
                    "A dependency version must be a valid semantic version.",
                    $"dependencies[{index}].version"));
            }
        }
    }

    private static void ValidateArtifacts(
        IReadOnlyCollection<BlueprintArtifact?>? source,
        ImmutableArray<BlueprintArtifact?> artifacts,
        List<BlueprintValidationIssue> issues)
    {
        if (source is null)
        {
            return;
        }

        var paths = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < artifacts.Length; index++)
        {
            var artifact = artifacts[index];
            if (artifact is null)
            {
                issues.Add(new BlueprintValidationIssue(
                    "blueprint.artifact.required",
                    "A blueprint artifact definition is required.",
                    $"artifacts[{index}]"));
                continue;
            }

            var normalizedPath = artifact.Path?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedPath) || normalizedPath.Contains('\0'))
            {
                issues.Add(new BlueprintValidationIssue(
                    "blueprint.artifact.path.invalid",
                    "A blueprint artifact path is required.",
                    $"artifacts[{index}].path"));
            }
            else if (!paths.Add(normalizedPath))
            {
                issues.Add(new BlueprintValidationIssue(
                    "blueprint.artifact.path.duplicate",
                    "Blueprint artifact paths must be unique.",
                    $"artifacts[{index}].path"));
            }
        }
    }

    private static void ValidateSteps(
        IReadOnlyCollection<BlueprintStepDefinition?>? source,
        ImmutableArray<BlueprintStepDefinition?> steps,
        List<BlueprintValidationIssue> issues)
    {
        if (source is null)
        {
            issues.Add(
                new BlueprintValidationIssue(
                    "blueprint.steps.required",
                    "The blueprint step collection is required.",
                    "steps"));
            return;
        }

        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < steps.Length; index++)
        {
            var step = steps[index];
            if (step is null)
            {
                issues.Add(
                    new BlueprintValidationIssue(
                        "blueprint.step.required",
                        "A blueprint step definition is required.",
                        $"steps[{index}]"));
                continue;
            }

            var normalizedId = step.Id?.Trim();
            if (!BlueprintIdentifierValidator.IsValid(step.Id))
            {
                issues.Add(
                    new BlueprintValidationIssue(
                        "blueprint.step.id.invalid",
                        "A step identifier must use lowercase dot- or hyphen-separated segments.",
                        $"steps[{index}].id"));
            }
            else if (!identifiers.Add(normalizedId!))
            {
                issues.Add(
                    new BlueprintValidationIssue(
                        "blueprint.step.id.duplicate",
                        "Blueprint step identifiers must be unique.",
                        $"steps[{index}].id"));
            }

            AddHandlerIssue(
                issues,
                step.HandlerId,
                "blueprint.step",
                $"steps[{index}].handlerId");

            if (step.Timeout <= TimeSpan.Zero)
            {
                issues.Add(
                    new BlueprintValidationIssue(
                        "blueprint.step.timeout.invalid",
                        "A blueprint step timeout must be positive.",
                        $"steps[{index}].timeout"));
            }
        }
    }

    private static void ValidateValidators(
        IReadOnlyCollection<ValidatorDefinition?>? source,
        ImmutableArray<ValidatorDefinition?> validators,
        List<BlueprintValidationIssue> issues)
    {
        if (source is null)
        {
            issues.Add(
                new BlueprintValidationIssue(
                    "blueprint.validators.required",
                    "The blueprint validator collection is required.",
                    "validators"));
            return;
        }

        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < validators.Length; index++)
        {
            var validator = validators[index];
            if (validator is null)
            {
                issues.Add(
                    new BlueprintValidationIssue(
                        "blueprint.validator.required",
                        "A blueprint validator definition is required.",
                        $"validators[{index}]"));
                continue;
            }

            if (!BlueprintIdentifierValidator.IsValid(validator.Id))
            {
                issues.Add(
                    new BlueprintValidationIssue(
                        "blueprint.validator.id.invalid",
                        "A validator identifier must use lowercase dot- or hyphen-separated segments.",
                        $"validators[{index}].id"));
            }
            else if (!identifiers.Add(validator.Id.Trim()))
            {
                issues.Add(
                    new BlueprintValidationIssue(
                        "blueprint.validator.id.duplicate",
                        "Blueprint validator identifiers must be unique.",
                        $"validators[{index}].id"));
            }

            AddHandlerIssue(
                issues,
                validator.HandlerId,
                "blueprint.validator",
                $"validators[{index}].handlerId");

            if (validator.Timeout <= TimeSpan.Zero)
            {
                issues.Add(
                    new BlueprintValidationIssue(
                        "blueprint.validator.timeout.invalid",
                        "A blueprint validator timeout must be positive.",
                        $"validators[{index}].timeout"));
            }

            ValidateParameterKeys(
                validator.Parameters,
                $"validators[{index}].parameters",
                "blueprint.validator.parameter",
                issues);
        }
    }

    private static void ValidateParameterKeys(
        ImmutableDictionary<string, BlueprintValue> parameters,
        string location,
        string codePrefix,
        List<BlueprintValidationIssue> issues)
    {
        var normalizedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in parameters)
        {
            var key = pair.Key.Trim();
            if (key.Length == 0)
            {
                issues.Add(new BlueprintValidationIssue(
                    $"{codePrefix}.key.required",
                    "A parameter key is required.",
                    location));
            }
            else if (!normalizedKeys.Add(key))
            {
                issues.Add(new BlueprintValidationIssue(
                    $"{codePrefix}.key.duplicate",
                    "Parameter keys must be unique after normalization.",
                    location));
            }

            if (pair.Value is null)
            {
                issues.Add(new BlueprintValidationIssue(
                    $"{codePrefix}.value.required",
                    "A parameter value is required.",
                    location));
            }
        }
    }

    private static void AddHandlerIssue(
        List<BlueprintValidationIssue> issues,
        string? handlerId,
        string codePrefix,
        string location)
    {
        if (string.IsNullOrWhiteSpace(handlerId))
        {
            issues.Add(
                new BlueprintValidationIssue(
                    $"{codePrefix}.handler.required",
                    "A handler identifier is required.",
                    location));
        }
        else if (!BlueprintIdentifierValidator.IsValid(handlerId))
        {
            issues.Add(
                new BlueprintValidationIssue(
                    $"{codePrefix}.handler.invalid",
                    "A handler identifier must use lowercase dot- or hyphen-separated segments.",
                    location));
        }
    }

    private static ToolRequirement NormalizeTool(
        ToolRequirement? tool,
        SemanticVersionRange versionRange)
    {
        return new ToolRequirement(
            tool!.Id.Trim(),
            versionRange.Expression,
            tool.Required);
    }

    private static InputDefinition NormalizeInput(InputDefinition? input)
    {
        return new InputDefinition(
            input!.Id.Trim(),
            input.Kind,
            input.Required,
            input.DefaultValue);
    }

    private static CompatibilityRule NormalizeCompatibilityRule(CompatibilityRule? rule)
    {
        return new CompatibilityRule(
            rule!.Id.Trim(),
            rule.Expression.Trim(),
            rule.Severity,
            rule.Message.Trim(),
            rule.Remediation?.Trim(),
            rule.Override);
    }

    private static BlueprintStepDefinition NormalizeStep(BlueprintStepDefinition? step)
    {
        return new BlueprintStepDefinition(
            step!.Id.Trim(),
            step.HandlerId.Trim(),
            step.Timeout);
    }

    private static ValidatorDefinition NormalizeValidator(ValidatorDefinition? validator)
    {
        return new ValidatorDefinition(
            validator!.Id.Trim(),
            validator.HandlerId.Trim(),
            validator.Timeout,
            NormalizeParameters(validator.Parameters),
            validator.Required);
    }

    private static BlueprintFeatureDefinition NormalizeFeature(BlueprintFeatureDefinition? feature)
    {
        return new BlueprintFeatureDefinition(feature!.Id.Trim(), feature.DefaultEnabled);
    }

    private static BlueprintActionDefinition NormalizeAction(BlueprintActionDefinition? action)
    {
        return new BlueprintActionDefinition(
            action!.Id.Trim(),
            action.HandlerId.Trim(),
            NormalizeParameters(action.Parameters),
            action.Timeout);
    }

    private static BlueprintDependency NormalizeDependency(BlueprintDependency? dependency)
    {
        return new BlueprintDependency(dependency!.Id.Trim(), dependency.Version.Trim());
    }

    private static BlueprintArtifact NormalizeArtifact(BlueprintArtifact? artifact)
    {
        return new BlueprintArtifact(artifact!.Path.Trim());
    }

    private static ImmutableDictionary<string, BlueprintValue> NormalizeParameters(
        ImmutableDictionary<string, BlueprintValue> parameters)
    {
        return parameters.ToImmutableDictionary(
            pair => pair.Key.Trim(),
            pair => pair.Value,
            StringComparer.Ordinal);
    }

}
