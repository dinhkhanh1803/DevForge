using System.Collections.Immutable;
using DevForge.Domain.Privacy;
using DevForge.Domain.Validation;

namespace DevForge.Domain.Execution;

public sealed class ExecutionStep
{
    private ExecutionStep(
        string id,
        string name,
        string handler,
        IEnumerable<KeyValuePair<string, PlanValue>> inputs,
        TimeSpan timeout,
        RetryPolicy retryPolicy)
    {
        Id = id;
        Name = name;
        Handler = handler;
        Inputs = inputs.ToImmutableDictionary(StringComparer.Ordinal);
        Timeout = timeout;
        RetryPolicy = retryPolicy;
    }

    public string Id { get; }

    public string Name { get; }

    public string Handler { get; }

    public ImmutableDictionary<string, PlanValue> Inputs { get; }

    public TimeSpan Timeout { get; }

    public RetryPolicy RetryPolicy { get; }

    public static ValidationResult<ExecutionStep> Create(
        string? id,
        string? name,
        string? handler,
        IEnumerable<KeyValuePair<string, PlanValue?>>? inputs,
        TimeSpan timeout,
        RetryPolicy? retryPolicy)
    {
        var issues = new List<ValidationIssue>();
        if (string.IsNullOrWhiteSpace(id))
        {
            issues.Add(new ValidationIssue("step.id.required", "Execution step identifier is required.", "id"));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            issues.Add(new ValidationIssue("step.name.required", "Execution step name is required.", "name"));
        }

        if (string.IsNullOrWhiteSpace(handler))
        {
            issues.Add(new ValidationIssue("step.handler.required", "Execution step handler is required.", "handler"));
        }

        var inputsSnapshot = inputs?.ToImmutableArray() ?? [];
        if (inputs is null)
        {
            issues.Add(new ValidationIssue("step.inputs.required", "Execution step inputs are required.", "inputs"));
        }
        else
        {
            var inputNames = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < inputsSnapshot.Length; index++)
            {
                var input = inputsSnapshot[index];
                if (string.IsNullOrWhiteSpace(input.Key))
                {
                    issues.Add(
                        new ValidationIssue(
                            "step.input.name.required",
                            "An execution step input name is required.",
                            $"inputs[{index}].name"));
                }
                else if (!inputNames.Add(input.Key.Trim()))
                {
                    issues.Add(
                        new ValidationIssue(
                            "step.input.name.duplicate",
                            "Execution step input names must be unique.",
                            $"inputs[{index}].name"));
                }

                if (input.Value is null)
                {
                    issues.Add(
                        new ValidationIssue(
                            "step.input.value.required",
                            "An execution step input value is required.",
                            $"inputs[{index}].value"));
                }
            }
        }

        if (timeout <= TimeSpan.Zero)
        {
            issues.Add(new ValidationIssue("step.timeout.invalid", "Step timeout must be positive.", "timeout"));
        }

