using System.Collections.Immutable;
using DevForge.Application.Contracts;
using DevForge.Application.Publication;
using DevForge.Domain.Diagnostics;
using DevForge.Domain.Privacy;

namespace DevForge.E2ETests.Desktop;

public sealed class ProjectPublicationWorkflowDesktopTests
{
    [Fact]
    public async Task RecoverableFailureReloadsAuthoritativePublishPendingCheckpoint()
    {
        var plan = ExecutionCenterViewModelTests.CreatePlan(initializeRepository: true);
        var pending = ExecutionCenterViewModelTests.CreatePublishPendingExecution(plan).Checkpoint;
        var error = DevForgeError.Create(
            "DF-PUB-003",
            "Publication remains recoverable.",
            RedactedText.FromTrustedRedaction("Publication remains recoverable.").Value,
            "publication",
            stepId: null,
            isRetryable: true,
            suggestedActions: [],
            redactedContext: []).Value;
        var store = new FixedStore(pending);
        var sut = new ProjectPublicationWorkflow(new FailingCoordinator(error), store);

        var result = await sut.CompleteAsync(
            plan.RunId,
            PublicationMutationMode.Normal,
            CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.Same(pending, result.Value.Checkpoint);
        Assert.Same(error, result.Value.Error);
        Assert.Equal(1, store.FindCalls);
    }

    private sealed class FailingCoordinator(DevForgeError error) : IProjectPublicationCoordinator
    {
        public Task<ExecutionOperationResult<RunCheckpoint>> PublishAsync(
            PublicationRequest request,
            CancellationToken cancellationToken) => Task.FromResult(
                ExecutionOperationResult.Failure<RunCheckpoint>(error));
    }

    private sealed class FixedStore(RunCheckpoint checkpoint) : IRunCheckpointStore
    {
        public int FindCalls { get; private set; }

        public Task SaveAsync(RunCheckpoint value, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RunCheckpoint?> FindAsync(string runId, CancellationToken cancellationToken)
        {
            FindCalls++;
            return Task.FromResult<RunCheckpoint?>(checkpoint);
        }

        public Task<ImmutableArray<RunCheckpoint>> ListAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
