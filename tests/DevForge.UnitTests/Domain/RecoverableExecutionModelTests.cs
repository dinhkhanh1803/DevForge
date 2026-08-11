using DevForge.Domain.Diagnostics;
using DevForge.Domain.Execution;
using DevForge.Domain.Privacy;
using DevForge.Domain.Reports;
using DevForge.Domain.Runs;

namespace DevForge.UnitTests.Domain;

public sealed class RecoverableExecutionModelTests
{
    [Fact]
    public void RetryModesAreExplicitAndPoliciesAreBounded()
    {
        Assert.Equal(["None", "Manual", "AutomaticLimited"], Enum.GetNames<RetryMode>());
        Assert.Equal([1, 2, 3], Enum.GetValues<RetryMode>().Select(value => (int)value));

        var none = RetryPolicy.Create(RetryMode.None, 1, TimeSpan.Zero, 1);
        var manual = RetryPolicy.Create(RetryMode.Manual, 3, TimeSpan.Zero, 1);
        var automatic = RetryPolicy.Create(
            RetryMode.AutomaticLimited,
            3,
            TimeSpan.FromSeconds(2),
            2);

        Assert.True(none.IsValid);
        Assert.True(manual.IsValid);
        Assert.True(automatic.IsValid);
        Assert.Equal(RetryMode.None, none.Value.Mode);
        Assert.Equal(RetryMode.Manual, manual.Value.Mode);
        Assert.Equal(RetryMode.AutomaticLimited, automatic.Value.Mode);
        Assert.False(manual.Value.IsAutomatic);
        Assert.True(automatic.Value.IsAutomatic);
    }

    [Theory]
    [InlineData(RetryMode.None, 2, 0, 1)]
    [InlineData(RetryMode.Manual, 1, 0, 1)]
    [InlineData(RetryMode.Manual, 3, 1, 1)]
    [InlineData(RetryMode.AutomaticLimited, 1, 1, 1)]
    [InlineData(RetryMode.AutomaticLimited, 3, 0, 1)]
    [InlineData(RetryMode.AutomaticLimited, 3, 1, 0.5)]
    [InlineData((RetryMode)999, 3, 1, 1)]
    public void RetryPolicyRejectsModeInconsistentOrUnboundedValues(
        RetryMode mode,
        int attempts,
        int delaySeconds,
        double backoff)
    {
        var result = RetryPolicy.Create(mode, attempts, TimeSpan.FromSeconds(delaySeconds), backoff);

        Assert.False(result.IsValid);
        Assert.All(result.Issues, issue => Assert.StartsWith("retry.", issue.Code, StringComparison.Ordinal));
    }

    [Fact]
    public void CompletedAttemptCarriesOnlyCanonicalLowercaseOutputDigest()
    {
        var startedAt = DateTimeOffset.UnixEpoch;
        var digest = $"sha256:{new string('a', 64)}";

        var valid = StepAttempt.Rehydrate(
            "build",
            1,
            startedAt,
            startedAt.AddSeconds(1),
            StepAttemptOutcome.Succeeded,
            0,
            null,
            digest);
        var runningWithDigest = StepAttempt.Rehydrate(
            "build",
            1,
            startedAt,
            null,
            StepAttemptOutcome.Running,
            null,
            null,
            digest);
        var uppercase = StepAttempt.Rehydrate(
            "build",
            1,
            startedAt,
            startedAt.AddSeconds(1),
            StepAttemptOutcome.Succeeded,
            0,
            null,
            $"sha256:{new string('A', 64)}");

        Assert.True(valid.IsValid);
        Assert.Equal(digest, valid.Value.OutputDigest);
        Assert.Contains(runningWithDigest.Issues, issue => issue.Code == "attempt.running.output-digest-unexpected");
        Assert.Contains(uppercase.Issues, issue => issue.Code == "attempt.output-digest.invalid");
    }

    [Fact]
    public void InterruptedRunningAttemptClosesWithRetryableEvidence()
    {
        var startedAt = DateTimeOffset.UnixEpoch;
        var run = ExecutingRun().StartAttempt("install", startedAt).Value;
        var error = Error("DF-EXEC-003", retryable: true, "install");

        var result = run.InterruptCurrentAttempt(
            startedAt.AddMinutes(1),
            error,
            $"sha256:{new string('b', 64)}");

        Assert.True(result.IsValid);
        Assert.Equal(RunStatus.Executing, result.Value.Status);
        Assert.Null(result.Value.CurrentStepId);
        var attempt = Assert.Single(result.Value.Attempts);
        Assert.Equal(StepAttemptOutcome.Failed, attempt.Outcome);
        Assert.Same(error, attempt.Error);
        Assert.Same(error, Assert.Single(result.Value.Errors));
    }

    [Fact]
    public void InterruptionRequiresExecutingRunningAttemptAndRetryableInterruptedError()
    {
        var time = DateTimeOffset.UnixEpoch;
        var noAttempt = ExecutingRun().InterruptCurrentAttempt(
            time,
            Error("DF-EXEC-003", retryable: true),
            null);
        var wrongError = ExecutingRun().StartAttempt("build", time).Value.InterruptCurrentAttempt(
            time.AddSeconds(1),
            Error("DF-EXEC-001", retryable: false, "build"),
            null);

        Assert.False(noAttempt.IsValid);
        Assert.False(wrongError.IsValid);
        Assert.Equal("run.interruption.attempt-required", Assert.Single(noAttempt.Issues).Code);
        Assert.Equal("run.interruption.error.invalid", Assert.Single(wrongError.Issues).Code);
    }

