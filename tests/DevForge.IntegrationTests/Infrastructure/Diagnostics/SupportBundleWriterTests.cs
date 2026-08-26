using System.Collections.Immutable;
using System.IO.Compression;
using System.Security.Cryptography;
using DevForge.Application.Contracts;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Environment;
using DevForge.Domain.Execution;
using DevForge.Domain.Privacy;
using DevForge.Domain.Projects;
using DevForge.Domain.Runs;
using DevForge.Infrastructure.Diagnostics;
using DevForge.Infrastructure.FileSystem;

namespace DevForge.IntegrationTests.Infrastructure.Diagnostics;

public sealed class SupportBundleWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "DevForge-M10-Support-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task WritesDeterministicAllowlistedIntegrityInventoriedBundleAndAdoptsRetry()
    {
        var localData = Path.Combine(_root, "local-data");
        var runArtifacts = Path.Combine(_root, "run-artifacts");
        Directory.CreateDirectory(Path.Combine(runArtifacts, "reports"));
        await File.WriteAllTextAsync(
            Path.Combine(runArtifacts, "reports", "run-1.json"),
            "{\"schemaVersion\":1,\"status\":\"failed\"}\n");
        await File.WriteAllTextAsync(
            Path.Combine(runArtifacts, "reports", "run-1.md"),
            "\uFEFF# Safe generation report\r\n");
        await File.WriteAllTextAsync(Path.Combine(runArtifacts, ".env"), "FORBIDDEN=1");
        await File.WriteAllTextAsync(Path.Combine(runArtifacts, "customer-source.cs"), "class Secret {}");
        Directory.CreateDirectory(localData);
        using (var sink = new JsonLinesDiagnosticSink(
                   new WindowsFileSystem(),
                   WorkspaceRoot.Create(localData).Value))
        {
            await sink.WriteAsync(CreateDiagnostic(), CancellationToken.None);
        }

        var checkpoint = CreateCheckpoint(runArtifacts);
        var writer = new SupportBundleWriter(
            new WindowsFileSystem(),
            WorkspaceRoot.Create(localData).Value,
            new StubEnvironmentDoctor(),
            new FixedTimeProvider());

        var first = await writer.WriteAsync(checkpoint, true, CancellationToken.None);
        var firstBytes = await File.ReadAllBytesAsync(
            Path.Combine(localData, first.Value.RelativePath.Value));
        var second = await writer.WriteAsync(checkpoint, true, CancellationToken.None);
        var secondBytes = await File.ReadAllBytesAsync(
            Path.Combine(localData, second.Value.RelativePath.Value));

        Assert.True(first.IsSuccessful);
        Assert.True(second.IsSuccessful);
        Assert.Equal(first.Value, second.Value);
        Assert.Equal(firstBytes, secondBytes);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(firstBytes)).ToLowerInvariant(), first.Value.Sha256);
        using var archive = new ZipArchive(new MemoryStream(firstBytes), ZipArchiveMode.Read);
        var names = archive.Entries.Select(entry => entry.FullName).ToArray();
        Assert.Equal(names.Order(StringComparer.Ordinal), names);
        Assert.Equal(
            [
                "blueprint/manifest-checksum.json",
                "catalog/error-catalog.json",
                "environment/tool-status.json",
                "inventory.json",
                "logs/run.jsonl",
                "run/checkpoint.json",
                "run/generation-report.json",
                "run/generation-report.md",
                "run/plan-summary.json",
                "run/recipe-summary.json",
            ],
            names);
        Assert.DoesNotContain(names, name =>
            name.Contains(".env", StringComparison.OrdinalIgnoreCase)
            || name.Contains("source", StringComparison.OrdinalIgnoreCase)
            || name.Contains("devforge.db", StringComparison.OrdinalIgnoreCase));
        var inventory = archive.GetEntry("inventory.json")!;
        using var reader = new StreamReader(inventory.Open());
        var inventoryText = await reader.ReadToEndAsync();
        Assert.Contains("devforge-support-bundle-inventory-v1", inventoryText, StringComparison.Ordinal);
        Assert.Contains("sha256", inventoryText, StringComparison.Ordinal);
        var markdown = archive.GetEntry("run/generation-report.md")!;
        using var markdownReader = new StreamReader(markdown.Open());
        var markdownText = await markdownReader.ReadToEndAsync();
        Assert.Equal("# Safe generation report\n", markdownText);
        Assert.Empty(Directory.EnumerateDirectories(
            Path.Combine(localData, "support-bundles", ".staging")));
    }

    [Fact]
    public async Task SecretShapedReportBlocksExportAndCreatesNoArchive()
    {
        var localData = Path.Combine(_root, "local-data");
        var runArtifacts = Path.Combine(_root, "run-artifacts");
        Directory.CreateDirectory(Path.Combine(runArtifacts, "reports"));
        await File.WriteAllTextAsync(
            Path.Combine(runArtifacts, "reports", "run-1.json"),
            "{\"note\":\"ghp_abcdefghijklmnop\"}\n");
        Directory.CreateDirectory(localData);
        var writer = CreateWriter(localData);

        var result = await writer.WriteAsync(
            CreateCheckpoint(runArtifacts),
            includeEnvironmentSnapshot: false,
            CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Equal("DF-SUPPORT-002", result.Error!.Code);
        Assert.Empty(Directory.EnumerateFiles(localData, "*.zip", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task CleanupRequiresExactOwnershipMarkerAndIsIdempotent()
    {
        var localData = Path.Combine(_root, "local-data");
        var runArtifacts = Path.Combine(_root, "run-artifacts");
        Directory.CreateDirectory(runArtifacts);
        Directory.CreateDirectory(localData);
        var writer = CreateWriter(localData);
        var exported = await writer.WriteAsync(
            CreateCheckpoint(runArtifacts),
            includeEnvironmentSnapshot: false,
            CancellationToken.None);

        var first = await writer.CleanupAsync(exported.Value, CancellationToken.None);
        var second = await writer.CleanupAsync(exported.Value, CancellationToken.None);

        Assert.True(first.IsSuccessful);
        Assert.True(first.Value.WasPresent);
        Assert.True(second.IsSuccessful);
        Assert.False(second.Value.WasPresent);
        Assert.False(File.Exists(Path.Combine(localData, exported.Value.RelativePath.Value)));
    }

    [Fact]
    public async Task RetryRejectsOwnershipMarkerWithUnknownFields()
    {
        var localData = Path.Combine(_root, "local-data");
        var runArtifacts = Path.Combine(_root, "run-artifacts");
        Directory.CreateDirectory(runArtifacts);
        Directory.CreateDirectory(localData);
        var writer = CreateWriter(localData);
        var checkpoint = CreateCheckpoint(runArtifacts);
        var exported = await writer.WriteAsync(checkpoint, false, CancellationToken.None);
        var markerPath = Path.Combine(
            localData,
            "support-bundles",
            exported.Value.BundleId + ".owner.json");
        var marker = (await File.ReadAllTextAsync(markerPath)).TrimEnd();
        await File.WriteAllTextAsync(markerPath, marker[..^1] + ",\"unexpected\":true}\n");

        var retried = await writer.WriteAsync(checkpoint, false, CancellationToken.None);

        Assert.False(retried.IsSuccessful);
        Assert.True(File.Exists(Path.Combine(localData, exported.Value.RelativePath.Value)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CancellationAtAtomicPublishLeavesOnlyRecoverableOwnedStateAndRetryCompletes(
        int cancelAtZipWrite)
    {
        var localData = Path.Combine(_root, "local-data");
        var runArtifacts = Path.Combine(_root, "run-artifacts");
        Directory.CreateDirectory(runArtifacts);
        Directory.CreateDirectory(localData);
        var checkpoint = CreateCheckpoint(runArtifacts);
        var faulting = new SupportBundleWriter(
            new PublishCancellingFileSystem(new WindowsFileSystem(), cancelAtZipWrite),
            WorkspaceRoot.Create(localData).Value,
            new StubEnvironmentDoctor(),
            new FixedTimeProvider());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => faulting.WriteAsync(
            checkpoint,
            includeEnvironmentSnapshot: false,
            CancellationToken.None));

        Assert.Empty(Directory.EnumerateFiles(
            Path.Combine(localData, "support-bundles"),
            "*.zip",
            SearchOption.TopDirectoryOnly));
        Assert.NotEmpty(Directory.EnumerateFiles(
            localData,
            "*.owner.json",
            SearchOption.AllDirectories));
        var recovered = await CreateWriter(localData).WriteAsync(
            checkpoint,
            includeEnvironmentSnapshot: false,
            CancellationToken.None);
        Assert.True(recovered.IsSuccessful);
        Assert.True(File.Exists(Path.Combine(localData, recovered.Value.RelativePath.Value)));
    }

    [Fact]
    public async Task CleanupRefusesCanonicalLookingArchiveWithoutOwnershipMarker()
    {
        var localData = Path.Combine(_root, "local-data");
        var runArtifacts = Path.Combine(_root, "run-artifacts");
        Directory.CreateDirectory(runArtifacts);
        Directory.CreateDirectory(localData);
        var writer = CreateWriter(localData);
        var exported = await writer.WriteAsync(
            CreateCheckpoint(runArtifacts),
            includeEnvironmentSnapshot: false,
            CancellationToken.None);
        var markerPath = Path.Combine(
            localData,
            "support-bundles",
            exported.Value.BundleId + ".owner.json");
        File.Delete(markerPath);

        var result = await writer.CleanupAsync(exported.Value, CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.True(File.Exists(Path.Combine(localData, exported.Value.RelativePath.Value)));
    }

    [Fact]
    public async Task OversizedEnvironmentToolCollectionFailsBeforeArchivePublication()
    {
        var localData = Path.Combine(_root, "local-data");
        var runArtifacts = Path.Combine(_root, "run-artifacts");
        Directory.CreateDirectory(runArtifacts);
        Directory.CreateDirectory(localData);
        var writer = new SupportBundleWriter(
            new WindowsFileSystem(),
            WorkspaceRoot.Create(localData).Value,
            new OversizedEnvironmentDoctor(),
            new FixedTimeProvider());

        var result = await writer.WriteAsync(
            CreateCheckpoint(runArtifacts),
            includeEnvironmentSnapshot: true,
            CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Empty(Directory.EnumerateFiles(localData, "*.zip", SearchOption.AllDirectories));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static DiagnosticEvent CreateDiagnostic() =>
        DiagnosticEvent.Create(
            new DateTimeOffset(2026, 8, 26, 8, 0, 0, TimeSpan.Zero),
            DiagnosticLevel.Error,
            "execution.failed",
            "run-1",
            "create",
            1,
            "execution-orchestrator",
            RedactedText.FromTrustedRedaction("A safe failure occurred.").Value,
            10,
            "DF-EXEC-001").Value;

    private static SupportBundleWriter CreateWriter(string localData) =>
        new(
            new WindowsFileSystem(),
            WorkspaceRoot.Create(localData).Value,
            new StubEnvironmentDoctor(),
            new FixedTimeProvider());

    private static RunCheckpoint CreateCheckpoint(string runArtifactRoot)
    {
        var blueprint = BlueprintReference.Create("sample.local", "1.0.0").Value;
        var step = ExecutionStep.Create(
            "create", "Create", "create-directory", [], TimeSpan.FromSeconds(30), RetryPolicy.None).Value;
        var plan = ExecutionPlan.Create($"sha256:{new string('1', 64)}", [step], []).Value;
        var fingerprint = BlueprintFingerprint.Create(
            "trusted-local",
            WorkspaceRelativePath.Create("sample.local\\1.0.0").Value,
            BlueprintTrust.TrustedLocal,
            $"sha256:{new string('2', 64)}").Value;
        var run = ProjectRun.Create("run-1", "recipe-1").Value
            .TransitionTo(RunStatus.Planning).Value
            .TransitionTo(RunStatus.Executing).Value
            .TransitionTo(RunStatus.Failed).Value;
        return RunCheckpoint.Create(
            run,
            plan,
            blueprint,
            fingerprint,
            StagingDescriptor.Create(
                WorkspaceRelativePath.Create(".devforge-staging\\run-1").Value,
                WorkspaceRelativePath.Create(".devforge-staging\\run-1\\payload").Value,
                WorkspaceRelativePath.Create(".devforge-staging\\run-1\\ownership.json").Value,
                "marker-1").Value,
            TargetDescriptor.Create(
                WorkspaceRoot.Create("C:\\Projects").Value,
                WorkspaceRelativePath.Create("sample").Value,
                WorkspaceRelativePath.Create(".devforge-finalize-run-1").Value).Value,
            RunArtifactDescriptor.Create(WorkspaceRoot.Create(runArtifactRoot).Value).Value,
            [],
            FinalizationState.Succeeded,
            ReportPersistenceState.Succeeded).Value;
    }

    private sealed class StubEnvironmentDoctor : IEnvironmentDoctor
    {
        public Task<EnvironmentSnapshot> InspectAsync(CancellationToken cancellationToken) =>
            Task.FromResult(EnvironmentSnapshot.Create(
                FixedTimeProvider.UtcNow,
                [new EnvironmentTool("dotnet", "10.0.302", true)],
                []).Value);
    }

    private sealed class OversizedEnvironmentDoctor : IEnvironmentDoctor
    {
        public Task<EnvironmentSnapshot> InspectAsync(CancellationToken cancellationToken) =>
            Task.FromResult(EnvironmentSnapshot.Create(
                FixedTimeProvider.UtcNow,
                Enumerable.Range(0, 65).Select(index =>
                    new EnvironmentTool($"tool-{index:D2}", "1.0.0", true)),
                []).Value);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        internal static readonly DateTimeOffset UtcNow =
            new(2026, 8, 26, 8, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class PublishCancellingFileSystem(
        IFileSystem inner,
        int cancelAtZipWrite) : IFileSystem
    {
        private int _zipWrites;

        public Task EnsureWorkspaceExistsAsync(
            WorkspaceRoot allowedRoot,
            CancellationToken cancellationToken) =>
            inner.EnsureWorkspaceExistsAsync(allowedRoot, cancellationToken);

        public async Task<IWorkspaceFileSystem> OpenWorkspaceAsync(
            WorkspaceRoot allowedRoot,
            CancellationToken cancellationToken)
        {
            var workspace = await inner.OpenWorkspaceAsync(allowedRoot, cancellationToken);
            return new PublishCancellingWorkspace(
                Assert.IsAssignableFrom<IAtomicFileWorkspaceFileSystem>(workspace),
                Assert.IsAssignableFrom<IAtomicWorkspaceFileSystem>(workspace),
                Assert.IsAssignableFrom<IExclusiveLeaseWorkspaceFileSystem>(workspace),
                () => Interlocked.Increment(ref _zipWrites) == cancelAtZipWrite);
        }
    }

    private sealed class PublishCancellingWorkspace(
        IAtomicFileWorkspaceFileSystem atomic,
        IAtomicWorkspaceFileSystem atomicDirectories,
        IExclusiveLeaseWorkspaceFileSystem leases,
        Func<bool> shouldCancel) :
        IAtomicFileWorkspaceFileSystem,
        IAtomicWorkspaceFileSystem,
        IExclusiveLeaseWorkspaceFileSystem
    {
        public WorkspaceRoot Root => atomic.Root;

        public Task<bool> FileExistsAsync(WorkspaceRelativePath path, CancellationToken token) =>
            atomic.FileExistsAsync(path, token);

        public Task<bool> DirectoryExistsAsync(WorkspaceRelativePath path, CancellationToken token) =>
            atomic.DirectoryExistsAsync(path, token);

        public Task CreateDirectoryAsync(WorkspaceRelativePath path, CancellationToken token) =>
            atomic.CreateDirectoryAsync(path, token);

        public Task<bool> TryCreateDirectoryAsync(
            WorkspaceRelativePath path,
            CancellationToken token) => atomicDirectories.TryCreateDirectoryAsync(path, token);

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
            CancellationToken token) => path.Value.EndsWith(".zip", StringComparison.Ordinal)
                && shouldCancel()
                    ? throw new OperationCanceledException()
                    : atomic.WriteFileAtomicallyAsync(path, content, overwrite, token);
    }
}
