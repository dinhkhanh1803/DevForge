using DevForge.Domain.Diagnostics;
using DevForge.Domain.Privacy;
using DevForge.Domain.Runs;

namespace DevForge.UnitTests.Domain;

public sealed class ProjectRunTests
{
    [Fact]
    public void RunStatusDefinesTheExactRequiredLifecycleVocabulary()
    {
        Assert.Equal(
            [
                "Draft",
                "Planning",
                "PreflightFailed",
                "Executing",
                "ValidationFailed",
                "LocalReady",
                "PublishPending",
                "Completed",
                "Cancelled",
                "Failed",
            ],
            Enum.GetNames<RunStatus>());
    }

    [Fact]
    public void TransitionToAllowsTheDocumentedHappyPath()
    {
        var run = ProjectRun.Create("run-1", "recipe-1").Value;

        foreach (var status in new[]
                 {
                     RunStatus.Planning,
                     RunStatus.Executing,
                     RunStatus.LocalReady,
                     RunStatus.PublishPending,
                     RunStatus.Completed,
                 })
        {
            var transition = run.TransitionTo(status);
            Assert.True(transition.IsValid);
            run = transition.Value;
        }

        Assert.Equal(RunStatus.Completed, run.Status);
    }

    [Fact]
    public void TransitionToRejectsUndocumentedAndTerminalTransitions()
    {
        var run = ProjectRun.Create("run-1", "recipe-1").Value;

        var skipped = run.TransitionTo(RunStatus.Completed);
        var cancelled = run.TransitionTo(RunStatus.Cancelled).Value;
        var restarted = cancelled.TransitionTo(RunStatus.Planning);

        Assert.False(skipped.IsValid);
        Assert.Equal("run.transition.invalid", Assert.Single(skipped.Issues).Code);
        Assert.False(restarted.IsValid);
    }

    [Fact]
    public void TransitionToRejectsUndefinedStatus()
    {
        var run = ProjectRun.Create("run-1", "recipe-1").Value;

        var result = run.TransitionTo((RunStatus)999);

        Assert.False(result.IsValid);
        Assert.Equal("run.status.invalid", Assert.Single(result.Issues).Code);
    }

    [Theory]
    [InlineData(RunStatus.Draft)]
    [InlineData(RunStatus.Planning)]
    [InlineData(RunStatus.Executing)]
    [InlineData(RunStatus.LocalReady)]
    [InlineData(RunStatus.PublishPending)]
    public void ActiveStatesAllowCancellationAndFailure(RunStatus activeStatus)
    {
        var run = ReachStatus(activeStatus);

        Assert.True(run.TransitionTo(RunStatus.Cancelled).IsValid);
        Assert.True(run.TransitionTo(RunStatus.Failed).IsValid);
    }

    [Theory]
    [InlineData(RunStatus.PreflightFailed)]
    [InlineData(RunStatus.ValidationFailed)]
    [InlineData(RunStatus.Completed)]
    [InlineData(RunStatus.Cancelled)]
    [InlineData(RunStatus.Failed)]
    public void TerminalStatesRejectEveryTransition(RunStatus terminalStatus)
    {
        var run = ReachStatus(terminalStatus);

        Assert.All(
            Enum.GetValues<RunStatus>(),
            next => Assert.False(run.TransitionTo(next).IsValid));
    }

    [Fact]
    public void StepAttemptRehydrateAggregatesInvalidInvariants()
    {
        var startedAt = DateTimeOffset.UtcNow;

        var result = StepAttempt.Rehydrate(
            " ",
            0,
            startedAt,
            startedAt.AddSeconds(-1),
            (StepAttemptOutcome)999,
            null,
            null);

        Assert.False(result.IsValid);
        Assert.Equal(
            [
                "attempt.step-id.required",
                "attempt.number.invalid",
                "attempt.completed-at.invalid",
                "attempt.outcome.invalid",
            ],
            result.Issues.Select(issue => issue.Code));
    }

