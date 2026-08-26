using DevForge.Application.Contracts;
using DevForge.Application.Contracts.Persistence;
using DevForge.Infrastructure.Persistence;

namespace DevForge.IntegrationTests.Persistence;

public sealed class LocalDataRootProvisionerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "DevForge-LocalDataTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DelegatesTheValidatedLocalDataRootToTheGuardedFileSystemPort()
    {
        var fileSystem = new RecordingFileSystem();
        var location = DatabaseLocation.Create(_root, "devforge.db").Value;

        await new LocalDataRootProvisioner(fileSystem).EnsureExistsAsync(
            location,
            CancellationToken.None);

        Assert.Equal(WorkspaceRoot.Create(_root).Value, Assert.Single(fileSystem.EnsuredRoots));
        Assert.False(fileSystem.OpenWorkspaceCalled);
    }

    [Fact]
    public async Task PropagatesCancellationBeforeProvisioning()
    {
        var fileSystem = new RecordingFileSystem();
        var location = DatabaseLocation.Create(_root, "devforge.db").Value;
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new LocalDataRootProvisioner(fileSystem).EnsureExistsAsync(
                location,
                cancellation.Token));

        Assert.Empty(fileSystem.EnsuredRoots);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class RecordingFileSystem : IFileSystem
    {
        public List<WorkspaceRoot> EnsuredRoots { get; } = [];

        public bool OpenWorkspaceCalled { get; private set; }

        public Task EnsureWorkspaceExistsAsync(
            WorkspaceRoot allowedRoot,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsuredRoots.Add(allowedRoot);
            return Task.CompletedTask;
        }

        public Task<IWorkspaceFileSystem> OpenWorkspaceAsync(
            WorkspaceRoot allowedRoot,
            CancellationToken cancellationToken)
        {
            OpenWorkspaceCalled = true;
            throw new InvalidOperationException("The provisioner must not open the workspace.");
        }
    }
}
