using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DevForge.Application.Contracts;
using DevForge.Application.Contracts.Persistence;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Infrastructure;
using DevForge.Infrastructure.Blueprints;
using DevForge.Infrastructure.Execution;
using DevForge.Infrastructure.FileSystem;

namespace DevForge.IntegrationTests.Infrastructure.Execution;

public sealed class BlueprintExecutionSourceTests
{
    [Fact]
    public async Task OpensExactPackageAsReadOnlyVerifiedByteSnapshot()
    {
        await using var fixture = await ExecutionSourceFixture.CreateAsync(
            BlueprintSourceProvenance.BuiltIn);
        var directory = await fixture.WriteValidPackageAsync();
        var fingerprint = await fixture.LoadFingerprintAsync(directory);
        var source = fixture.CreateExecutionSource();

        var result = await source.OpenAsync(
            Reference("sample.blueprint", "1.2.3"),
            fingerprint,
            CancellationToken.None);
        Assert.True(result.IsSuccessful);
        Assert.Equal(fingerprint, result.Value.Blueprint.Fingerprint);

        await fixture.ReplaceTemplateAsync(directory, "changed after verification");
        var content = await ReadTextAsync(
            result.Value.PackageWorkspace,
            Relative("templates\\app.txt"));

        Assert.Equal("{{ project.name }}", content);
        Assert.Equal("[WORKSPACE ROOT]", result.Value.PackageWorkspace.Root.ToString());
        await Assert.ThrowsAsync<InfrastructureOperationException>(
            () => result.Value.PackageWorkspace.CreateDirectoryAsync(
                Relative("mutation"),
                CancellationToken.None));
    }

    [Fact]
    public async Task MissingSourceOrPackageFailsWithStableErrorWithoutAbsolutePath()
    {
        await using var fixture = await ExecutionSourceFixture.CreateAsync(
            BlueprintSourceProvenance.BuiltIn);
        var directory = await fixture.WriteValidPackageAsync();
        var loaded = await fixture.LoadFingerprintAsync(directory);
        var missingSource = BlueprintFingerprint.Create(
            "missing",
            loaded.PackageDirectory,
            loaded.Trust,
            loaded.AggregateChecksum).Value;
        var missingPackage = BlueprintFingerprint.Create(
            loaded.SourceId,
            Relative("missing.package"),
            loaded.Trust,
            loaded.AggregateChecksum).Value;
        var source = fixture.CreateExecutionSource();

        var sourceResult = await source.OpenAsync(
            Reference("sample.blueprint", "1.2.3"),
            missingSource,
            CancellationToken.None);
        var packageResult = await source.OpenAsync(
            Reference("sample.blueprint", "1.2.3"),
            missingPackage,
            CancellationToken.None);

        AssertExecutionFailure(sourceResult, fixture.RootPath);
        AssertExecutionFailure(packageResult, fixture.RootPath);
    }

    [Fact]
    public async Task ChangedPackageChecksumBlocksExecution()
    {
        await using var fixture = await ExecutionSourceFixture.CreateAsync(
            BlueprintSourceProvenance.BuiltIn);
        var directory = await fixture.WriteValidPackageAsync();
        var fingerprint = await fixture.LoadFingerprintAsync(directory);
        await fixture.ReplaceTemplateAsync(directory, "changed without checksum update");

        var result = await fixture.CreateExecutionSource().OpenAsync(
            Reference("sample.blueprint", "1.2.3"),
            fingerprint,
            CancellationToken.None);

        AssertExecutionFailure(result, fixture.RootPath);
    }

    [Fact]
    public async Task ManifestIdentityMustMatchPlannedReference()
    {
        await using var fixture = await ExecutionSourceFixture.CreateAsync(
            BlueprintSourceProvenance.BuiltIn);
        var directory = await fixture.WriteValidPackageAsync();
        var fingerprint = await fixture.LoadFingerprintAsync(directory);

        var result = await fixture.CreateExecutionSource().OpenAsync(
            Reference("different.blueprint", "1.2.3"),
            fingerprint,
            CancellationToken.None);

        AssertExecutionFailure(result, fixture.RootPath);
    }

