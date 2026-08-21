using System.Collections.Immutable;
using DevForge.Application.Contracts;
using DevForge.Application.Publication;

namespace DevForge.UnitTests.Application.Publication;

public sealed class ProjectPublicationWorkflowTests
{
    [Fact]
    public void OutcomeRequiresAuthoritativeCheckpoint()
    {
        var result = ProjectPublicationOutcome.Create(checkpoint: null, error: null);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue =>
            issue.Code == "publication.outcome.checkpoint.required");
    }

    [Fact]
    public async Task InvalidRunIdentityFailsBeforeCoordinatorOrStore()
    {
        var coordinator = new NeverCoordinator();
        var store = new EmptyStore();
        var sut = new ProjectPublicationWorkflow(coordinator, store);

        var result = await sut.CompleteAsync(
            "invalid",
            PublicationMutationMode.Normal,
            CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Equal("DF-PUB-REQUEST", result.Error!.Code);
        Assert.Equal(0, coordinator.Calls);
        Assert.Equal(0, store.FindCalls);
    }

    private sealed class NeverCoordinator : IProjectPublicationCoordinator
    {
        public int Calls { get; private set; }

        public Task<ExecutionOperationResult<RunCheckpoint>> PublishAsync(
            PublicationRequest request,
            CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("The invalid boundary must not be called.");
        }
    }

    private sealed class EmptyStore : IRunCheckpointStore
    {
        public int FindCalls { get; private set; }

        public Task SaveAsync(RunCheckpoint checkpoint, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RunCheckpoint?> FindAsync(string runId, CancellationToken cancellationToken)
        {
            FindCalls++;
            return Task.FromResult<RunCheckpoint?>(null);
        }

        public Task<ImmutableArray<RunCheckpoint>> ListAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
