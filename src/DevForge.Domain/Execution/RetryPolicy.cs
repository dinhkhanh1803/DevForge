using DevForge.Domain.Validation;

namespace DevForge.Domain.Execution;

public enum RetryMode
{
    None = 1,
    Manual = 2,
    AutomaticLimited = 3,
}

public sealed record RetryPolicy
{
    public const int MaximumSupportedAttempts = 10;
    public static readonly TimeSpan MaximumDelay = TimeSpan.FromMinutes(5);

    private RetryPolicy(
        RetryMode mode,
        int maxAttempts,
        TimeSpan delay,
        double backoffMultiplier)
    {
        Mode = mode;
        MaxAttempts = maxAttempts;
        Delay = delay;
        BackoffMultiplier = backoffMultiplier;
    }

    public static RetryPolicy None { get; } = new(RetryMode.None, 1, TimeSpan.Zero, 1);

    public RetryMode Mode { get; }

    public int MaxAttempts { get; }

    public TimeSpan Delay { get; }

    public double BackoffMultiplier { get; }

    public bool IsAutomatic => Mode == RetryMode.AutomaticLimited;

    public static ValidationResult<RetryPolicy> Create(
        int maxAttempts,
        TimeSpan delay,
        double backoffMultiplier = 1)
    {
        return Create(RetryMode.AutomaticLimited, maxAttempts, delay, backoffMultiplier);
    }

    public static ValidationResult<RetryPolicy> Create(
        RetryMode mode,
        int maxAttempts,
        TimeSpan delay,
        double backoffMultiplier = 1)
    {
        var issues = new List<ValidationIssue>();
        if (!Enum.IsDefined(mode))
        {
            issues.Add(
                new ValidationIssue(
                    "retry.mode.invalid",
                    "The retry mode is not defined.",
                    "mode"));
        }

        if (maxAttempts < 1 || maxAttempts > MaximumSupportedAttempts)
        {
            issues.Add(
                new ValidationIssue(
                    "retry.max-attempts.invalid",
                    "Maximum attempts must be within the supported bound.",
                    "maxAttempts"));
        }

        if (delay < TimeSpan.Zero || delay > MaximumDelay)
        {
            issues.Add(
                new ValidationIssue(
                    "retry.delay.invalid",
                    "Retry delay must be within the supported bound.",
                    "delay"));
        }

        if (backoffMultiplier < 1
            || backoffMultiplier > 4
            || double.IsNaN(backoffMultiplier)
            || double.IsInfinity(backoffMultiplier))
        {
            issues.Add(
                new ValidationIssue(
                    "retry.backoff.invalid",
                    "Backoff multiplier must be a finite supported value.",
                    "backoffMultiplier"));
        }

        if (issues.Count == 0 && !IsConsistent(mode, maxAttempts, delay, backoffMultiplier))
        {
            issues.Add(
                new ValidationIssue(
                    "retry.policy.inconsistent",
                    "The retry policy values are inconsistent with its mode.",
                    "mode"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new RetryPolicy(mode, maxAttempts, delay, backoffMultiplier))
            : ValidationResult.Failure<RetryPolicy>(issues);
    }

    public static ValidationResult<RetryPolicy> Manual(int maxAttempts = 3)
    {
        return Create(RetryMode.Manual, maxAttempts, TimeSpan.Zero, 1);
    }

    public static ValidationResult<RetryPolicy> AutomaticLimited(
        int maxAttempts,
        TimeSpan delay,
        double backoffMultiplier = 1)
    {
        return Create(RetryMode.AutomaticLimited, maxAttempts, delay, backoffMultiplier);
    }

    private static bool IsConsistent(
        RetryMode mode,
        int maxAttempts,
        TimeSpan delay,
        double backoffMultiplier)
    {
        return mode switch
        {
            RetryMode.None => maxAttempts == 1 && delay == TimeSpan.Zero && backoffMultiplier == 1,
            RetryMode.Manual => maxAttempts >= 2 && delay == TimeSpan.Zero && backoffMultiplier == 1,
            RetryMode.AutomaticLimited => maxAttempts >= 2 && delay > TimeSpan.Zero,
            _ => false,
        };
    }
}
