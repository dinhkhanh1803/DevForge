using DevForge.Application.Contracts;
using DevForge.Application.Execution;
using DevForge.Domain.Diagnostics;
using DevForge.Domain.Execution;
using DevForge.Domain.Privacy;

namespace DevForge.UnitTests.Application.Execution;

public sealed class RetryDecisionEngineTests
{
    [Fact]
    public void NonRetryableAndNonePoliciesStop()
    {
        var permanent = RetryDecisionEngine.Decide(
            RetryPolicy.AutomaticLimited(3, TimeSpan.FromSeconds(1)).Value,
            attemptNumber: 1,
            Error(retryable: false),
            ExecutionResumeBehavior.RevalidatePostcondition);
        var none = RetryDecisionEngine.Decide(
            RetryPolicy.None,
            attemptNumber: 1,
            Error(retryable: true),
            ExecutionResumeBehavior.RevalidatePostcondition);

        Assert.Equal(RetryAction.Stop, permanent.Action);
        Assert.Equal(RetryAction.Stop, none.Action);
        Assert.Equal(TimeSpan.Zero, permanent.Delay);
        Assert.Equal(TimeSpan.Zero, none.Delay);
    }

    [Fact]
    public void ManualPolicyWaitsForExplicitRetryUntilItsBound()
    {
        var policy = RetryPolicy.Manual(3).Value;

        var available = RetryDecisionEngine.Decide(
            policy,
            attemptNumber: 2,
            Error(retryable: true),
            ExecutionResumeBehavior.RevalidatePostcondition);
        var exhausted = RetryDecisionEngine.Decide(
            policy,
            attemptNumber: 3,
            Error(retryable: true),
            ExecutionResumeBehavior.RevalidatePostcondition);

        Assert.Equal(RetryAction.AwaitManualRetry, available.Action);
        Assert.Equal(RetryAction.Stop, exhausted.Action);
    }

    [Fact]
    public void AutomaticPolicyUsesBoundedExponentialDelay()
    {
        var policy = RetryPolicy.AutomaticLimited(
            10,
            TimeSpan.FromMinutes(5),
            backoffMultiplier: 4).Value;

        var first = RetryDecisionEngine.Decide(
            policy,
            attemptNumber: 1,
            Error(retryable: true),
            ExecutionResumeBehavior.RevalidatePostcondition);
        var later = RetryDecisionEngine.Decide(
            policy,
            attemptNumber: 9,
            Error(retryable: true),
            ExecutionResumeBehavior.RevalidatePostcondition);

        Assert.Equal(RetryAction.RetryCurrentStaging, first.Action);
        Assert.Equal(TimeSpan.FromMinutes(5), first.Delay);
        Assert.Equal(RetryPolicy.MaximumDelay, later.Delay);
    }

    [Fact]
    public void OpaqueProcessMutationRequiresFreshStagingReplay()
    {
        var decision = RetryDecisionEngine.Decide(
            RetryPolicy.AutomaticLimited(2, TimeSpan.FromSeconds(1)).Value,
            attemptNumber: 1,
            Error(retryable: true),
            ExecutionResumeBehavior.ReplayFromFreshStaging);

        Assert.Equal(RetryAction.ReplayFromFreshStaging, decision.Action);
        Assert.Equal(TimeSpan.FromSeconds(1), decision.Delay);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(11)]
    public void InvalidAttemptNumbersFailClosed(int attemptNumber)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RetryDecisionEngine.Decide(
                RetryPolicy.AutomaticLimited(2, TimeSpan.FromSeconds(1)).Value,
                attemptNumber,
                Error(retryable: true),
                ExecutionResumeBehavior.RevalidatePostcondition));
    }

    private static DevForgeError Error(bool retryable) => DevForgeError.Create(
        "DF-EXEC-001",
        "Execution failed.",
        RedactedText.FromTrustedRedaction("Scrubbed execution failure detail.").Value,
        "execute",
        "step-1",
        retryable,
        [],
        []).Value;
}