    [Fact]
    public async Task TrustedLocalPackageRequiresCurrentMatchingMetadataTrust()
    {
        await using var fixture = await ExecutionSourceFixture.CreateAsync(
            BlueprintSourceProvenance.Local);
        var directory = await fixture.WriteValidPackageAsync();
        var loaded = await fixture.LoadFingerprintAsync(directory);
        var trusted = BlueprintFingerprint.Create(
            loaded.SourceId,
            loaded.PackageDirectory,
            BlueprintTrust.TrustedLocal,
            loaded.AggregateChecksum).Value;
        fixture.Metadata.Record = BlueprintMetadataRecord.Create(
            "sample.blueprint",
            "1.2.3",
            BlueprintSource.Local,
            BlueprintTrust.TrustedLocal,
            loaded.AggregateChecksum["sha256:".Length..],
            isDisabled: false,
            DateTimeOffset.UnixEpoch).Value;

        var allowed = await fixture.CreateExecutionSource().OpenAsync(
            Reference("sample.blueprint", "1.2.3"),
            trusted,
            CancellationToken.None);
        fixture.Metadata.Record = null;
        var revoked = await fixture.CreateExecutionSource().OpenAsync(
            Reference("sample.blueprint", "1.2.3"),
            trusted,
            CancellationToken.None);

        Assert.True(allowed.IsSuccessful);
        Assert.Equal(BlueprintTrust.TrustedLocal, allowed.Value.Blueprint.Manifest.Trust);
        AssertExecutionFailure(revoked, fixture.RootPath);
    }

    [Fact]
    public async Task BuiltInPackageDisabledAfterPlanningCannotBeReopenedForExecution()
    {
        await using var fixture = await ExecutionSourceFixture.CreateAsync(
            BlueprintSourceProvenance.BuiltIn);
        var directory = await fixture.WriteValidPackageAsync();
        var fingerprint = await fixture.LoadFingerprintAsync(directory);
        fixture.Metadata.Record = BlueprintMetadataRecord.Create(
            "sample.blueprint",
            "1.2.3",
            BlueprintSource.BuiltIn,
            BlueprintTrust.BuiltIn,
            fingerprint.AggregateChecksum["sha256:".Length..],
            isDisabled: true,
            DateTimeOffset.UnixEpoch).Value;

        var result = await fixture.CreateExecutionSource().OpenAsync(
            Reference("sample.blueprint", "1.2.3"),
            fingerprint,
            CancellationToken.None);

        AssertExecutionFailure(result, fixture.RootPath);
    }

