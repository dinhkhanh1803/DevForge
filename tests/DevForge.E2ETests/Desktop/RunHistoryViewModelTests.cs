using DevForge.Application.Contracts;
using DevForge.Desktop.RunHistory;
using DevForge.Domain.Diagnostics;
using DevForge.Domain.Privacy;
using DevForge.Domain.Runs;

namespace DevForge.E2ETests.Desktop;

public sealed class RunHistoryViewModelTests
{
    [Fact]
    public async Task SafeModeIsForwardedToApplicationPublicationBoundary()
    {
        var publication = new RecordingPublication();
        var actions = new RunHistoryActionCoordinator(new UnusedRecoveryWorkflow(), publication);
        actions.EnterReadOnlyMode();

        await actions.RetryPublicationAsync(
            $"run-{new string('1', 32)}",
            CancellationToken.None);

        Assert.Equal(PublicationMutationMode.SafeReadOnly, publication.Mode);
    }

    [Theory]
    [InlineData(RunStatus.Planning, true, false, false, false)]
    [InlineData(RunStatus.Cancelled, true, false, true, false)]
    [InlineData(RunStatus.ValidationFailed, true, false, true, false)]
    [InlineData(RunStatus.LocalReady, false, false, false, false)]
    [InlineData(RunStatus.PublishPending, false, false, false, true)]
    [InlineData(RunStatus.Completed, false, false, false, false)]
    public void HistoryActionsFollowDomainEligibility(
        RunStatus status,
        bool canResume,
        bool canRetry,
        bool canCleanup,
        bool canRetryPublish)
    {
        var run = ProjectRun.Rehydrate("run-1", "recipe-1", status, null, [], []).Value;

        var item = RunHistoryItemViewModel.From(
            run,
            new ProjectRecoveryEligibility(canResume, canRetry, canCleanup));

        Assert.Equal(canResume, item.CanResume);
        Assert.Equal(canRetry, item.CanRetry);
        Assert.Equal(canCleanup, item.CanCleanup);
        Assert.Equal(canRetryPublish, item.CanRetryPublish);
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

        var item = RunHistoryItemViewModel.From(
            run,
            new ProjectRecoveryEligibility(true, true, false));

        Assert.True(item.CanResume);
        Assert.True(item.CanRetry);
        Assert.Equal("DF-EXEC-003", item.ErrorCode);
    }

    private sealed class RecordingPublication : IProjectPublicationWorkflow
    {
        public PublicationMutationMode? Mode { get; private set; }

        public Task<ExecutionOperationResult<ProjectPublicationOutcome>> CompleteAsync(
            string runId,
            PublicationMutationMode mutationMode,
            CancellationToken cancellationToken)
        {
            Mode = mutationMode;
            return Task.FromResult(ExecutionOperationResult.Failure<ProjectPublicationOutcome>(
                DevForgeError.Create(
                    "DF-PUB-READONLY",
                    "Publication is disabled in safe mode.",
                    RedactedText.FromTrustedRedaction("Publication is disabled in safe mode.").Value,
                    "publication",
                    stepId: null,
                    isRetryable: false,
                    suggestedActions: [],
                    redactedContext: []).Value));
        }
    }

    private sealed class UnusedRecoveryWorkflow : IProjectRecoveryWorkflow
    {
        public Task<ProjectRecoveryEligibility> InspectAsync(RunCheckpoint checkpoint, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ProjectRecoverySnapshot> ContinueAsync(string runId, ExecutionMode mode, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task CleanupAsync(string runId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
