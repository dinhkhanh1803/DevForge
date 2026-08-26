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
        WriteOwned("logs\\daily\\2026-08-26.jsonl", 128, now.AddDays(-90));
        WriteOwned("logs\\daily\\2026-06-01.jsonl", 128, now.AddDays(-60));
        WriteOwned("logs\\runs\\run-old.jsonl", 9 * 1024 * 1024, now.AddDays(-2));
        WriteOwned("logs\\runs\\run-new.jsonl", 9 * 1024 * 1024, now.AddDays(-1));
        Write("logs\\daily\\2026-05-01.jsonl", 128, now.AddDays(-100));
        Write("logs\\daily\\2026-05-02.jsonl", 128, now.AddDays(-100));
        File.WriteAllBytes(
            Full("logs\\daily\\2026-05-02.jsonl.owner.json"),
            DiagnosticLogOwnership.CreateMarkerBytes(
                WorkspaceRelativePath.Create("logs\\daily\\another-file.jsonl").Value));
        Write("logs\\notes.txt", 128, now.AddDays(-100));
        Write("support-bundles\\keep.zip", 128, now.AddDays(-100));
        var policy = DiagnosticRetentionPolicy.Create(
            30,
            DiagnosticRetentionPolicy.MinimumTotalBytes).Value;
        var service = new DiagnosticRetentionService(
            new WindowsFileSystem(),
            WorkspaceRoot.Create(_root).Value);

        var result = await service.ApplyAsync(policy, now, CancellationToken.None);

        Assert.True(File.Exists(Full("logs\\daily\\2026-08-26.jsonl")));
        Assert.False(File.Exists(Full("logs\\daily\\2026-06-01.jsonl")));
        Assert.False(File.Exists(Full("logs\\runs\\run-old.jsonl")));
        Assert.True(File.Exists(Full("logs\\runs\\run-new.jsonl")));
        Assert.True(File.Exists(Full("logs\\notes.txt")));
        Assert.True(File.Exists(Full("support-bundles\\keep.zip")));
        Assert.True(File.Exists(Full("logs\\daily\\2026-05-01.jsonl")));
        Assert.True(File.Exists(Full("logs\\daily\\2026-05-02.jsonl")));
        Assert.Equal(2, result.DeletedCount);
        Assert.Equal(2, result.UnownedCount);
    }

    [Fact]
    public async Task MissingLogsDirectoryIsANoOp()
    {
        Directory.CreateDirectory(_root);
        var service = new DiagnosticRetentionService(
            new WindowsFileSystem(),
            WorkspaceRoot.Create(_root).Value);

        var result = await service.ApplyAsync(
            DiagnosticRetentionPolicy.Default,
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
        Assert.Equal(0, result.DeletedCount);
    }

    [Fact]
    public async Task PreCancelledRetentionReturnsRedactedPartialResultWithoutMutation()
    {
        var now = new DateTimeOffset(2026, 8, 26, 8, 0, 0, TimeSpan.Zero);
        WriteOwned("logs\\daily\\2026-06-01.jsonl", 128, now.AddDays(-60));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var service = new DiagnosticRetentionService(
            new WindowsFileSystem(),
            WorkspaceRoot.Create(_root).Value);

        var result = await service.ApplyAsync(
            DiagnosticRetentionPolicy.Default,
            now,
            cancellation.Token);

        Assert.True(result.WasCancelled);
        Assert.Contains(DiagnosticRetentionReason.Cancelled, result.Reasons);
        Assert.True(File.Exists(Full("logs\\daily\\2026-06-01.jsonl")));
    }

    [Fact]
    public async Task DeleteFailureStopsBeforeLaterCandidatesAndReturnsPartialResult()
    {
        var now = new DateTimeOffset(2026, 8, 26, 8, 0, 0, TimeSpan.Zero);
        const string blocked = "logs\\daily\\2026-05-01.jsonl";
        const string later = "logs\\daily\\2026-06-01.jsonl";
        WriteOwned(blocked, 128, now.AddDays(-90));
        WriteOwned(later, 128, now.AddDays(-60));
        File.SetAttributes(Full(blocked), FileAttributes.ReadOnly);
        var service = new DiagnosticRetentionService(
            new WindowsFileSystem(),
            WorkspaceRoot.Create(_root).Value);

        var result = await service.ApplyAsync(
            DiagnosticRetentionPolicy.Default,
            now,
            CancellationToken.None);

        Assert.Equal(0, result.DeletedCount);
        Assert.Equal(2, result.DeferredCount);
        Assert.Contains(DiagnosticRetentionReason.DeleteFailed, result.Reasons);
        Assert.True(File.Exists(Full(later)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

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

    private void WriteOwned(
        string relativePath,
        int length,
        DateTimeOffset lastWriteUtc)
    {
        Write(relativePath, length, lastWriteUtc);
        var marker = DiagnosticLogOwnership.CreateMarkerBytes(
            WorkspaceRelativePath.Create(relativePath).Value);
        File.WriteAllBytes(Full(relativePath + ".owner.json"), marker);
    }

    private string Full(string relativePath) => Path.Combine(_root, relativePath);
}
