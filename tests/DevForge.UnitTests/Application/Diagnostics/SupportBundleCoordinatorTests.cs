using System.Collections.Immutable;
using DevForge.Application.Contracts;
using DevForge.Application.Diagnostics;

namespace DevForge.UnitTests.Application.Diagnostics;

public sealed class SupportBundleCoordinatorTests
{
    [Fact]
    public async Task MissingRunReturnsScrubbedFailureWithoutCallingWriter()
    {
        var writer = new RecordingWriter();
        var coordinator = new SupportBundleCoordinator(new MissingStore(), writer);
        var request = SupportBundleRequest.Create("run-missing", false).Value;

        var result = await coordinator.ExportAsync(request, CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Equal("DF-SUPPORT-001", result.Error!.Code);
        Assert.DoesNotContain("run-missing", result.Error.TechnicalDetail.Value, StringComparison.Ordinal);
        Assert.Equal(0, writer.CallCount);
    }

    private sealed class MissingStore : IRunCheckpointStore
    {
        public Task SaveAsync(RunCheckpoint checkpoint, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RunCheckpoint?> FindAsync(string runId, CancellationToken cancellationToken) =>
            Task.FromResult<RunCheckpoint?>(null);

        public Task<ImmutableArray<RunCheckpoint>> ListAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingWriter : ISupportBundleWriter
    {
        public int CallCount { get; private set; }

        public Task<ExecutionOperationResult<SupportBundleReceipt>> WriteAsync(
            RunCheckpoint checkpoint,
            bool includeEnvironmentSnapshot,
            CancellationToken cancellationToken)
        {
            CallCount++;
            throw new NotSupportedException();
        }
    }
}