    [Theory]
    [InlineData(RunStatus.Planning)]
    [InlineData(RunStatus.Cancelled)]
    [InlineData(RunStatus.ValidationFailed)]
    public void ExplicitResumeReturnsRecoverableRunsToExecuting(RunStatus status)
    {
        var run = status switch
        {
            RunStatus.Planning => ProjectRun.Create("run", "recipe").Value
                .TransitionTo(RunStatus.Planning).Value,
            RunStatus.Cancelled => ProjectRun.Create("run", "recipe").Value
                .TransitionTo(RunStatus.Cancelled).Value,
            _ => ExecutingRun().TransitionTo(RunStatus.ValidationFailed).Value,
        };

        var result = run.ResumeExecution();

        Assert.True(result.IsValid);
        Assert.Equal(RunStatus.Executing, result.Value.Status);
        Assert.Equal(run.Attempts, result.Value.Attempts);
    }

    [Fact]
    public void ExplicitResumeAcceptsAnyIdleExecutingCheckpointButNeverARunningAttempt()
    {
        var time = DateTimeOffset.UnixEpoch;
        var interrupted = ExecutingRun().StartAttempt("build", time).Value
            .InterruptCurrentAttempt(time.AddSeconds(1), Error("DF-EXEC-003", true, "build"), null).Value;
        var running = ExecutingRun().StartAttempt("build", time).Value;
        var resumedAndProgressed = interrupted.ResumeExecution().Value
            .StartAttempt("validate", time.AddSeconds(2)).Value
            .CompleteAttempt(
                "validate",
                1,
                StepAttemptOutcome.Succeeded,
                time.AddSeconds(3),
                null,
                null).Value;
        var completed = ProjectRun.Create("run", "recipe").Value
            .TransitionTo(RunStatus.Planning).Value
            .TransitionTo(RunStatus.Executing).Value
            .TransitionTo(RunStatus.LocalReady).Value;

        Assert.True(interrupted.ResumeExecution().IsValid);
        Assert.False(running.ResumeExecution().IsValid);
        Assert.True(resumedAndProgressed.ResumeExecution().IsValid);
        Assert.False(completed.ResumeExecution().IsValid);
    }

    [Theory]
    [InlineData(RunStatus.ValidationFailed, true)]
    [InlineData(RunStatus.Cancelled, true)]
    [InlineData(RunStatus.Failed, true)]
    [InlineData(RunStatus.Draft, false)]
    [InlineData(RunStatus.Executing, false)]
    [InlineData(RunStatus.LocalReady, false)]
    [InlineData(RunStatus.PublishPending, false)]
    [InlineData(RunStatus.Completed, false)]
    public void CleanupEligibilityNeverIncludesLiveOrFinalizedStates(RunStatus status, bool expected)
    {
        Assert.Equal(expected, Reach(status).AllowsStagingCleanup);
    }

    [Fact]
    public void GenerationReportAcceptsExplicitWarningValidationEvidence()
    {
        Assert.Equal(4, (int)ValidationCheckStatus.Warning);
        var result = GenerationReport.Create(
            "run",
            DateTimeOffset.UnixEpoch,
            [new ValidationCheck("optional-lint", ValidationCheckStatus.Warning, "Optional lint warned.", null)],
            [],
            []);

        Assert.True(result.IsValid);
        Assert.Equal(ValidationCheckStatus.Warning, Assert.Single(result.Value.Validations).Status);
    }

    private static ProjectRun ExecutingRun() => ProjectRun.Create("run", "recipe").Value
        .TransitionTo(RunStatus.Planning).Value
        .TransitionTo(RunStatus.Executing).Value;

    private static ProjectRun Reach(RunStatus status)
    {
        var run = ProjectRun.Create("run", "recipe").Value;
        var path = status switch
        {
            RunStatus.Draft => Array.Empty<RunStatus>(),
            RunStatus.Planning => [RunStatus.Planning],
            RunStatus.PreflightFailed => [RunStatus.Planning, RunStatus.PreflightFailed],
            RunStatus.Executing => [RunStatus.Planning, RunStatus.Executing],
            RunStatus.ValidationFailed => [RunStatus.Planning, RunStatus.Executing, RunStatus.ValidationFailed],
            RunStatus.LocalReady => [RunStatus.Planning, RunStatus.Executing, RunStatus.LocalReady],
            RunStatus.PublishPending => [RunStatus.Planning, RunStatus.Executing, RunStatus.LocalReady, RunStatus.PublishPending],
            RunStatus.Completed => [RunStatus.Planning, RunStatus.Executing, RunStatus.LocalReady, RunStatus.Completed],
            RunStatus.Cancelled => [RunStatus.Cancelled],
            RunStatus.Failed => [RunStatus.Failed],
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };
        foreach (var next in path)
        {
            run = run.TransitionTo(next).Value;
        }

        return run;
    }

    private static DevForgeError Error(string code, bool retryable, string? stepId = null)
    {
        return DevForgeError.Create(
            code,
            "Execution was interrupted.",
            RedactedText.FromTrustedRedaction("The prior process is no longer active.").Value,
            "execution",
            stepId,
            retryable,
            ["Resume after verifying the checkpoint."],
            []).Value;
    }
}
