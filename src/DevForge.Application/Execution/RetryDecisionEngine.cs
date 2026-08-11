using DevForge.Application.Contracts;
using DevForge.Domain.Diagnostics;
using DevForge.Domain.Execution;

namespace DevForge.Application.Execution;

public enum RetryAction
{
    Stop = 1,
    AwaitManualRetry = 2,
    RetryCurrentStaging = 3,
    ReplayFromFreshStaging = 4,
}

public readonly record struct RetryDecision(RetryAction Action, TimeSpan Delay);

public static class RetryDecisionEngine
{
    public static RetryDecision Decide(
        RetryPolicy policy,
        int attemptNumber,
        DevForgeError error,
        ExecutionResumeBehavior resumeBehavior)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(error);
        if (attemptNumber is < 1 or > RetryPolicy.MaximumSupportedAttempts)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptNumber));
        }

        if (!Enum.IsDefined(resumeBehavior))
        {
            throw new ArgumentOutOfRangeException(nameof(resumeBehavior));
        }

        if (!error.IsRetryable
            || policy.Mode == RetryMode.None
            || attemptNumber >= policy.MaxAttempts)
        {
            return new RetryDecision(RetryAction.Stop, TimeSpan.Zero);
        }

        if (policy.Mode == RetryMode.Manual)
        {
            return new RetryDecision(RetryAction.AwaitManualRetry, TimeSpan.Zero);
        }

        var delay = BoundedDelay(policy, attemptNumber);
        return new RetryDecision(
            resumeBehavior == ExecutionResumeBehavior.ReplayFromFreshStaging
                ? RetryAction.ReplayFromFreshStaging
                : RetryAction.RetryCurrentStaging,
            delay);
    }

    private static TimeSpan BoundedDelay(RetryPolicy policy, int attemptNumber)
    {
        var multiplier = Math.Pow(policy.BackoffMultiplier, attemptNumber - 1);
        var ticks = policy.Delay.Ticks * multiplier;
        return ticks >= RetryPolicy.MaximumDelay.Ticks
            ? RetryPolicy.MaximumDelay
            : TimeSpan.FromTicks((long)ticks);
    }
}