    [Theory]
    [InlineData(StepAttemptOutcome.Running, true, null, false, "attempt.running.inconsistent")]
    [InlineData(StepAttemptOutcome.Succeeded, false, null, false, "attempt.succeeded.inconsistent")]
    [InlineData(StepAttemptOutcome.Failed, true, 1, false, "attempt.failed.error-required")]
    [InlineData(StepAttemptOutcome.Cancelled, true, null, true, "attempt.cancelled.error-unexpected")]
    public void StepAttemptRehydrateRejectsOutcomeInconsistency(
        StepAttemptOutcome outcome,
        bool hasCompletedAt,
        int? exitCode,
        bool includeError,
        string expectedCode)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var result = StepAttempt.Rehydrate(
            "build",
            1,
            startedAt,
            hasCompletedAt ? startedAt.AddSeconds(1) : null,
            outcome,
            exitCode,
            includeError ? SafeError() : null);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == expectedCode);
    }

    [Fact]
    public void ProjectRunCreationIsGuardedAndDoesNotAcceptHistory()
    {
        var result = ProjectRun.Create(" ", " ");

        Assert.False(result.IsValid);
        Assert.Equal(["run.id.required", "run.recipe-id.required"], result.Issues.Select(issue => issue.Code));
    }

    [Fact]
    public void ProjectRunEvolvesAttemptAndErrorHistoryImmutably()
    {
        var startedAt = DateTimeOffset.UtcNow;
        var original = ReachStatus(RunStatus.Executing);

        var started = original.StartAttempt("build", startedAt).Value;
        var completed = started.CompleteAttempt(
            "build",
            1,
            StepAttemptOutcome.Succeeded,
            startedAt.AddSeconds(1),
            0,
            null).Value;
        var withError = completed.AppendError(SafeError()).Value;

        Assert.Empty(original.Attempts);
        Assert.Equal(StepAttemptOutcome.Running, Assert.Single(started.Attempts).Outcome);
        Assert.Equal("build", started.CurrentStepId);
        Assert.Null(completed.CurrentStepId);
        Assert.Equal(StepAttemptOutcome.Succeeded, Assert.Single(completed.Attempts).Outcome);
        Assert.Single(withError.Errors);
        Assert.Empty(completed.Errors);
    }

    [Fact]
    public void StartAttemptUsesTheNextNumberAfterSparseRehydratedHistory()
    {
        var startedAt = DateTimeOffset.UtcNow;
        var prior = StepAttempt.Rehydrate(
            "build",
            2,
            startedAt,
            startedAt.AddSeconds(1),
            StepAttemptOutcome.Succeeded,
            0,
            null).Value;
        var run = ProjectRun.Rehydrate(
            "run-1",
            "recipe-1",
            RunStatus.Executing,
            null,
            [prior],
            []).Value;

        var result = run.StartAttempt("build", startedAt.AddSeconds(2));

        Assert.True(result.IsValid);
        Assert.Equal(3, result.Value.Attempts[^1].AttemptNumber);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void SucceededAttemptAllowsOptionalExitCode(int? exitCode)
    {
        var startedAt = DateTimeOffset.UtcNow;

        var result = StepAttempt.Rehydrate(
            "build",
            1,
            startedAt,
            startedAt.AddSeconds(1),
            StepAttemptOutcome.Succeeded,
            exitCode,
            null);

        Assert.True(result.IsValid);
        Assert.Equal(exitCode, result.Value.ExitCode);
    }

    [Fact]
    public void StartAttemptRejectsEveryNonExecutingStatus()
    {
        foreach (var status in Enum.GetValues<RunStatus>().Where(status => status != RunStatus.Executing))
        {
            var result = ReachStatus(status).StartAttempt("build", DateTimeOffset.UtcNow);

            Assert.False(result.IsValid);
            Assert.Equal("run.attempt.start.status", Assert.Single(result.Issues).Code);
        }
    }

    [Fact]
    public void StartAttemptRejectsAnExistingRunningAttempt()
    {
        var run = ReachStatus(RunStatus.Executing)
            .StartAttempt("build", DateTimeOffset.UtcNow).Value;

        var result = run.StartAttempt("test", DateTimeOffset.UtcNow.AddSeconds(1));

        Assert.False(result.IsValid);
        Assert.Equal("run.attempt.running", Assert.Single(result.Issues).Code);
    }

    [Fact]
    public void CompleteAttemptRequiresExecutingStatus()
    {
        var run = ProjectRun.Create("run-1", "recipe-1").Value;

        var result = run.CompleteAttempt(
            "build",
            1,
            StepAttemptOutcome.Succeeded,
            DateTimeOffset.UtcNow,
            null,
            null);

        Assert.False(result.IsValid);
        Assert.Equal("run.attempt.complete.status", Assert.Single(result.Issues).Code);
    }

    [Theory]
    [InlineData("other", 1)]
    [InlineData("build", 2)]
    public void CompleteAttemptRequiresTheMatchingCurrentRunningAttempt(string stepId, int attemptNumber)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var run = ReachStatus(RunStatus.Executing).StartAttempt("build", startedAt).Value;

        var result = run.CompleteAttempt(
            stepId,
            attemptNumber,
            StepAttemptOutcome.Succeeded,
            startedAt.AddSeconds(1),
            null,
            null);

        Assert.False(result.IsValid);
        Assert.Equal("run.attempt.running-not-found", Assert.Single(result.Issues).Code);
    }

    [Theory]
    [InlineData(RunStatus.ValidationFailed)]
    [InlineData(RunStatus.LocalReady)]
    [InlineData(RunStatus.Cancelled)]
    [InlineData(RunStatus.Failed)]
    public void TransitionFromExecutingRejectsEveryLegalExitWhileAnAttemptIsRunning(RunStatus nextStatus)
    {
        var run = ReachStatus(RunStatus.Executing)
            .StartAttempt("build", DateTimeOffset.UtcNow).Value;

        var result = run.TransitionTo(nextStatus);

        Assert.False(result.IsValid);
        Assert.Equal("run.transition.attempt-running", Assert.Single(result.Issues).Code);
    }

    [Theory]
    [InlineData(RunStatus.Draft)]
    [InlineData(RunStatus.PreflightFailed)]
    [InlineData(RunStatus.ValidationFailed)]
    [InlineData(RunStatus.Completed)]
    [InlineData(RunStatus.Cancelled)]
    [InlineData(RunStatus.Failed)]
    public void AppendErrorRejectsDraftAndTerminalHistoryMutation(RunStatus status)
    {
        var result = ReachStatus(status).AppendError(SafeError());

        Assert.False(result.IsValid);
        Assert.Equal("run.error.append.status", Assert.Single(result.Issues).Code);
    }

    [Fact]
    public void RehydrateRejectsDraftHistoryAndCurrentStep()
    {
        var completed = SucceededAttempt("build", 1);

        var result = ProjectRun.Rehydrate(
            "run-1",
            "recipe-1",
            RunStatus.Draft,
            "build",
            [completed],
            []);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "run.draft.history.invalid");
        Assert.Contains(result.Issues, issue => issue.Code == "run.current-step.status.invalid");
    }

    [Fact]
    public void RehydrateRejectsRunningAttemptsOutsideExecuting()
    {
        var running = StepAttempt.Start("build", 1, DateTimeOffset.UtcNow).Value;

        foreach (var status in Enum.GetValues<RunStatus>().Where(status => status != RunStatus.Executing))
        {
            var result = ProjectRun.Rehydrate(
                "run-1",
                "recipe-1",
                status,
                "build",
                [running],
                []);

            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, issue => issue.Code == "run.attempt.running-status.invalid");
        }
    }

    [Fact]
    public void RehydrateRejectsExecutingCurrentStepAndRunningAttemptInconsistencies()
    {
        var runningBuild = StepAttempt.Start("build", 1, DateTimeOffset.UtcNow).Value;
        var runningTest = StepAttempt.Start("test", 1, DateTimeOffset.UtcNow).Value;

        var currentWithoutRunning = ProjectRun.Rehydrate(
            "run-1", "recipe-1", RunStatus.Executing, "build", [], []);
        var runningWithoutCurrent = ProjectRun.Rehydrate(
            "run-1", "recipe-1", RunStatus.Executing, null, [runningBuild], []);
        var mismatchedCurrent = ProjectRun.Rehydrate(
            "run-1", "recipe-1", RunStatus.Executing, "test", [runningBuild], []);
        var multipleRunning = ProjectRun.Rehydrate(
            "run-1", "recipe-1", RunStatus.Executing, "build", [runningBuild, runningTest], []);

        Assert.Contains(currentWithoutRunning.Issues, issue => issue.Code == "run.current-step.running-required");
        Assert.Contains(runningWithoutCurrent.Issues, issue => issue.Code == "run.current-step.required");
        Assert.Contains(mismatchedCurrent.Issues, issue => issue.Code == "run.current-step.mismatch");
        Assert.Contains(multipleRunning.Issues, issue => issue.Code == "run.attempt.running.multiple");
    }

    private static StepAttempt SucceededAttempt(string stepId, int attemptNumber)
    {
        var startedAt = DateTimeOffset.UtcNow;
        return StepAttempt.Rehydrate(
            stepId,
            attemptNumber,
            startedAt,
            startedAt.AddSeconds(1),
            StepAttemptOutcome.Succeeded,
            null,
            null).Value;
    }
    private static ProjectRun ReachStatus(RunStatus status)
    {
        var run = ProjectRun.Create("run-1", "recipe-1").Value;
        RunStatus[] path = status switch
        {
            RunStatus.Draft => [],
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

    private static DevForgeError SafeError()
    {
        return DevForgeError.Create(
            "build.failed",
            "Build failed.",
            RedactedText.FromTrustedRedaction("Compiler returned a redacted error.").Value,
            "validation",
            "build",
            true,
            ["Run the build again."],
            new Dictionary<string, RedactedText>()).Value;
    }
}
