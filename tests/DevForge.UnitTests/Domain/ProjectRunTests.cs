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
        var original = ProjectRun.Create("run-1", "recipe-1").Value;

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
            [prior],
            []).Value;

        var result = run.StartAttempt("build", startedAt.AddSeconds(2));

        Assert.True(result.IsValid);
        Assert.Equal(3, result.Value.Attempts[^1].AttemptNumber);
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
            SanitizedText.Create("Compiler returned a redacted error.").Value,
            "validation",
            "build",
            true,
            ["Run the build again."],
            new Dictionary<string, SanitizedText>()).Value;
    }
}
