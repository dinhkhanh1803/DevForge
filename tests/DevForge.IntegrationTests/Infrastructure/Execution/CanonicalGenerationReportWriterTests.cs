using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using System.Text.Json;
using DevForge.Application.Contracts;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Execution;
using DevForge.Domain.Reports;
using DevForge.Domain.Runs;
using DevForge.Infrastructure.Execution;
using DevForge.Infrastructure.FileSystem;

namespace DevForge.IntegrationTests.Infrastructure.Execution;

public sealed class CanonicalGenerationReportWriterTests
{
    [Fact]
    public void CrossVolumeTreeComparerUsesExactOrdinalPathIdentity()
    {
        var comparerType = typeof(AtomicProjectFinalizer).GetNestedType(
            "WorkspacePathComparer",
            BindingFlags.NonPublic);
        var instance = comparerType?.GetProperty(
            "Instance",
            BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        var comparer = Assert.IsAssignableFrom<IEqualityComparer<WorkspaceRelativePath>>(instance);

        Assert.False(comparer.Equals(Path("src\\App.cs"), Path("src\\app.cs")));
    }

    [Fact]
    public async Task WritesBoundedCanonicalJsonAndMarkdownWithoutWorkspacePaths()
    {
        var rootPath = TestRoot();
        Directory.CreateDirectory(rootPath);
        try
        {
            var fileSystem = new WindowsFileSystem();
            var workspace = await fileSystem.OpenWorkspaceAsync(
                WorkspaceRoot.Create(rootPath).Value,
                CancellationToken.None);
            var checkpoint = Checkpoint(workspace.Root);
            var report = GenerationReport.Create(
                checkpoint.Run.Id,
                new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero),
                [new ValidationCheck("build", ValidationCheckStatus.Passed, "Build passed.", null)],
                [new ReportToolStatus("dotnet", true, true, true, "10.0.0")],
                [new ReportWarning(
                    "planner.optional",
                    DevForge.Domain.Privacy.RedactedText.FromTrustedRedaction(
                        "An optional capability was not selected.").Value)],
                [],
                ["src\\App.csproj"]).Value;
            var writer = new CanonicalGenerationReportWriter();

            var result = await writer.WriteAsync(
                checkpoint,
                report,
                workspace,
                CancellationToken.None);

            Assert.True(result.IsSuccessful);
            var json = await ReadAsync(workspace, result.Value.JsonReport);
            var markdown = await ReadAsync(workspace, result.Value.MarkdownReport);
            Assert.Contains(checkpoint.PlanHash, json, StringComparison.Ordinal);
            Assert.Contains("desktop.csharp-wpf-tool", json, StringComparison.Ordinal);
            Assert.Contains("\"toolStatuses\"", json, StringComparison.Ordinal);
            Assert.Contains("\"warnings\"", json, StringComparison.Ordinal);
            Assert.Contains("dotnet", markdown, StringComparison.Ordinal);
            Assert.Contains("planner.optional", markdown, StringComparison.Ordinal);
            Assert.Contains("Build passed.", markdown, StringComparison.Ordinal);
            Assert.DoesNotContain(rootPath, json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(rootPath, markdown, StringComparison.OrdinalIgnoreCase);
            Assert.True(Encoding.UTF8.GetByteCount(json) <= CanonicalGenerationReportWriter.MaximumReportBytes);
            Assert.True(Encoding.UTF8.GetByteCount(markdown) <= CanonicalGenerationReportWriter.MaximumReportBytes);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public async Task SameVolumeFinalizerMovesPayloadAtomicallyWithoutOverwritingTarget()
    {
        var rootPath = TestRoot();
        Directory.CreateDirectory(rootPath);
        try
        {
            var fileSystem = new WindowsFileSystem();
            var workspace = await fileSystem.OpenWorkspaceAsync(
                WorkspaceRoot.Create(rootPath).Value,
                CancellationToken.None);
            var checkpoint = Checkpoint(workspace.Root, FinalizationState.IntentPersisted);
            await workspace.CreateDirectoryAsync(
                checkpoint.Staging.PayloadDirectory,
                CancellationToken.None);
            await WriteMarkerAsync(workspace, checkpoint, corruptPlanHash: false);
            await using (var output = await workspace.OpenWriteAsync(
                Path(".devforge-staging\\run-report\\payload\\app.txt"),
                overwrite: false,
                CancellationToken.None))
            {
                await output.WriteAsync("hello"u8.ToArray(), CancellationToken.None);
            }

            var payload = await fileSystem.OpenWorkspaceAsync(
                WorkspaceRoot.Create(System.IO.Path.Combine(
                    rootPath,
                    checkpoint.Staging.PayloadDirectory.Value)).Value,
                CancellationToken.None);
            var staging = StagingWorkspace.Create(checkpoint.Staging, payload).Value;
            var finalizer = new AtomicProjectFinalizer();

            var result = await finalizer.FinalizeAsync(
                checkpoint,
                staging,
                workspace,
                CancellationToken.None);

            Assert.True(result.IsSuccessful);
            Assert.True(await workspace.FileExistsAsync(
                Path("project\\app.txt"),
                CancellationToken.None));
            Assert.False(await workspace.DirectoryExistsAsync(
                checkpoint.Staging.PayloadDirectory,
                CancellationToken.None));
            Assert.StartsWith("sha256:", result.Value.TreeDigest, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public async Task ReportWriterReturnsScrubbedFailureForGuardedWorkspaceOperationError()
    {
        var rootPath = TestRoot();
        Directory.CreateDirectory(rootPath);
        try
        {
            var workspace = await new WindowsFileSystem().OpenWorkspaceAsync(
                WorkspaceRoot.Create(rootPath).Value,
                CancellationToken.None);
            await using (var blocker = await workspace.OpenWriteAsync(
                Path("reports"),
                overwrite: false,
                CancellationToken.None))
            {
                await blocker.WriteAsync("blocked"u8.ToArray(), CancellationToken.None);
            }

            var checkpoint = Checkpoint(workspace.Root);
            var report = GenerationReport.Create(
                checkpoint.Run.Id,
                DateTimeOffset.UnixEpoch,
                [],
                [],
                []).Value;

            var result = await new CanonicalGenerationReportWriter().WriteAsync(
                checkpoint,
                report,
                workspace,
                CancellationToken.None);

            Assert.False(result.IsSuccessful);
            Assert.Equal("DF-FINAL-001", result.Error?.Code);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public async Task FinalizerRejectsTamperedOwnershipMarkerAndRetainsPayload()
    {
        var rootPath = TestRoot();
        Directory.CreateDirectory(rootPath);
        try
        {
            var fileSystem = new WindowsFileSystem();
            var workspace = await fileSystem.OpenWorkspaceAsync(
                WorkspaceRoot.Create(rootPath).Value,
                CancellationToken.None);
            var checkpoint = Checkpoint(workspace.Root, FinalizationState.IntentPersisted);
            await workspace.CreateDirectoryAsync(
                checkpoint.Staging.PayloadDirectory,
                CancellationToken.None);
            await WriteMarkerAsync(workspace, checkpoint, corruptPlanHash: true);
            var payload = await fileSystem.OpenWorkspaceAsync(
                WorkspaceRoot.Create(System.IO.Path.Combine(
                    rootPath,
                    checkpoint.Staging.PayloadDirectory.Value)).Value,
                CancellationToken.None);
            var staging = StagingWorkspace.Create(checkpoint.Staging, payload).Value;

            var result = await new AtomicProjectFinalizer().FinalizeAsync(
                checkpoint,
                staging,
                workspace,
                CancellationToken.None);

            Assert.False(result.IsSuccessful);
            Assert.Equal("DF-FINAL-001", result.Error?.Code);
            Assert.True(await workspace.DirectoryExistsAsync(
                checkpoint.Staging.PayloadDirectory,
                CancellationToken.None));
            Assert.False(await workspace.DirectoryExistsAsync(
                checkpoint.Target.TargetDirectory,
                CancellationToken.None));
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public async Task FinalizerReturnsScrubbedFailureWhenOwnershipMarkerIsMissing()
    {
        var rootPath = TestRoot();
        Directory.CreateDirectory(rootPath);
        try
        {
            var fileSystem = new WindowsFileSystem();
            var workspace = await fileSystem.OpenWorkspaceAsync(
                WorkspaceRoot.Create(rootPath).Value,
                CancellationToken.None);
            var checkpoint = Checkpoint(workspace.Root, FinalizationState.IntentPersisted);
            await workspace.CreateDirectoryAsync(
                checkpoint.Staging.PayloadDirectory,
                CancellationToken.None);
            var payload = await fileSystem.OpenWorkspaceAsync(
                WorkspaceRoot.Create(System.IO.Path.Combine(
                    rootPath,
                    checkpoint.Staging.PayloadDirectory.Value)).Value,
                CancellationToken.None);

            var result = await new AtomicProjectFinalizer().FinalizeAsync(
                checkpoint,
                StagingWorkspace.Create(checkpoint.Staging, payload).Value,
                workspace,
                CancellationToken.None);

            Assert.False(result.IsSuccessful);
            Assert.Equal("DF-FINAL-001", result.Error?.Code);
            Assert.True(await workspace.DirectoryExistsAsync(
                checkpoint.Staging.PayloadDirectory,
                CancellationToken.None));
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public async Task SameVolumeFinalizerRefusesAnExistingTargetWithoutChangingIt()
    {
        var rootPath = TestRoot();
        Directory.CreateDirectory(rootPath);
        try
        {
            var fileSystem = new WindowsFileSystem();
            var workspace = await fileSystem.OpenWorkspaceAsync(
                WorkspaceRoot.Create(rootPath).Value,
                CancellationToken.None);
            var checkpoint = Checkpoint(workspace.Root, FinalizationState.IntentPersisted);
            await workspace.CreateDirectoryAsync(checkpoint.Staging.PayloadDirectory, CancellationToken.None);
            await workspace.CreateDirectoryAsync(checkpoint.Target.TargetDirectory, CancellationToken.None);
            var payload = await fileSystem.OpenWorkspaceAsync(
                WorkspaceRoot.Create(System.IO.Path.Combine(
                    rootPath,
                    checkpoint.Staging.PayloadDirectory.Value)).Value,
                CancellationToken.None);
            var staging = StagingWorkspace.Create(checkpoint.Staging, payload).Value;

            var result = await new AtomicProjectFinalizer().FinalizeAsync(
                checkpoint,
                staging,
                workspace,
                CancellationToken.None);

            Assert.False(result.IsSuccessful);
            Assert.Equal("DF-FINAL-001", result.Error?.Code);
            Assert.True(await workspace.DirectoryExistsAsync(
                checkpoint.Staging.PayloadDirectory,
                CancellationToken.None));
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public async Task DetachedPayloadUsesVerifiedCopyThenAtomicTargetRename()
    {
        var rootPath = TestRoot();
        var sourcePath = System.IO.Path.Combine(rootPath, "source");
        var targetPath = System.IO.Path.Combine(rootPath, "target-parent");
        Directory.CreateDirectory(sourcePath);
        Directory.CreateDirectory(targetPath);
        try
        {
            var fileSystem = new WindowsFileSystem();
            var source = await fileSystem.OpenWorkspaceAsync(
                WorkspaceRoot.Create(sourcePath).Value,
                CancellationToken.None);
            var target = await fileSystem.OpenWorkspaceAsync(
                WorkspaceRoot.Create(targetPath).Value,
                CancellationToken.None);
            await source.CreateDirectoryAsync(Path("src"), CancellationToken.None);
            await using (var output = await source.OpenWriteAsync(
                Path("src\\app.txt"),
                overwrite: false,
                CancellationToken.None))
            {
                await output.WriteAsync("verified copy"u8.ToArray(), CancellationToken.None);
            }

            var checkpoint = Checkpoint(target.Root, FinalizationState.IntentPersisted);
            await target.CreateDirectoryAsync(
                checkpoint.Staging.ContainerDirectory,
                CancellationToken.None);
            await WriteMarkerAsync(target, checkpoint, corruptPlanHash: false);
            var staging = StagingWorkspace.Create(checkpoint.Staging, source).Value;

            var result = await new AtomicProjectFinalizer().FinalizeAsync(
                checkpoint,
                staging,
                target,
                CancellationToken.None);

            Assert.True(result.IsSuccessful);
            Assert.True(await target.FileExistsAsync(
                Path("project\\src\\app.txt"),
                CancellationToken.None));
            Assert.False(await target.DirectoryExistsAsync(
                Path(".devforge-finalize-run-report"),
                CancellationToken.None));
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    private static RunCheckpoint Checkpoint(
        WorkspaceRoot artifactRoot,
        FinalizationState finalizationState = FinalizationState.NotStarted)
    {
        var hash = $"sha256:{new string('1', 64)}";
        var run = ProjectRun.Create("run-report", "recipe-1").Value
            .TransitionTo(RunStatus.Planning).Value
            .TransitionTo(RunStatus.Executing).Value;
        var plan = ExecutionPlan.Create(hash, [], []).Value;
        var blueprint = BlueprintReference.Create("desktop.csharp-wpf-tool", "1.0.0").Value;
        var fingerprint = BlueprintFingerprint.Create(
            "built-in",
            Path("desktop.csharp-wpf-tool\\1.0.0"),
            BlueprintTrust.BuiltIn,
            $"sha256:{new string('2', 64)}").Value;
        return RunCheckpoint.Create(
            run,
            plan,
            blueprint,
            fingerprint,
            StagingDescriptor.Create(
                Path(".devforge-staging\\run-report"),
                Path(".devforge-staging\\run-report\\payload"),
                Path(".devforge-staging\\run-report\\ownership.json"),
                "run-report").Value,
            TargetDescriptor.Create(artifactRoot, Path("project"), null).Value,
            RunArtifactDescriptor.Create(artifactRoot).Value,
            [],
            finalizationState,
            ReportPersistenceState.NotStarted).Value;
    }

    private static async Task<string> ReadAsync(
        IWorkspaceFileSystem workspace,
        WorkspaceRelativePath path)
    {
        await using var stream = await workspace.OpenReadAsync(path, CancellationToken.None);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync(CancellationToken.None);
    }

    [Fact]
    public async Task CorruptedCrossVolumeCopyIsRejectedAndTemporaryDirectoryIsRemoved()
    {
        var rootPath = TestRoot();
        var sourcePath = System.IO.Path.Combine(rootPath, "source");
        var targetPath = System.IO.Path.Combine(rootPath, "target-parent");
        Directory.CreateDirectory(sourcePath);
        Directory.CreateDirectory(targetPath);
        try
        {
            var fileSystem = new WindowsFileSystem();
            var source = await fileSystem.OpenWorkspaceAsync(
                WorkspaceRoot.Create(sourcePath).Value,
                CancellationToken.None);
            var realTarget = await fileSystem.OpenWorkspaceAsync(
                WorkspaceRoot.Create(targetPath).Value,
                CancellationToken.None);
            await using (var output = await source.OpenWriteAsync(
                Path("app.txt"),
                overwrite: false,
                CancellationToken.None))
            {
                await output.WriteAsync("verified bytes"u8.ToArray(), CancellationToken.None);
            }

            var checkpoint = Checkpoint(realTarget.Root, FinalizationState.IntentPersisted);
            await realTarget.CreateDirectoryAsync(
                checkpoint.Staging.ContainerDirectory,
                CancellationToken.None);
            await WriteMarkerAsync(realTarget, checkpoint, corruptPlanHash: false);
            var corruptingTarget = new CorruptingAtomicWorkspace(
                Assert.IsAssignableFrom<IAtomicWorkspaceFileSystem>(realTarget));

            var result = await new AtomicProjectFinalizer().FinalizeAsync(
                checkpoint,
                StagingWorkspace.Create(checkpoint.Staging, source).Value,
                corruptingTarget,
                CancellationToken.None);

            Assert.False(result.IsSuccessful);
            Assert.Equal("DF-FINAL-001", result.Error?.Code);
            Assert.False(await realTarget.DirectoryExistsAsync(
                checkpoint.Target.TargetDirectory,
                CancellationToken.None));
            Assert.False(await realTarget.DirectoryExistsAsync(
                Path(".devforge-finalize-run-report"),
                CancellationToken.None));
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public async Task SameVolumeFinalizerRejectsPayloadWorkspaceThatDoesNotOwnDescriptorPath()
    {
        var rootPath = TestRoot();
        var sourcePath = System.IO.Path.Combine(rootPath, "detached");
        var targetPath = System.IO.Path.Combine(rootPath, "target-parent");
        Directory.CreateDirectory(sourcePath);
        Directory.CreateDirectory(targetPath);
        try
        {
            var fileSystem = new WindowsFileSystem();
            var source = await fileSystem.OpenWorkspaceAsync(
                WorkspaceRoot.Create(sourcePath).Value,
                CancellationToken.None);
            var target = await fileSystem.OpenWorkspaceAsync(
                WorkspaceRoot.Create(targetPath).Value,
                CancellationToken.None);
            var checkpoint = Checkpoint(target.Root, FinalizationState.IntentPersisted);
            await target.CreateDirectoryAsync(
                checkpoint.Staging.PayloadDirectory,
                CancellationToken.None);
            await WriteMarkerAsync(target, checkpoint, corruptPlanHash: false);

            var result = await new AtomicProjectFinalizer().FinalizeAsync(
                checkpoint,
                StagingWorkspace.Create(checkpoint.Staging, source).Value,
                target,
                CancellationToken.None);

            Assert.False(result.IsSuccessful);
            Assert.True(await target.DirectoryExistsAsync(
                checkpoint.Staging.PayloadDirectory,
                CancellationToken.None));
            Assert.False(await target.DirectoryExistsAsync(
                checkpoint.Target.TargetDirectory,
                CancellationToken.None));
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public async Task FinalizerRejectsPayloadBeyondTheExplicitFileCountBound()
    {
        var rootPath = TestRoot();
        Directory.CreateDirectory(rootPath);
        try
        {
            var fileSystem = new WindowsFileSystem();
            var target = await fileSystem.OpenWorkspaceAsync(
                WorkspaceRoot.Create(rootPath).Value,
                CancellationToken.None);
            var checkpoint = Checkpoint(target.Root, FinalizationState.IntentPersisted);
            await target.CreateDirectoryAsync(
                checkpoint.Staging.ContainerDirectory,
                CancellationToken.None);
            await WriteMarkerAsync(target, checkpoint, corruptPlanHash: false);
            var source = new ManyFilesWorkspace(AtomicProjectFinalizer.MaximumFileCount + 1);
            var staging = StagingWorkspace.Create(checkpoint.Staging, source).Value;

            var result = await new AtomicProjectFinalizer().FinalizeAsync(
                checkpoint,
                staging,
                target,
                CancellationToken.None);

            Assert.False(result.IsSuccessful);
            Assert.False(await target.DirectoryExistsAsync(
                checkpoint.Target.TargetDirectory,
                CancellationToken.None));
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    private static async Task WriteMarkerAsync(
        IWorkspaceFileSystem workspace,
        RunCheckpoint checkpoint,
        bool corruptPlanHash)
    {
        var planHash = corruptPlanHash
            ? $"sha256:{new string('f', 64)}"
            : checkpoint.PlanHash;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            markerId = checkpoint.Staging.MarkerId,
            runId = checkpoint.Run.Id,
            planHash,
            blueprintId = checkpoint.Blueprint.Id,
            blueprintVersion = checkpoint.Blueprint.Version,
            blueprintChecksum = checkpoint.BlueprintFingerprint.AggregateChecksum,
            lifecycleIntent = "staging",
        });
        await using var output = await workspace.OpenWriteAsync(
            checkpoint.Staging.MarkerFile,
            overwrite: false,
            CancellationToken.None);
        await output.WriteAsync(bytes, CancellationToken.None);
        await output.FlushAsync(CancellationToken.None);
    }

    private static WorkspaceRelativePath Path(string value) =>
        WorkspaceRelativePath.Create(value).Value;

    private static string TestRoot() => System.IO.Path.GetFullPath(System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        "DevForge.ReportWriterTests",
        Guid.NewGuid().ToString("N")));

    private sealed class ManyFilesWorkspace(int count) : IWorkspaceFileSystem
    {
        private readonly System.Collections.Immutable.ImmutableArray<WorkspaceRelativePath> _files =
            Enumerable.Range(0, count)
                .Select(index => Path($"files\\f{index:D5}.txt"))
                .ToImmutableArray();

        public WorkspaceRoot Root { get; } = WorkspaceRoot.Create("C:\\bounded-source").Value;

        public Task<System.Collections.Immutable.ImmutableArray<WorkspaceRelativePath>> EnumerateAllFilesAsync(
            CancellationToken cancellationToken) => Task.FromResult(_files);

        public Task<System.Collections.Immutable.ImmutableArray<WorkspaceRelativePath>> EnumerateRootDirectoriesAsync(
            CancellationToken cancellationToken) => Task.FromResult(
                System.Collections.Immutable.ImmutableArray.Create(Path("files")));

        public Task<System.Collections.Immutable.ImmutableArray<WorkspaceRelativePath>> EnumerateDirectoriesAsync(
            WorkspaceRelativePath directory,
            CancellationToken cancellationToken) => Task.FromResult(
                System.Collections.Immutable.ImmutableArray<WorkspaceRelativePath>.Empty);

        public Task<bool> FileExistsAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DirectoryExistsAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CreateDirectoryAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Stream> OpenWriteAsync(WorkspaceRelativePath path, bool overwrite, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteFileAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<System.Collections.Immutable.ImmutableArray<WorkspaceRelativePath>> EnumerateFilesAsync(WorkspaceRelativePath directory, bool recursive, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteDirectoryAsync(WorkspaceRelativePath path, DirectoryCleanupIntent intent, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MoveDirectoryAsync(WorkspaceRelativePath source, WorkspaceRelativePath destination, WorkspaceMoveIntent intent, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class CorruptingAtomicWorkspace(IAtomicWorkspaceFileSystem inner) :
        IAtomicWorkspaceFileSystem
    {
        public WorkspaceRoot Root => inner.Root;

        public Task<bool> TryCreateDirectoryAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) =>
            inner.TryCreateDirectoryAsync(path, cancellationToken);
        public Task<bool> FileExistsAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => inner.FileExistsAsync(path, cancellationToken);
        public Task<bool> DirectoryExistsAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => inner.DirectoryExistsAsync(path, cancellationToken);
        public Task CreateDirectoryAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => inner.CreateDirectoryAsync(path, cancellationToken);
        public Task<Stream> OpenReadAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => inner.OpenReadAsync(path, cancellationToken);

        public async Task<Stream> OpenWriteAsync(
            WorkspaceRelativePath path,
            bool overwrite,
            CancellationToken cancellationToken)
        {
            var stream = await inner.OpenWriteAsync(path, overwrite, cancellationToken);
            return path.Value.StartsWith(".devforge-finalize-", StringComparison.Ordinal)
                ? new CorruptingWriteStream(stream)
                : stream;
        }

        public Task DeleteFileAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => inner.DeleteFileAsync(path, cancellationToken);
        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateAllFilesAsync(CancellationToken cancellationToken) => inner.EnumerateAllFilesAsync(cancellationToken);
        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateRootDirectoriesAsync(CancellationToken cancellationToken) => inner.EnumerateRootDirectoriesAsync(cancellationToken);
        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateFilesAsync(WorkspaceRelativePath directory, bool recursive, CancellationToken cancellationToken) => inner.EnumerateFilesAsync(directory, recursive, cancellationToken);
        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateDirectoriesAsync(WorkspaceRelativePath directory, CancellationToken cancellationToken) => inner.EnumerateDirectoriesAsync(directory, cancellationToken);
        public Task DeleteDirectoryAsync(WorkspaceRelativePath path, DirectoryCleanupIntent intent, CancellationToken cancellationToken) => inner.DeleteDirectoryAsync(path, intent, cancellationToken);
        public Task MoveDirectoryAsync(WorkspaceRelativePath source, WorkspaceRelativePath destination, WorkspaceMoveIntent intent, CancellationToken cancellationToken) => inner.MoveDirectoryAsync(source, destination, intent, cancellationToken);
    }

    private sealed class CorruptingWriteStream(Stream inner) : Stream
    {
        private bool _corrupted;
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count)
        {
            var copy = buffer.AsSpan(offset, count).ToArray();
            Corrupt(copy);
            inner.Write(copy, 0, copy.Length);
        }

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var copy = buffer.ToArray();
            Corrupt(copy);
            await inner.WriteAsync(copy, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }

        private void Corrupt(byte[] bytes)
        {
            if (!_corrupted && bytes.Length > 0)
            {
                bytes[0] ^= 0xff;
                _corrupted = true;
            }
        }
    }
}
