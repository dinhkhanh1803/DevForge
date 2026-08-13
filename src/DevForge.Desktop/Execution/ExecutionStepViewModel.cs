using DevForge.Domain.Runs;

namespace DevForge.Desktop.Execution;

public sealed record ExecutionStepViewModel(
    string Id,
    string DisplayName,
    string StatusLabel,
    string StatusGlyph,
    int? AttemptNumber,
    TimeSpan? Duration,
    string? ErrorCode,
    string? ErrorSummary,
    string? Remediation,
    bool CanOpenStaging,
    bool CanCreateSupportBundle)
{
    public static ExecutionStepViewModel From(
        string id,
        string displayName,
        StepAttempt? attempt)
    {
        var status = attempt?.Outcome switch
        {
            StepAttemptOutcome.Running => ("RUNNING", "▶"),
            StepAttemptOutcome.Succeeded => ("SUCCEEDED", "✓"),
            StepAttemptOutcome.Failed => ("FAILED", "✕"),
            StepAttemptOutcome.Cancelled => ("CANCELLED", "■"),
            _ => ("PENDING", "○"),
        };
        TimeSpan? duration = attempt?.CompletedAt is null
            ? null
            : attempt.CompletedAt.Value - attempt.StartedAt;
        return new ExecutionStepViewModel(
            id,
            displayName,
            status.Item1,
            status.Item2,
            attempt?.AttemptNumber,
            duration,
            attempt?.Error?.Code,
            attempt?.Error?.Summary,
            attempt?.Error?.SuggestedActions.FirstOrDefault(),
            CanOpenStaging: false,
            CanCreateSupportBundle: false);
    }
}