    [Fact]
    public async Task PreCancelledOpenDoesNotReadBlueprintSource()
    {
        await using var fixture = await ExecutionSourceFixture.CreateAsync(
            BlueprintSourceProvenance.BuiltIn);
        var directory = await fixture.WriteValidPackageAsync();
        var fingerprint = await fixture.LoadFingerprintAsync(directory);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.CreateExecutionSource().OpenAsync(
                Reference("sample.blueprint", "1.2.3"),
                fingerprint,
                cancellation.Token));
    }

    [Fact]
    public async Task CancellationAfterFinalVerifiedReadPreventsSnapshotPublication()
    {
        await using var fixture = await ExecutionSourceFixture.CreateAsync(
            BlueprintSourceProvenance.BuiltIn);
        var directory = await fixture.WriteValidPackageAsync();
        var fingerprint = await fixture.LoadFingerprintAsync(directory);
        using var cancellation = new CancellationTokenSource();
        var cancellingWorkspace = new CancelAfterSecondTemplateReadWorkspace(
            fixture.Workspace,
            cancellation);
        var source = fixture.CreateExecutionSource(cancellingWorkspace);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => source.OpenAsync(
                Reference("sample.blueprint", "1.2.3"),
                fingerprint,
                cancellation.Token));
    }

    [Fact]
    public async Task TrustRevokedDuringVerifiedCapturePreventsSnapshotPublication()
    {
        await using var fixture = await ExecutionSourceFixture.CreateAsync(
            BlueprintSourceProvenance.Local);
        var directory = await fixture.WriteValidPackageAsync();
        var loaded = await fixture.LoadFingerprintAsync(directory);
        var trusted = BlueprintFingerprint.Create(
            loaded.SourceId,
            loaded.PackageDirectory,
            BlueprintTrust.TrustedLocal,
            loaded.AggregateChecksum).Value;
        var trustedRecord = BlueprintMetadataRecord.Create(
            "sample.blueprint",
            "1.2.3",
            BlueprintSource.Local,
            BlueprintTrust.TrustedLocal,
            loaded.AggregateChecksum["sha256:".Length..],
            isDisabled: false,
            DateTimeOffset.UnixEpoch).Value;
        var metadata = new RevokeAfterFirstReadMetadataStore(trustedRecord);

        var result = await fixture.CreateExecutionSource(metadataStore: metadata).OpenAsync(
            Reference("sample.blueprint", "1.2.3"),
            trusted,
            CancellationToken.None);

        AssertExecutionFailure(result, fixture.RootPath);
        Assert.Equal(2, metadata.ReadCount);
    }

    private static void AssertExecutionFailure(
        ExecutionOperationResult<BlueprintExecutionPackage> result,
        string absolutePath)
    {
        Assert.False(result.IsSuccessful);
        Assert.Equal("DF-EXEC-003", result.Error?.Code);
        Assert.DoesNotContain(
            absolutePath,
            result.Error?.TechnicalDetail.Value,
            StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> ReadTextAsync(
        IWorkspaceFileSystem workspace,
        WorkspaceRelativePath path)
    {
        await using var stream = await workspace.OpenReadAsync(path, CancellationToken.None);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync(CancellationToken.None);
    }

    private static BlueprintReference Reference(string id, string version) =>
        BlueprintReference.Create(id, version).Value;

    private static WorkspaceRelativePath Relative(string value) =>
        WorkspaceRelativePath.Create(value).Value;

    private sealed class ExecutionSourceFixture : IAsyncDisposable
    {
        private const string Manifest = """
            id: sample.blueprint
            name: Sample Blueprint
            version: 1.2.3
            engineVersion: ">=1.0.0 <2.0.0"
            tools:
              - id: dotnet
                version: ">=10.0.0 <11.0.0"
                required: true
            features: []
            actions:
              - id: create-source
                handler: create-directory
                timeoutSeconds: 30
                parameters:
                  path: src
            validators: []
            artifacts:
              - path: src
            dependencies: []
            """;

        private const string InputSchema = """
            {
              "type": "object",
              "properties": {
                "project-name": {
                  "type": "string",
                  "default": "sample",
                  "minLength": 1,
                  "maxLength": 80
                }
              },
              "required": ["project-name"],
              "additionalProperties": false
            }
            """;

        private const string Rules = """
            - id: windows-only
              condition: runtime.os == "windows"
              severity: blocking
              message: Windows is required.
              remediation: Select a Windows environment.
              override: none
            """;

        private ExecutionSourceFixture(
            string rootPath,
            IWorkspaceFileSystem workspace,
            BlueprintPackageSource source)
        {
            RootPath = rootPath;
            Workspace = workspace;
            Source = source;
        }

        public string RootPath { get; }

        public IWorkspaceFileSystem Workspace { get; }

        public BlueprintPackageSource Source { get; }

        public MutableMetadataStore Metadata { get; } = new();

        public static async Task<ExecutionSourceFixture> CreateAsync(
            BlueprintSourceProvenance provenance)
        {
            var rootPath = Path.GetFullPath(Path.Combine(
                Path.GetTempPath(),
                "DevForge-M5-ExecutionSource-" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(rootPath);
            var workspace = await new WindowsFileSystem().OpenWorkspaceAsync(
                WorkspaceRoot.Create(rootPath).Value,
                CancellationToken.None);
            var source = BlueprintPackageSource.Create("fixture", workspace, provenance).Value;
            return new ExecutionSourceFixture(rootPath, workspace, source);
        }

        public BlueprintExecutionSource CreateExecutionSource(
            IWorkspaceFileSystem? workspace = null,
            IBlueprintMetadataStore? metadataStore = null)
        {
            var source = workspace is null
                ? Source
                : BlueprintPackageSource.Create(Source.Id, workspace, Source.Provenance).Value;
            return new BlueprintExecutionSource([source], metadataStore ?? Metadata);
        }

        public async Task<WorkspaceRelativePath> WriteValidPackageAsync()
        {
            var package = Relative("sample.blueprint");
            await Workspace.CreateDirectoryAsync(package, CancellationToken.None);
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["manifest.yaml"] = Encoding.UTF8.GetBytes(Manifest),
                ["inputs.schema.json"] = Encoding.UTF8.GetBytes(InputSchema),
                ["rules.yaml"] = Encoding.UTF8.GetBytes(Rules),
                ["templates/app.txt"] = Encoding.UTF8.GetBytes("{{ project.name }}"),
            };
            foreach (var file in files)
            {
                await WriteAsync(
                    Relative("sample.blueprint\\" + file.Key.Replace('/', '\\')),
                    file.Value,
                    overwrite: false);
            }

            var checksums = files.ToDictionary(
                item => item.Key,
                item => Convert.ToHexString(SHA256.HashData(item.Value)).ToLowerInvariant(),
                StringComparer.Ordinal);
            await WriteAsync(
                Relative("sample.blueprint\\checksums.json"),
                JsonSerializer.SerializeToUtf8Bytes(checksums),
                overwrite: false);
            return package;
        }

        public async Task<BlueprintFingerprint> LoadFingerprintAsync(
            WorkspaceRelativePath packageDirectory)
        {
            var loaded = await new BlueprintPackageLoader().LoadAsync(
                Source,
                packageDirectory,
                CancellationToken.None);
            return loaded.Package!.Fingerprint;
        }

        public Task ReplaceTemplateAsync(
            WorkspaceRelativePath packageDirectory,
            string content) =>
            WriteAsync(
                Relative(packageDirectory.Value + "\\templates\\app.txt"),
                Encoding.UTF8.GetBytes(content),
                overwrite: true);

        public ValueTask DisposeAsync()
        {
            var fullPath = Path.GetFullPath(RootPath);
            if (!fullPath.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(fullPath).StartsWith(
                    "DevForge-M5-ExecutionSource-",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Refusing to clean an unexpected execution-source fixture.");
            }

            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, recursive: true);
            }

            return ValueTask.CompletedTask;
        }

        private async Task WriteAsync(
            WorkspaceRelativePath path,
            byte[] content,
            bool overwrite)
        {
            var separator = path.Value.LastIndexOf('\\');
            if (separator > 0)
            {
                await Workspace.CreateDirectoryAsync(
                    Relative(path.Value[..separator]),
                    CancellationToken.None);
            }

            await using var stream = await Workspace.OpenWriteAsync(
                path,
                overwrite,
                CancellationToken.None);
            await stream.WriteAsync(content, CancellationToken.None);
        }
    }

    private sealed class MutableMetadataStore : IBlueprintMetadataStore
    {
        public BlueprintMetadataRecord? Record { get; set; }

        public Task<ImmutableArray<BlueprintMetadataRecord>> ListAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Record is null
                ? ImmutableArray<BlueprintMetadataRecord>.Empty
                : ImmutableArray.Create(Record));
        }

        public Task<BlueprintMetadataRecord?> GetAsync(
            string id,
            string version,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Record is not null
                && StringComparer.Ordinal.Equals(Record.Id, id)
                && StringComparer.Ordinal.Equals(Record.Version, version)
                    ? Record
                    : null);
        }

        public Task UpsertAsync(
            BlueprintMetadataRecord blueprint,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> RemoveAsync(
            string id,
            string version,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RevokeAfterFirstReadMetadataStore(
        BlueprintMetadataRecord trustedRecord) : IBlueprintMetadataStore
    {
        private int _readCount;

        public int ReadCount => _readCount;

        public Task<ImmutableArray<BlueprintMetadataRecord>> ListAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<BlueprintMetadataRecord?> GetAsync(
            string id,
            string version,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = Interlocked.Increment(ref _readCount);
            return Task.FromResult<BlueprintMetadataRecord?>(read == 1 ? trustedRecord : null);
        }

        public Task UpsertAsync(
            BlueprintMetadataRecord blueprint,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> RemoveAsync(
            string id,
            string version,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class CancelAfterSecondTemplateReadWorkspace(
        IWorkspaceFileSystem inner,
        CancellationTokenSource cancellation) : IWorkspaceFileSystem
    {
        private int _templateReads;

        public WorkspaceRoot Root => inner.Root;

        public Task<bool> FileExistsAsync(
            WorkspaceRelativePath path,
            CancellationToken cancellationToken) => inner.FileExistsAsync(path, cancellationToken);

        public Task<bool> DirectoryExistsAsync(
            WorkspaceRelativePath path,
            CancellationToken cancellationToken) => inner.DirectoryExistsAsync(path, cancellationToken);

        public Task CreateDirectoryAsync(
            WorkspaceRelativePath path,
            CancellationToken cancellationToken) => inner.CreateDirectoryAsync(path, cancellationToken);

        public async Task<Stream> OpenReadAsync(
            WorkspaceRelativePath path,
            CancellationToken cancellationToken)
        {
            var stream = await inner.OpenReadAsync(path, cancellationToken);
            return path.Value.EndsWith("\\templates\\app.txt", StringComparison.OrdinalIgnoreCase)
                && Interlocked.Increment(ref _templateReads) == 2
                ? new CancelAtEndOfStream(stream, cancellation)
                : stream;
        }

        public Task<Stream> OpenWriteAsync(
            WorkspaceRelativePath path,
            bool overwrite,
            CancellationToken cancellationToken) => inner.OpenWriteAsync(path, overwrite, cancellationToken);

        public Task DeleteFileAsync(
            WorkspaceRelativePath path,
            CancellationToken cancellationToken) => inner.DeleteFileAsync(path, cancellationToken);

        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateAllFilesAsync(
            CancellationToken cancellationToken) => inner.EnumerateAllFilesAsync(cancellationToken);

        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateRootDirectoriesAsync(
            CancellationToken cancellationToken) => inner.EnumerateRootDirectoriesAsync(cancellationToken);

        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateFilesAsync(
            WorkspaceRelativePath directory,
            bool recursive,
            CancellationToken cancellationToken) => inner.EnumerateFilesAsync(
                directory,
                recursive,
                cancellationToken);

        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateDirectoriesAsync(
            WorkspaceRelativePath directory,
            CancellationToken cancellationToken) => inner.EnumerateDirectoriesAsync(directory, cancellationToken);

        public Task DeleteDirectoryAsync(
            WorkspaceRelativePath path,
            DirectoryCleanupIntent intent,
            CancellationToken cancellationToken) => inner.DeleteDirectoryAsync(path, intent, cancellationToken);

        public Task MoveDirectoryAsync(
            WorkspaceRelativePath source,
            WorkspaceRelativePath destination,
            WorkspaceMoveIntent intent,
            CancellationToken cancellationToken) => inner.MoveDirectoryAsync(
                source,
                destination,
                intent,
                cancellationToken);
    }

    private sealed class CancelAtEndOfStream(
        Stream inner,
        CancellationTokenSource cancellation) : Stream
    {
        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => inner.CanSeek;

        public override bool CanWrite => false;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            CancelAtEnd(read);
            return read;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken);
            CancelAtEnd(read);
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }

        private void CancelAtEnd(int read)
        {
            if (read == 0)
            {
                cancellation.Cancel();
            }
        }
    }
}
