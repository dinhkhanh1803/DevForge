using System.Collections.Immutable;
using System.Text.Json;
using DevForge.Application.Contracts;
using DevForge.Domain.Privacy;
using DevForge.Infrastructure.Diagnostics;
using DevForge.Infrastructure.FileSystem;

namespace DevForge.IntegrationTests.Infrastructure.Diagnostics;

public sealed class JsonLinesDiagnosticSinkTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "DevForge-M10-Diagnostics-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ConcurrentWritesProduceParseableDailyAndRunSpecificJsonLines()
    {
        Directory.CreateDirectory(_root);
        var sink = new JsonLinesDiagnosticSink(
            new WindowsFileSystem(),
            WorkspaceRoot.Create(_root).Value);

        await Task.WhenAll(Enumerable.Range(1, 40).Select(attempt => sink.WriteAsync(
            CreateEvent(attempt),
            CancellationToken.None)));

        var daily = await ReadLinesAsync(Path.Combine(_root, "logs", "daily", "2026-08-26.jsonl"));
        var run = await ReadLinesAsync(Path.Combine(_root, "logs", "runs", "run-001.jsonl"));
        Assert.Equal(40, daily.Length);
        Assert.Equal(40, run.Length);
        Assert.All(daily.Concat(run), line =>
        {
            using var document = JsonDocument.Parse(line);
            Assert.Equal("execution.step.completed", document.RootElement.GetProperty("eventId").GetString());
        });
    }

    [Fact]
    public async Task SeparateSinkInstancesWaitForTheSharedLeaseWithoutDroppingEvents()
    {
        Directory.CreateDirectory(_root);
        var fileSystem = new WindowsFileSystem();
        var root = WorkspaceRoot.Create(_root).Value;
        using var first = new JsonLinesDiagnosticSink(fileSystem, root);
        using var second = new JsonLinesDiagnosticSink(fileSystem, root);

        await Task.WhenAll(Enumerable.Range(1, 40).Select(attempt =>
            (attempt % 2 == 0 ? first : second).WriteAsync(
                CreateEvent(attempt),
                CancellationToken.None)));

        Assert.Equal(
            40,
            (await ReadLinesAsync(Path.Combine(_root, "logs", "daily", "2026-08-26.jsonl"))).Length);
    }

    [Fact]
    public async Task SinkAndRetentionShareTheCrossProcessLeaseWithoutDroppingActiveEvents()
    {
        Directory.CreateDirectory(_root);
        var fileSystem = new WindowsFileSystem();
        var root = WorkspaceRoot.Create(_root).Value;
        using var sink = new JsonLinesDiagnosticSink(fileSystem, root);
        var retention = new DiagnosticRetentionService(fileSystem, root);
        await sink.WriteAsync(CreateEvent(1), CancellationToken.None);

        await Task.WhenAll(
            Task.WhenAll(Enumerable.Range(2, 39).Select(attempt => sink.WriteAsync(
                CreateEvent(attempt),
                CancellationToken.None))),
            retention.ApplyAsync(
                DiagnosticRetentionPolicy.Default,
                new DateTimeOffset(2026, 8, 26, 8, 0, 0, TimeSpan.Zero),
                CancellationToken.None));

        Assert.Equal(
            40,
            (await ReadLinesAsync(Path.Combine(_root, "logs", "daily", "2026-08-26.jsonl"))).Length);
    }

    [Fact]
    public async Task PreCancelledWriteCreatesNoDiagnosticArtifacts()
    {
        Directory.CreateDirectory(_root);
        var sink = new JsonLinesDiagnosticSink(
            new WindowsFileSystem(),
            WorkspaceRoot.Create(_root).Value);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sink.WriteAsync(
            CreateEvent(1),
            cancellation.Token));

        Assert.False(Directory.Exists(Path.Combine(_root, "logs")));
    }

    [Fact]
    public async Task AtomicPublicationCancellationLeavesNoPartialJsonLine()
    {
        Directory.CreateDirectory(_root);
        var root = WorkspaceRoot.Create(_root).Value;
        var sink = new JsonLinesDiagnosticSink(
            new FaultingFileSystem(new WindowsFileSystem()),
            root);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sink.WriteAsync(
            CreateEvent(1),
            CancellationToken.None));

        Assert.Empty(Directory.EnumerateFiles(_root, "*.jsonl", SearchOption.AllDirectories));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static DiagnosticEvent CreateEvent(int attempt) =>
        DiagnosticEvent.Create(
            new DateTimeOffset(2026, 8, 26, 7, 30, 0, TimeSpan.Zero),
            DiagnosticLevel.Information,
            "execution.step.completed",
            "run-001",
            "restore",
            attempt,
            "execution-orchestrator",
            RedactedText.FromTrustedRedaction("Restore completed.").Value,
            125,
            null).Value;

    private static async Task<string[]> ReadLinesAsync(string path) =>
        (await File.ReadAllLinesAsync(path)).Where(line => line.Length != 0).ToArray();

    private sealed class FaultingFileSystem(IFileSystem inner) : IFileSystem
    {
        public Task EnsureWorkspaceExistsAsync(
            WorkspaceRoot allowedRoot,
            CancellationToken cancellationToken) => inner.EnsureWorkspaceExistsAsync(
                allowedRoot,
                cancellationToken);

        public async Task<IWorkspaceFileSystem> OpenWorkspaceAsync(
            WorkspaceRoot allowedRoot,
            CancellationToken cancellationToken)
        {
            var workspace = await inner.OpenWorkspaceAsync(allowedRoot, cancellationToken);
            return new FaultingWorkspace(
                Assert.IsAssignableFrom<IAtomicFileWorkspaceFileSystem>(workspace),
                Assert.IsAssignableFrom<IExclusiveLeaseWorkspaceFileSystem>(workspace));
        }
    }

    private sealed class FaultingWorkspace(
        IAtomicFileWorkspaceFileSystem atomic,
        IExclusiveLeaseWorkspaceFileSystem leases) :
        IAtomicFileWorkspaceFileSystem,
        IExclusiveLeaseWorkspaceFileSystem
    {
        public WorkspaceRoot Root => atomic.Root;

        public Task<bool> FileExistsAsync(WorkspaceRelativePath path, CancellationToken token) =>
            atomic.FileExistsAsync(path, token);

        public Task<bool> DirectoryExistsAsync(WorkspaceRelativePath path, CancellationToken token) =>
            atomic.DirectoryExistsAsync(path, token);

        public Task CreateDirectoryAsync(WorkspaceRelativePath path, CancellationToken token) =>
            atomic.CreateDirectoryAsync(path, token);

        public Task<Stream> OpenReadAsync(WorkspaceRelativePath path, CancellationToken token) =>
            atomic.OpenReadAsync(path, token);

        public Task<Stream> OpenWriteAsync(
            WorkspaceRelativePath path,
            bool overwrite,
            CancellationToken token) => atomic.OpenWriteAsync(path, overwrite, token);

        public Task DeleteFileAsync(WorkspaceRelativePath path, CancellationToken token) =>
            atomic.DeleteFileAsync(path, token);

        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateAllFilesAsync(CancellationToken token) =>
            atomic.EnumerateAllFilesAsync(token);

        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateRootDirectoriesAsync(CancellationToken token) =>
            atomic.EnumerateRootDirectoriesAsync(token);

        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateFilesAsync(
            WorkspaceRelativePath directory,
            bool recursive,
            CancellationToken token) => atomic.EnumerateFilesAsync(directory, recursive, token);

        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateDirectoriesAsync(
            WorkspaceRelativePath directory,
            CancellationToken token) => atomic.EnumerateDirectoriesAsync(directory, token);

        public Task DeleteDirectoryAsync(
            WorkspaceRelativePath path,
            DirectoryCleanupIntent intent,
            CancellationToken token) => atomic.DeleteDirectoryAsync(path, intent, token);

        public Task MoveDirectoryAsync(
            WorkspaceRelativePath source,
            WorkspaceRelativePath destination,
            WorkspaceMoveIntent intent,
            CancellationToken token) => atomic.MoveDirectoryAsync(source, destination, intent, token);

        public Task<IWorkspaceExclusiveLease?> TryAcquireExclusiveLeaseAsync(
            WorkspaceRelativePath path,
            CancellationToken token) => leases.TryAcquireExclusiveLeaseAsync(path, token);

        public Task WriteFileAtomicallyAsync(
            WorkspaceRelativePath path,
            ReadOnlyMemory<byte> content,
            bool overwrite,
            CancellationToken token) => throw new OperationCanceledException();
    }
}
