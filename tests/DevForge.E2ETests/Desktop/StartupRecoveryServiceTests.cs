using System.Collections.Immutable;
using DevForge.Application.Contracts;
using DevForge.Desktop.Bootstrap;

namespace DevForge.E2ETests.Desktop;

public sealed class StartupRecoveryServiceTests
{
    [Fact]
    public async Task EmptyCheckpointStoreCompletesWithoutWrites()
    {
        var store = new EmptyCheckpointStore();
        var sut = new StartupRecoveryService(store, TimeProvider.System);

        var result = await sut.RecoverAsync(CancellationToken.None);

        Assert.True(result);
        Assert.Equal(1, store.ListCalls);
        Assert.Equal(0, store.SaveCalls);
    }

    private sealed class EmptyCheckpointStore : IRunCheckpointStore
    {
        public int ListCalls { get; private set; }

        public int SaveCalls { get; private set; }

        public Task SaveAsync(RunCheckpoint checkpoint, CancellationToken cancellationToken)
        {
            SaveCalls++;
            return Task.CompletedTask;
        }

        public Task<RunCheckpoint?> FindAsync(string runId, CancellationToken cancellationToken) =>
            Task.FromResult<RunCheckpoint?>(null);

        public Task<ImmutableArray<RunCheckpoint>> ListAsync(CancellationToken cancellationToken)
        {
            ListCalls++;
            return Task.FromResult(ImmutableArray<RunCheckpoint>.Empty);
        }
    }
}
