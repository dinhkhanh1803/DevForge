using DevForge.Domain.Execution;
using DevForge.Domain.Privacy;
using DevForge.Domain.Runs;
using DevForge.Domain.Validation;

namespace DevForge.Application.Contracts;

public sealed class ExecutionProgressLine
{
    private ExecutionProgressLine(string stepId, RedactedText text)
    {
        StepId = stepId;
        Text = text;
    }

    public string StepId { get; }

    public RedactedText Text { get; }

    public static ValidationResult<ExecutionProgressLine> Create(
        string? stepId,
        RedactedText? text)
    {
        var issues = new List<ValidationIssue>();
        if (string.IsNullOrWhiteSpace(stepId))
        {
            issues.Add(
                new ValidationIssue(
                    "execution.progress.step-id.required",
                    "An execution progress step identifier is required.",
                    "stepId"));
        }

        if (text is null)
        {
            issues.Add(
                new ValidationIssue(
                    "execution.progress.text.required",
                    "Redacted execution progress text is required.",
                    "text"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new ExecutionProgressLine(stepId!.Trim(), text!))
            : ValidationResult.Failure<ExecutionProgressLine>(issues);
    }
}

public interface IExecutionOrchestrator
{
    Task<RunCheckpoint> ExecuteAsync(
        ExecutionRequest request,
        IProgress<ExecutionProgressLine>? progress,
        CancellationToken cancellationToken);
}
