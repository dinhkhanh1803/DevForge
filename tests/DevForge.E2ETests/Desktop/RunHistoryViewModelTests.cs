using DevForge.Desktop.RunHistory;
using DevForge.Domain.Diagnostics;
using DevForge.Domain.Privacy;
using DevForge.Domain.Runs;

namespace DevForge.E2ETests.Desktop;

public sealed class RunHistoryViewModelTests
{
    [Theory]
    [InlineData(RunStatus.Planning, true, false, false)]
    [InlineData(RunStatus.Cancelled, true, false, true)]
    [InlineData(RunStatus.ValidationFailed, true, false, true)]
    [InlineData(RunStatus.LocalReady, false, false, false)]
    [InlineData(RunStatus.Completed, false, false, false)]
    public void HistoryActionsFollowDomainEligibility(
        RunStatus status,
        bool canResume,
        bool canRetry,
        bool canCleanup)
    {
        var run = ProjectRun.Rehydrate("run-1", "recipe-1", status, null, [], []).Value;

        var item = RunHistoryItemViewModel.From(run);

        Assert.Equal(canResume, item.CanResume);
        Assert.Equal(canRetry, item.CanRetry);
        Assert.Equal(canCleanup, item.CanCleanup);
        Assert.NotEqual("SUCCESS", item.StatusLabel);
    }

    [Fact]
    public void RetryRequiresIdleExecutingRetryableFailure()
    {
        var error = DevForgeError.Create(
            "DF-EXEC-003", "Interrupted.",
            RedactedText.FromTrustedRedaction("Safe detail.").Value,
            "Execute", "create", true, ["Resume the run."], []).Value;
        var attempt = StepAttempt.Rehydrate(
            "create", 1, DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddSeconds(1),
            StepAttemptOutcome.Failed, null, error).Value;
        var run = ProjectRun.Rehydrate(
            "run-1", "recipe-1", RunStatus.Executing, null, [attempt], [error]).Value;

        var item = RunHistoryItemViewModel.From(run);

        Assert.True(item.CanResume);
        Assert.True(item.CanRetry);
        Assert.Equal("DF-EXEC-003", item.ErrorCode);
    }
}