        if (retryPolicy is null)
        {
            issues.Add(
                new ValidationIssue(
                    "step.retry-policy.required",
                    "Execution step retry policy is required.",
                    "retryPolicy"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(
                new ExecutionStep(
                    id!.Trim(),
                    name!.Trim(),
                    handler!.Trim(),
                    inputsSnapshot.Select(input => KeyValuePair.Create(input.Key.Trim(), input.Value!)),
                    timeout,
                    retryPolicy!))
            : ValidationResult.Failure<ExecutionStep>(issues);
    }
}

public sealed class ExecutionPlan
{
    private ExecutionPlan(
        string id,
        IEnumerable<ExecutionStep> steps,
        IEnumerable<ExecutionValidator> validators,
        IEnumerable<KeyValuePair<string, string>> templateContext)
    {
        Id = id;
        Steps = [.. steps];
        Validators = [.. validators];
        TemplateContext = templateContext.ToImmutableSortedDictionary(StringComparer.Ordinal);
    }

    public string Id { get; }

    public ImmutableArray<ExecutionStep> Steps { get; }

    public ImmutableArray<ExecutionValidator> Validators { get; }

    public ImmutableSortedDictionary<string, string> TemplateContext { get; }

    public static ValidationResult<ExecutionPlan> Create(
        string? id,
        IEnumerable<ExecutionStep?>? steps)
    {
        return Create(id, steps, [], []);
    }

    public static ValidationResult<ExecutionPlan> Create(
        string? id,
        IEnumerable<ExecutionStep?>? steps,
        IEnumerable<ExecutionValidator?>? validators)
    {
        return Create(id, steps, validators, []);
    }

    public static ValidationResult<ExecutionPlan> Create(
        string? id,
        IEnumerable<ExecutionStep?>? steps,
        IEnumerable<ExecutionValidator?>? validators,
        IEnumerable<KeyValuePair<string, string?>>? templateContext)
    {
        var issues = new List<ValidationIssue>();
        if (string.IsNullOrWhiteSpace(id))
        {
            issues.Add(new ValidationIssue("plan.id.required", "Execution plan identifier is required.", "id"));
        }

        var stepsSnapshot = steps?.ToImmutableArray() ?? [];
        var validatorsSnapshot = validators?.ToImmutableArray() ?? [];
        var contextSnapshot = templateContext?.ToImmutableArray() ?? [];
        if (steps is null)
        {
            issues.Add(new ValidationIssue("plan.steps.required", "Execution plan steps are required.", "steps"));
        }
        else
        {
            var stepIds = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < stepsSnapshot.Length; index++)
            {
                var step = stepsSnapshot[index];
                if (step is null)
                {
                    issues.Add(
                        new ValidationIssue(
                            "plan.step.required",
                            "Execution plan steps cannot contain null values.",
                            $"steps[{index}]"));
                }
                else if (!stepIds.Add(step.Id))
                {
                    issues.Add(
                        new ValidationIssue(
                            "plan.step.id.duplicate",
                            "Execution plan step identifiers must be unique.",
                            $"steps[{index}].id"));
                }
            }
        }

        if (validators is null)
        {
            issues.Add(new ValidationIssue(
                "plan.validators.required",
                "Execution plan validators are required.",
                "validators"));
        }
        else
        {
            var validatorIds = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < validatorsSnapshot.Length; index++)
            {
                var validator = validatorsSnapshot[index];
                if (validator is null)
                {
                    issues.Add(new ValidationIssue(
                        "plan.validator.required",
                        "Execution plan validators cannot contain null values.",
                        $"validators[{index}]"));
                }
                else if (!validatorIds.Add(validator.Id))
                {
                    issues.Add(new ValidationIssue(
                        "plan.validator.id.duplicate",
                        "Execution plan validator identifiers must be unique.",
                        $"validators[{index}].id"));
                }
            }
        }

        ValidateTemplateContext(templateContext, contextSnapshot, issues);

        return issues.Count == 0
            ? ValidationResult.Success(new ExecutionPlan(
                id!.Trim(),
                stepsSnapshot.Select(step => step!),
                validatorsSnapshot.Select(validator => validator!),
                contextSnapshot.Select(item => KeyValuePair.Create(item.Key, item.Value!))))
            : ValidationResult.Failure<ExecutionPlan>(issues);
    }

