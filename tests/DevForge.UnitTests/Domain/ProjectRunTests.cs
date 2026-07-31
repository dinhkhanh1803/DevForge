using DevForge.Domain.Diagnostics;
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
        var run = ProjectRun.Create("run-1", "recipe-1");

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
        var run = ProjectRun.Create("run-1", "recipe-1");

        var skipped = run.TransitionTo(RunStatus.Completed);
        var cancelled = run.TransitionTo(RunStatus.Cancelled).Value;
        var restarted = cancelled.TransitionTo(RunStatus.Planning);

        Assert.False(skipped.IsValid);
        Assert.Equal("run.transition.invalid", Assert.Single(skipped.Issues).Code);
        Assert.False(restarted.IsValid);
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
    public void RunSnapshotsAttemptsAndErrors()
    {
        var attempts = new List<StepAttempt>
        {
            new("build", 1, DateTimeOffset.UtcNow, null, StepAttemptOutcome.Running, null, null),
        };
        var errors = new List<DevForgeError>
        {
            DevForgeError.Create(
                "build.failed",
                "Build failed.",
                "Compiler returned a redacted error.",
                "validation",
                "build",
                true,
                ["Run the build again."],
                new Dictionary<string, string>()).Value,
        };

        var run = ProjectRun.Create("run-1", "recipe-1", attempts, errors);
        attempts.Clear();
        errors.Clear();

        Assert.Single(run.Attempts);
        Assert.Single(run.Errors);
    }

    private static ProjectRun ReachStatus(RunStatus status)
    {
        var run = ProjectRun.Create("run-1", "recipe-1");
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
}
