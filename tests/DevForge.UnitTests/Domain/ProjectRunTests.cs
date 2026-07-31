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

    [Fact]
    public void RunSnapshotsAttemptsAndErrors()
    {
        var attempts = new List<StepAttempt>
        {
            new("build", 1, DateTimeOffset.UtcNow, null, StepAttemptOutcome.Running, null, null),
        };
        var errors = new List<DevForgeError>
        {
            new(
                "build.failed",
                "Build failed.",
                "Compiler returned a redacted error.",
                "validation",
                "build",
                true,
                ["Run the build again."],
                new Dictionary<string, string>()),
        };

        var run = ProjectRun.Create("run-1", "recipe-1", attempts, errors);
        attempts.Clear();
        errors.Clear();

        Assert.Single(run.Attempts);
        Assert.Single(run.Errors);
    }
}
