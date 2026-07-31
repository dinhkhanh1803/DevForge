using System.Collections.Immutable;
using DevForge.Blueprints.Abstractions.Validation;

namespace DevForge.Blueprints.Abstractions.Models;

public sealed class BlueprintManifest
{
    private static readonly string[] _secretFragments =
    [
        "apikey",
        "connectionstring",
        "credential",
        "password",
        "privatekey",
        "secret",
        "token",
    ];

    private BlueprintManifest(
        BlueprintManifestDraft draft,
        ImmutableArray<ToolRequirement?> tools,
        ImmutableArray<InputDefinition?> inputs,
        ImmutableArray<CompatibilityRule?> compatibilityRules,
        ImmutableArray<BlueprintStepDefinition?> steps,
        ImmutableArray<ValidatorDefinition?> validators)
    {
        Id = draft.Id!.Trim();
        Version = draft.Version!.Trim();
        EngineVersionRange = NormalizeVersionRange(draft.EngineVersionRange!);
        Trust = draft.Trust;
        Tools = [.. tools.Select(NormalizeTool)];
        Inputs = [.. inputs.Select(NormalizeInput)];
        CompatibilityRules = [.. compatibilityRules.Select(NormalizeCompatibilityRule)];
        Steps = [.. steps.Select(NormalizeStep)];
        Validators = [.. validators.Select(NormalizeValidator)];
    }

    public string Id { get; }

    public string Version { get; }

    public string EngineVersionRange { get; }

    public BlueprintTrustLevel Trust { get; }

    public ImmutableArray<ToolRequirement> Tools { get; }

    public ImmutableArray<InputDefinition> Inputs { get; }

    public ImmutableArray<CompatibilityRule> CompatibilityRules { get; }

    public ImmutableArray<BlueprintStepDefinition> Steps { get; }

    public ImmutableArray<ValidatorDefinition> Validators { get; }

    public static BlueprintValidationResult<BlueprintManifest> Create(BlueprintManifestDraft? draft)
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
        var issues = new List<BlueprintValidationIssue>();

        ValidateManifestIdentity(draft, issues);
        ValidateTools(draft.Tools, tools, issues);
        ValidateInputs(draft.Inputs, inputs, issues);
        ValidateCompatibilityRules(
            draft.CompatibilityRules,
            compatibilityRules,
            issues);
        ValidateSteps(draft.Steps, steps, issues);
        ValidateValidators(draft.Validators, validators, issues);

