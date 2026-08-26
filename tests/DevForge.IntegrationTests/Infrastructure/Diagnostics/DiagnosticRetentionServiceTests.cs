using DevForge.Application.Contracts;
using DevForge.Infrastructure.Diagnostics;
using DevForge.Infrastructure.FileSystem;

namespace DevForge.IntegrationTests.Infrastructure.Diagnostics;

public sealed class DiagnosticRetentionServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "DevForge-M10-Retention-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DeletesExpiredThenOldestOwnedLogsAndPreservesActiveAndUnownedFiles()
    {
        var now = new DateTimeOffset(2026, 8, 26, 8, 0, 0, TimeSpan.Zero);
        Write("logs\\daily\\2026-08-26.jsonl", 128, now.AddDays(-90));
        Write("logs\\daily\\2026-06-01.jsonl", 128, now.AddDays(-60));
        Write("logs\\runs\\run-old.jsonl", 9 * 1024 * 1024, now.AddDays(-2));
        Write("logs\\runs\\run-new.jsonl", 9 * 1024 * 1024, now.AddDays(-1));
        Write("logs\\notes.txt", 128, now.AddDays(-100));
        Write("support-bundles\\keep.zip", 128, now.AddDays(-100));
        var policy = DiagnosticRetentionPolicy.Create(
            30,
            DiagnosticRetentionPolicy.MinimumTotalBytes).Value;
        var service = new DiagnosticRetentionService(
            new WindowsFileSystem(),
            WorkspaceRoot.Create(_root).Value);

        await service.ApplyAsync(policy, now, CancellationToken.None);

        Assert.True(File.Exists(Full("logs\\daily\\2026-08-26.jsonl")));
        Assert.False(File.Exists(Full("logs\\daily\\2026-06-01.jsonl")));
        Assert.False(File.Exists(Full("logs\\runs\\run-old.jsonl")));
        Assert.True(File.Exists(Full("logs\\runs\\run-new.jsonl")));
        Assert.True(File.Exists(Full("logs\\notes.txt")));
        Assert.True(File.Exists(Full("support-bundles\\keep.zip")));
    }

    [Fact]
    public async Task MissingLogsDirectoryIsANoOp()
    {
        Directory.CreateDirectory(_root);
        var service = new DiagnosticRetentionService(
            new WindowsFileSystem(),
            WorkspaceRoot.Create(_root).Value);

        await service.ApplyAsync(
            DiagnosticRetentionPolicy.Default,
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void Write(
        string relativePath,
        int length,
        DateTimeOffset lastWriteUtc)
    {
        var path = Full(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.SetLength(length);
        }

        File.SetLastWriteTimeUtc(path, lastWriteUtc.UtcDateTime);
    }

    private string Full(string relativePath) => Path.Combine(_root, relativePath);
}
