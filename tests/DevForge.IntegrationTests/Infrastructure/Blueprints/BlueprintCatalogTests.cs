using System.Collections.Immutable;
using DevForge.Application.Contracts;
using DevForge.Application.Contracts.Persistence;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Infrastructure.Blueprints;

namespace DevForge.IntegrationTests.Infrastructure.Blueprints;

public sealed class BlueprintCatalogTests
{
    [Fact]
    public async Task RefreshReconcilesTrustAndDisabledStateWithoutWritingMetadata()
    {
        var builtInWorkspace = new CatalogWorkspace(
            "built-in.blueprint",
            "disabled-built-in.blueprint");
        var localWorkspace = new CatalogWorkspace(
            "trusted.blueprint",
            "changed.blueprint",
            "new.blueprint",
            "disabled.blueprint");
        var builtIn = Source("built-in", builtInWorkspace, BlueprintSourceProvenance.BuiltIn);
        var local = Source("local", localWorkspace, BlueprintSourceProvenance.Local);
        var loader = new CatalogLoader();
        loader.Add(builtIn, "built-in.blueprint", Package(builtIn, "built-in.blueprint", 'a'));
        loader.Add(
            builtIn,
            "disabled-built-in.blueprint",
            Package(builtIn, "disabled-built-in.blueprint", 'f'));
        loader.Add(local, "trusted.blueprint", Package(local, "trusted.blueprint", 'b'));
        loader.Add(local, "changed.blueprint", Package(local, "changed.blueprint", 'c'));
        loader.Add(local, "new.blueprint", Package(local, "new.blueprint", 'd'));
        loader.Add(local, "disabled.blueprint", Package(local, "disabled.blueprint", 'e'));
        var metadata = new CatalogMetadataStore(
            Metadata("trusted.blueprint", 'b', BlueprintTrust.TrustedLocal),
            Metadata("changed.blueprint", 'f', BlueprintTrust.TrustedLocal),
            Metadata("disabled.blueprint", 'e', BlueprintTrust.TrustedLocal, isDisabled: true),
            Metadata(
                "disabled-built-in.blueprint",
                '0',
                BlueprintTrust.BuiltIn,
                isDisabled: true,
                source: BlueprintSource.BuiltIn));
        var catalog = new BlueprintCatalog([local, builtIn], metadata, loader);

        await catalog.RefreshAsync(CancellationToken.None);

        var executable = await catalog.ListAsync(CancellationToken.None);
        Assert.Equal(
            ["built-in.blueprint", "trusted.blueprint"],
            executable.Select(item => item.Manifest.Id));
        Assert.Equal(BlueprintTrust.BuiltIn, executable[0].Manifest.Trust);
        Assert.Equal(BlueprintTrust.TrustedLocal, executable[1].Manifest.Trust);
        var inspections = (await catalog.InspectAsync(CancellationToken.None)).Inspections;
        Assert.Equal(BlueprintTrust.Untrusted, Inspection(inspections, "changed.blueprint").Trust);
        Assert.Equal(BlueprintTrust.Untrusted, Inspection(inspections, "new.blueprint").Trust);
        Assert.True(Inspection(inspections, "disabled.blueprint").IsDisabled);
        Assert.True(Inspection(inspections, "disabled-built-in.blueprint").IsDisabled);
        Assert.Equal(0, metadata.WriteCount);
    }

