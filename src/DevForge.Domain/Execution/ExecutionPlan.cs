using System.Collections.Immutable;
using DevForge.Domain.Validation;

namespace DevForge.Domain.Execution;

public sealed class ExecutionStep
{
    private ExecutionStep(
        string id,
        string name,
        string handler,
        IEnumerable<KeyValuePair<string, string>> inputs,
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

    public ImmutableDictionary<string, string> Inputs { get; }

    public TimeSpan Timeout { get; }

    public RetryPolicy RetryPolicy { get; }

    public static ValidationResult<ExecutionStep> Create(
        string? id,
        string? name,
        string? handler,
        IEnumerable<KeyValuePair<string, string>>? inputs,
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
                else if (!inputNames.Add(input.Key))
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
                    inputsSnapshot,
                    timeout,
                    retryPolicy!))
            : ValidationResult.Failure<ExecutionStep>(issues);
    }
}

public sealed class ExecutionPlan
{
    private ExecutionPlan(string id, IEnumerable<ExecutionStep> steps)
    {
        Id = id;
        Steps = [.. steps];
    }

    public string Id { get; }

    public ImmutableArray<ExecutionStep> Steps { get; }

    public static ValidationResult<ExecutionPlan> Create(
        string? id,
        IEnumerable<ExecutionStep?>? steps)
    {
        var issues = new List<ValidationIssue>();
        if (string.IsNullOrWhiteSpace(id))
        {
            issues.Add(new ValidationIssue("plan.id.required", "Execution plan identifier is required.", "id"));
        }

        var stepsSnapshot = steps?.ToImmutableArray() ?? [];
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

        return issues.Count == 0
            ? ValidationResult.Success(new ExecutionPlan(id!.Trim(), stepsSnapshot.Select(step => step!)))
            : ValidationResult.Failure<ExecutionPlan>(issues);
    }
}