    private static void ValidateTemplateContext(
        IEnumerable<KeyValuePair<string, string?>>? source,
        ImmutableArray<KeyValuePair<string, string?>> snapshot,
        List<ValidationIssue> issues)
    {
        if (source is null)
        {
            issues.Add(new ValidationIssue(
                "plan.template-context.required",
                "The deterministic template context is required.",
                "templateContext"));
            return;
        }

        if (snapshot.Length > 256)
        {
            issues.Add(new ValidationIssue(
                "plan.template-context.too-many",
                "The deterministic template context exceeds the supported entry limit.",
                "templateContext"));
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        long totalCharacters = 0;
        for (var index = 0; index < snapshot.Length; index++)
        {
            var item = snapshot[index];
            if (!IsTemplateContextName(item.Key)
                || !names.Add(item.Key)
                || RedactedText.IsSecretShapedKey(item.Key))
            {
                issues.Add(new ValidationIssue(
                    "plan.template-context.name.invalid",
                    "A deterministic template context name is invalid or duplicated.",
                    $"templateContext[{index}].name"));
            }

            if (item.Value is null
                || item.Value.Length > 64 * 1024
                || item.Value.Contains('\0')
                || RedactedText.IsSecretShapedValue(item.Value))
            {
                issues.Add(new ValidationIssue(
                    "plan.template-context.value.invalid",
                    "A deterministic template context value is invalid or unsafe.",
                    $"templateContext[{index}].value"));
            }

            totalCharacters += item.Value?.Length ?? 0;
        }

        if (totalCharacters > 2L * 1024L * 1024L)
        {
            issues.Add(new ValidationIssue(
                "plan.template-context.total.too-large",
                "The deterministic template context exceeds the supported total size.",
                "templateContext"));
        }

        var orderedNames = names.Order(StringComparer.Ordinal).ToArray();
        for (var index = 1; index < orderedNames.Length; index++)
        {
            if (orderedNames[index].StartsWith(orderedNames[index - 1] + '.', StringComparison.Ordinal))
            {
                issues.Add(new ValidationIssue(
                    "plan.template-context.name.conflict",
                    "Template context names cannot be both a value and a parent path.",
                    "templateContext"));
            }
        }
    }

    private static bool IsTemplateContextName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value != value.Trim()
            || value.Length > 256)
        {
            return false;
        }

        return value.Split('.').All(segment => segment.Length > 0
            && (char.IsAsciiLetter(segment[0]) || segment[0] == '_')
            && segment.Skip(1).All(character => char.IsAsciiLetterOrDigit(character) || character == '_'));
    }
}

public sealed class ExecutionValidator
{
    private ExecutionValidator(
        string id,
        string handler,
        IEnumerable<KeyValuePair<string, PlanValue>> inputs,
        TimeSpan timeout,
        bool required)
    {
        Id = id;
        Handler = handler;
        Inputs = inputs.ToImmutableDictionary(StringComparer.Ordinal);
        Timeout = timeout;
        Required = required;
    }

    public string Id { get; }

    public string Handler { get; }

    public ImmutableDictionary<string, PlanValue> Inputs { get; }

    public TimeSpan Timeout { get; }

    public bool Required { get; }

    public static ValidationResult<ExecutionValidator> Create(
        string? id,
        string? handler,
        IEnumerable<KeyValuePair<string, PlanValue?>>? inputs,
        TimeSpan timeout,
        bool required)
    {
        var issues = new List<ValidationIssue>();
        if (string.IsNullOrWhiteSpace(id))
        {
            issues.Add(new ValidationIssue(
                "validator.id.required",
                "Execution validator identifier is required.",
                "id"));
        }

        if (string.IsNullOrWhiteSpace(handler))
        {
            issues.Add(new ValidationIssue(
                "validator.handler.required",
                "Execution validator handler is required.",
                "handler"));
        }

        var snapshot = inputs?.ToImmutableArray() ?? [];
        if (inputs is null)
        {
            issues.Add(new ValidationIssue(
                "validator.inputs.required",
                "Execution validator inputs are required.",
                "inputs"));
        }
        else
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < snapshot.Length; index++)
            {
                var input = snapshot[index];
                var name = input.Key?.Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    issues.Add(new ValidationIssue(
                        "validator.input.name.required",
                        "An execution validator input name is required.",
                        $"inputs[{index}].name"));
                }
                else if (!names.Add(name))
                {
                    issues.Add(new ValidationIssue(
                        "validator.input.name.duplicate",
                        "Execution validator input names must be unique.",
                        $"inputs[{index}].name"));
                }

                if (input.Value is null)
                {
                    issues.Add(new ValidationIssue(
                        "validator.input.value.required",
                        "An execution validator input value is required.",
                        $"inputs[{index}].value"));
                }
            }
        }

        if (timeout <= TimeSpan.Zero)
        {
            issues.Add(new ValidationIssue(
                "validator.timeout.invalid",
                "Execution validator timeout must be positive.",
                "timeout"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new ExecutionValidator(
                id!.Trim(),
                handler!.Trim(),
                snapshot.Select(item => KeyValuePair.Create(item.Key.Trim(), item.Value!)),
                timeout,
                required))
            : ValidationResult.Failure<ExecutionValidator>(issues);
    }
}