    [Fact]
    public async Task RefreshQuarantinesIdentityConflictsAndOrdersExactLookupsDeterministically()
    {
        var firstWorkspace = new CatalogWorkspace("duplicate.blueprint", "ordered.blueprint-v1");
        var secondWorkspace = new CatalogWorkspace("duplicate.blueprint", "ordered.blueprint-v2");
        var first = Source("z-source", firstWorkspace, BlueprintSourceProvenance.BuiltIn);
        var second = Source("a-source", secondWorkspace, BlueprintSourceProvenance.BuiltIn);
        var loader = new CatalogLoader();
        loader.Add(first, "duplicate.blueprint", Package(first, "duplicate.blueprint", 'a'));
        loader.Add(second, "duplicate.blueprint", Package(second, "duplicate.blueprint", 'b'));
        loader.Add(first, "ordered.blueprint-v1", Package(first, "ordered.blueprint", 'c', "1.0.0"));
        loader.Add(second, "ordered.blueprint-v2", Package(second, "ordered.blueprint", 'd', "2.0.0"));
        var catalog = new BlueprintCatalog(
            [first, second],
            new CatalogMetadataStore(),
            loader);

        await catalog.RefreshAsync(CancellationToken.None);

        var executable = await catalog.ListAsync(CancellationToken.None);
        Assert.Equal(["2.0.0", "1.0.0"], executable.Select(item => item.Manifest.Version));
        Assert.DoesNotContain(executable, item => item.Manifest.Id == "duplicate.blueprint");
        var conflicts = (await catalog.InspectAsync(CancellationToken.None)).Inspections
            .Where(item => item.Reference?.Id == "duplicate.blueprint")
            .ToArray();
        Assert.Equal(2, conflicts.Length);
        Assert.All(conflicts, item => Assert.Equal("DF-BP-005", Assert.Single(item.Issues).Code));

        var exact = await catalog.FindAsync(
            BlueprintReference.Create("ordered.blueprint", "1.0.0").Value,
            CancellationToken.None);
        var missing = await catalog.FindAsync(
            BlueprintReference.Create("ordered.blueprint", "1.5.0").Value,
            CancellationToken.None);
        Assert.Equal("1.0.0", exact!.Manifest.Version);
        Assert.Null(missing);
    }

    [Fact]
    public async Task RefreshKeepsMalformedPackageAsInspectOnlyEntry()
    {
        var workspace = new CatalogWorkspace("malformed.blueprint");
        var source = Source("built-in", workspace, BlueprintSourceProvenance.BuiltIn);
        var loader = new CatalogLoader();
        loader.Add(
            source,
            "malformed.blueprint",
            FailedPackage(source, "malformed.blueprint", "DF-BP-001"));
        var catalog = new BlueprintCatalog([source], new CatalogMetadataStore(), loader);

        await catalog.RefreshAsync(CancellationToken.None);

        Assert.Empty(await catalog.ListAsync(CancellationToken.None));
        var inspection = Assert.Single((await catalog.InspectAsync(CancellationToken.None)).Inspections);
        Assert.Equal(BlueprintTrust.Quarantined, inspection.Trust);
        Assert.Equal("DF-BP-001", Assert.Single(inspection.Issues).Code);
    }

    [Fact]
    public async Task FailedRefreshRetainsThePreviousCompleteSnapshot()
    {
        var workspace = new CatalogWorkspace("stable.blueprint");
        var source = Source("built-in", workspace, BlueprintSourceProvenance.BuiltIn);
        var loader = new CatalogLoader();
        loader.Add(source, "stable.blueprint", Package(source, "stable.blueprint", 'a'));
        var metadata = new CatalogMetadataStore();
        var catalog = new BlueprintCatalog([source], metadata, loader);
        await catalog.RefreshAsync(CancellationToken.None);
        var previous = await catalog.InspectAsync(CancellationToken.None);

        workspace.Failure = new InvalidOperationException("source failure");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            catalog.RefreshAsync(CancellationToken.None));
        Assert.Same(previous, await catalog.InspectAsync(CancellationToken.None));