        return issues.Count == 0
            ? BlueprintValidationResult.Success(
                new BlueprintManifest(
                    draft,
                    tools,
                    inputs,
                    compatibilityRules,
                    steps,
                    validators))
            : BlueprintValidationResult.Failure<BlueprintManifest>(issues);
    }

    private static void ValidateManifestIdentity(
        BlueprintManifestDraft draft,
        List<BlueprintValidationIssue> issues)
    {
        if (!IsIdentifier(draft.Id))
        {
            issues.Add(
                new BlueprintValidationIssue(
                    "blueprint.id.invalid",
                    "The blueprint identifier must use lowercase dot- or hyphen-separated segments.",
                    "id"));
        }

        if (!IsSemanticVersion(draft.Version))
        {
            issues.Add(
                new BlueprintValidationIssue(
                    "blueprint.version.invalid",
                    "The blueprint version must be a valid semantic version.",
                    "version"));
        }

        if (!IsVersionRange(draft.EngineVersionRange))
        {
            issues.Add(
                new BlueprintValidationIssue(
                    "blueprint.engine-range.invalid",
                    "The engine version range must contain valid semantic-version comparators.",
                    "engineVersionRange"));
        }

        if (!Enum.IsDefined(draft.Trust))
        {
            issues.Add(
                new BlueprintValidationIssue(
                    "blueprint.trust.invalid",
                    "The blueprint trust level is not defined.",
                    "trust"));
        }
    }

    private static void ValidateTools(
        IReadOnlyCollection<ToolRequirement?>? source,
        ImmutableArray<ToolRequirement?> tools,
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

            if (!IsIdentifier(tool.Id))
            {
                issues.Add(
                    new BlueprintValidationIssue(
                        "blueprint.tool.id.invalid",
                        "A tool identifier must use lowercase dot- or hyphen-separated segments.",
                        $"tools[{index}].id"));
            }

            if (!IsVersionRange(tool.VersionRange))
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
            if (!IsIdentifier(input.Id))
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

            if (IsSecretShaped(input.Id))
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

            if (input.DefaultValue is not null && IsSecretShaped(input.DefaultValue))
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
            if (!IsIdentifier(step.Id))
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

            if (!IsIdentifier(validator.Id))
            {
                issues.Add(
                    new BlueprintValidationIssue(
                        "blueprint.validator.id.invalid",
                        "A validator identifier must use lowercase dot- or hyphen-separated segments.",
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
        else if (!IsIdentifier(handlerId))
        {
            issues.Add(
                new BlueprintValidationIssue(
                    $"{codePrefix}.handler.invalid",
                    "A handler identifier must use lowercase dot- or hyphen-separated segments.",
                    location));
        }
    }

    private static ToolRequirement NormalizeTool(ToolRequirement? tool)
    {
        return new ToolRequirement(
            tool!.Id.Trim(),
            NormalizeVersionRange(tool.VersionRange),
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
        return new CompatibilityRule(rule!.Expression.Trim(), rule.Message.Trim());
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
            validator.Timeout);
    }

    private static bool IsIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Trim();
        if (!IsLowercaseLetter(candidate[0]) || !IsLowercaseLetterOrDigit(candidate[^1]))
        {
            return false;
        }

        var previousWasSeparator = false;
        foreach (var character in candidate)
        {
            if (IsLowercaseLetterOrDigit(character))
            {
                previousWasSeparator = false;
                continue;
            }

            if ((character != '.' && character != '-') || previousWasSeparator)
            {
                return false;
            }

            previousWasSeparator = true;
        }

        return true;
    }

    private static bool IsVersionRange(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var tokens = SplitRange(value);
        var comparatorExpected = true;
        foreach (var token in tokens)
        {
            if (token == "||")
            {
                if (comparatorExpected)
                {
                    return false;
                }

                comparatorExpected = true;
                continue;
            }

            if (!TryGetComparatorVersion(token, out var version)
                || !IsSemanticVersion(version))
            {
                return false;
            }

            comparatorExpected = false;
        }

        return !comparatorExpected;
    }

    private static bool TryGetComparatorVersion(string token, out string version)
    {
        if (token.StartsWith(">=", StringComparison.Ordinal)
            || token.StartsWith("<=", StringComparison.Ordinal))
        {
            version = token[2..];
        }
        else if (token[0] is '>' or '<' or '=')
        {
            version = token[1..];
        }
        else
        {
            version = token;
        }

        return version.Length > 0;
    }

    private static bool IsSemanticVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Trim();
        var buildParts = candidate.Split('+');
        if (buildParts.Length > 2
            || (buildParts.Length == 2 && !AreValidIdentifiers(buildParts[1], false)))
        {
            return false;
        }

        var coreAndPrerelease = buildParts[0];
        var prereleaseSeparator = coreAndPrerelease.IndexOf('-', StringComparison.Ordinal);
        var core = prereleaseSeparator < 0
            ? coreAndPrerelease
            : coreAndPrerelease[..prereleaseSeparator];
        if (prereleaseSeparator >= 0
            && !AreValidIdentifiers(coreAndPrerelease[(prereleaseSeparator + 1)..], true))
        {
            return false;
        }

        var coreParts = core.Split('.');
        return coreParts.Length == 3 && coreParts.All(IsValidNumericIdentifier);
    }

    private static bool AreValidIdentifiers(string value, bool forbidNumericLeadingZero)
    {
        var identifiers = value.Split('.');
        foreach (var identifier in identifiers)
        {
            if (identifier.Length == 0
                || identifier.Any(
                    character => !IsAsciiLetterOrDigit(character) && character != '-'))
            {
                return false;
            }

            if (forbidNumericLeadingZero
                && identifier.All(IsAsciiDigit)
                && !IsValidNumericIdentifier(identifier))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidNumericIdentifier(string value)
    {
        return value.Length > 0
            && value.All(IsAsciiDigit)
            && (value.Length == 1 || value[0] != '0');
    }

    private static bool IsSecretShaped(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = string.Concat(
            value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant));
        return _secretFragments.Any(
            fragment => normalized.Contains(fragment, StringComparison.Ordinal));
    }

    private static string NormalizeVersionRange(string value)
    {
        return string.Join(' ', SplitRange(value));
    }

    private static string[] SplitRange(string value)
    {
        return value.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool IsLowercaseLetter(char value)
    {
        return value is >= 'a' and <= 'z';
    }

    private static bool IsLowercaseLetterOrDigit(char value)
    {
        return IsLowercaseLetter(value) || IsAsciiDigit(value);
    }

    private static bool IsAsciiLetterOrDigit(char value)
    {
        return value is >= 'a' and <= 'z'
            || value is >= 'A' and <= 'Z'
            || IsAsciiDigit(value);
    }

    private static bool IsAsciiDigit(char value)
    {
        return value is >= '0' and <= '9';
    }
}
