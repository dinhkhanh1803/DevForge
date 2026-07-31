using DevForge.Domain.Validation;

namespace DevForge.Domain.Execution;

public sealed record RetryPolicy
{
    private RetryPolicy(int maxAttempts, TimeSpan delay, double backoffMultiplier)
    {
        MaxAttempts = maxAttempts;
        Delay = delay;
        BackoffMultiplier = backoffMultiplier;
    }

    public static RetryPolicy None { get; } = new(1, TimeSpan.Zero, 1);

    public int MaxAttempts { get; }

    public TimeSpan Delay { get; }

    public double BackoffMultiplier { get; }

    public static ValidationResult<RetryPolicy> Create(
        int maxAttempts,
        TimeSpan delay,
        double backoffMultiplier = 1)
    {
        var issues = new List<ValidationIssue>();
        if (maxAttempts < 1)
        {
            issues.Add(
                new ValidationIssue(
                    "retry.max-attempts.invalid",
                    "Maximum attempts must be at least one.",
                    "maxAttempts"));
        }

        if (delay < TimeSpan.Zero)
        {
            issues.Add(
                new ValidationIssue(
                    "retry.delay.invalid",
                    "Retry delay cannot be negative.",
                    "delay"));
        }

        if (backoffMultiplier < 1 || double.IsNaN(backoffMultiplier) || double.IsInfinity(backoffMultiplier))
        {
            issues.Add(
                new ValidationIssue(
                    "retry.backoff.invalid",
                    "Backoff multiplier must be a finite value of at least one.",
                    "backoffMultiplier"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new RetryPolicy(maxAttempts, delay, backoffMultiplier))
            : ValidationResult.Failure<RetryPolicy>(issues);
    }
}