        workspace.Failure = null;
        metadata.Failure = new InvalidOperationException("metadata failure");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            catalog.RefreshAsync(CancellationToken.None));
        Assert.Same(previous, await catalog.InspectAsync(CancellationToken.None));

        metadata.Failure = null;
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            catalog.RefreshAsync(cancelled.Token));
        Assert.Same(previous, await catalog.InspectAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ConcurrentReadersObserveOnlyPreviousOrCompletePublishedSnapshot()
    {
        var workspace = new CatalogWorkspace("old.blueprint");
        var source = Source("built-in", workspace, BlueprintSourceProvenance.BuiltIn);
        var loader = new CatalogLoader();
        loader.Add(source, "old.blueprint", Package(source, "old.blueprint", 'a'));
        loader.Add(source, "new.blueprint", Package(source, "new.blueprint", 'b'));
        var catalog = new BlueprintCatalog([source], new CatalogMetadataStore(), loader);
        await catalog.RefreshAsync(CancellationToken.None);

        workspace.SetDirectories("new.blueprint");
        loader.Block("new.blueprint");
        var refresh = catalog.RefreshAsync(CancellationToken.None);
        await loader.WaitUntilBlockedAsync();
        var reads = await Task.WhenAll(Enumerable.Range(0, 32).Select(async _ =>
            (await catalog.ListAsync(CancellationToken.None)).Select(item => item.Manifest.Id).ToArray()));
        Assert.All(reads, ids => Assert.Equal(["old.blueprint"], ids));

        loader.Release();
        await refresh;
        Assert.Equal(
            ["new.blueprint"],
            (await catalog.ListAsync(CancellationToken.None)).Select(item => item.Manifest.Id));
    }

    [Fact]
    public async Task RefreshRejectsSourcePackageBoundWithoutPublishingAPartialSnapshot()
    {
        var workspace = new CatalogWorkspace("stable.blueprint");
        var source = Source("built-in", workspace, BlueprintSourceProvenance.BuiltIn);
        var loader = new CatalogLoader();
        loader.Add(source, "stable.blueprint", Package(source, "stable.blueprint", 'a'));
        var catalog = new BlueprintCatalog([source], new CatalogMetadataStore(), loader);
        await catalog.RefreshAsync(CancellationToken.None);
        var previous = await catalog.InspectAsync(CancellationToken.None);
        workspace.SetDirectories(
            [.. Enumerable.Range(0, BlueprintCatalog.MaximumPackagesPerSource + 1)
                .Select(index => $"package-{index:D3}")]);

        var exception = await Assert.ThrowsAsync<DevForge.Infrastructure.InfrastructureOperationException>(() =>
            catalog.RefreshAsync(CancellationToken.None));

        Assert.Equal("DF-BP-004", exception.Code);
        Assert.Same(previous, await catalog.InspectAsync(CancellationToken.None));
    }

    private static BlueprintInspection Inspection(
        ImmutableArray<BlueprintInspection> inspections,
        string id)
    {
        return Assert.Single(inspections, item => item.Reference?.Id == id);
    }

    private static BlueprintPackageSource Source(
        string id,
        IWorkspaceFileSystem workspace,
        BlueprintSourceProvenance provenance)
    {
        return BlueprintPackageSource.Create(id, workspace, provenance).Value;
    }

    private static BlueprintPackageLoadResult Package(
        BlueprintPackageSource source,
        string id,
        char checksumCharacter,
        string version = "1.0.0")
    {
        var trust = source.Provenance == BlueprintSourceProvenance.BuiltIn
            ? BlueprintTrust.BuiltIn
            : BlueprintTrust.Untrusted;
        var manifest = BlueprintManifest.Create(
            new BlueprintManifestDraft(
                id,
                version,
                ">=1.0.0 <2.0.0",
                [],
                [],
                [],
                [],
                []),
            new BlueprintTrustAssignment(trust)).Value;
        var directory = Relative(id.Contains("ordered", StringComparison.Ordinal)
            ? id + "-v" + version[0]
            : id);
        var checksum = $"sha256:{new string(checksumCharacter, 64)}";
        var fingerprint = BlueprintFingerprint.Create(
            source.Id,
            directory,
            trust,
            checksum).Value;
        var reference = BlueprintReference.Create(id, version).Value;
        var inspection = BlueprintInspection.Create(
            source.Id,
            directory,
            reference,
            trust,
            []).Value;
        return BlueprintPackageLoadResult.Success(
            new LoadedBlueprintPackage(manifest, [], fingerprint),
            inspection);
    }

    private static BlueprintPackageLoadResult FailedPackage(
        BlueprintPackageSource source,
        string directory,
        string code)
    {
        var issue = BlueprintInspectionIssue.Create(code, "The blueprint package is invalid.").Value;
        var inspection = BlueprintInspection.Create(
            source.Id,
            Relative(directory),
            null,
            BlueprintTrust.Quarantined,
            [issue]).Value;
        return BlueprintPackageLoadResult.Failure(inspection);
    }

    private static BlueprintMetadataRecord Metadata(
        string id,
        char checksumCharacter,
        BlueprintTrust trust,
        bool isDisabled = false,
        BlueprintSource source = BlueprintSource.Local)
    {
        return BlueprintMetadataRecord.Create(
            id,
            "1.0.0",
            source,
            trust,
            new string(checksumCharacter, 64),
            isDisabled,
            DateTimeOffset.UnixEpoch).Value;
    }

    private static WorkspaceRelativePath Relative(string value) =>
        WorkspaceRelativePath.Create(value).Value;

    private sealed class CatalogLoader : IBlueprintPackageLoader
    {
        private readonly Dictionary<string, BlueprintPackageLoadResult> _packages = new(StringComparer.Ordinal);
        private readonly TaskCompletionSource _blocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private string? _blockedDirectory;

        public void Add(
            BlueprintPackageSource source,
            string directory,
            BlueprintPackageLoadResult result) => _packages.Add(Key(source.Id, directory), result);

        public void Block(string directory) => _blockedDirectory = directory;

        public Task WaitUntilBlockedAsync() => _blocked.Task;

        public void Release() => _release.TrySetResult();

        public async Task<BlueprintPackageLoadResult> LoadAsync(
            BlueprintPackageSource source,
            WorkspaceRelativePath packageDirectory,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (StringComparer.Ordinal.Equals(_blockedDirectory, packageDirectory.Value))
            {
                _blocked.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken);
            }

            return _packages[Key(source.Id, packageDirectory.Value)];
        }

        private static string Key(string source, string directory) => source + "\0" + directory;
    }

    private sealed class CatalogWorkspace(params string[] directories) : IWorkspaceFileSystem
    {
        private ImmutableArray<WorkspaceRelativePath> _directories = [.. directories.Select(Relative)];

        public Exception? Failure { get; set; }

        public WorkspaceRoot Root { get; } = WorkspaceRoot.Create("C:\\catalog-fixture").Value;

        public void SetDirectories(params string[] values) =>
            _directories = [.. values.Select(Relative)];

        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateDirectoriesAsync(
            WorkspaceRelativePath directory,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateRootDirectoriesAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure is not null)
            {
                return Task.FromException<ImmutableArray<WorkspaceRelativePath>>(Failure);
            }

            return Task.FromResult(_directories);
        }

        public Task<bool> FileExistsAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> DirectoryExistsAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task CreateDirectoryAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Stream> OpenReadAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Stream> OpenWriteAsync(
            WorkspaceRelativePath path,
            bool overwrite,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DeleteFileAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateAllFilesAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateFilesAsync(
            WorkspaceRelativePath directory,
            bool recursive,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DeleteDirectoryAsync(
            WorkspaceRelativePath path,
            DirectoryCleanupIntent intent,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task MoveDirectoryAsync(
            WorkspaceRelativePath source,
            WorkspaceRelativePath destination,
            WorkspaceMoveIntent intent,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class CatalogMetadataStore(params BlueprintMetadataRecord[] records)
        : IBlueprintMetadataStore
    {
        public Exception? Failure { get; set; }

        public int WriteCount { get; private set; }

        public Task<ImmutableArray<BlueprintMetadataRecord>> ListAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Failure is null
                ? Task.FromResult<ImmutableArray<BlueprintMetadataRecord>>([.. records])
                : Task.FromException<ImmutableArray<BlueprintMetadataRecord>>(Failure);
        }

        public Task<BlueprintMetadataRecord?> GetAsync(
            string id,
            string version,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task UpsertAsync(BlueprintMetadataRecord blueprint, CancellationToken cancellationToken)
        {
            WriteCount++;
            throw new InvalidOperationException("Catalog discovery must be read-only.");
        }

        public Task<bool> RemoveAsync(
            string id,
            string version,
            CancellationToken cancellationToken)
        {
            WriteCount++;
            throw new InvalidOperationException("Catalog discovery must be read-only.");
        }
    }
}
