using System.Collections.Immutable;
using System.IO;
using DevForge.Application.Contracts;
using DevForge.Application.Contracts.Persistence;
using DevForge.Blueprints.BuiltIn;
using DevForge.Infrastructure.Blueprints;
using DevForge.Infrastructure.FileSystem;

namespace DevForge.BlueprintTests.Production;

internal sealed class ProductionBlueprintCatalogFixture : IDisposable
{
    private ProductionBlueprintCatalogFixture(
        BlueprintPackageSource source,
        BlueprintCatalog catalog)
    {
        Source = source;
        Catalog = catalog;
    }

    public BlueprintPackageSource Source { get; }

    public BlueprintCatalog Catalog { get; }

    public static Task<ProductionBlueprintCatalogFixture> CreateAsync() =>
        CreateAtAsync(BuiltInBlueprintCatalog.OutputDirectory);

    public static Task<ProductionBlueprintCatalogFixture> CreateCandidatesAsync() =>
        CreateAtAsync(Path.Combine("blueprints", "candidates"));

    internal static async Task<ProductionBlueprintCatalogFixture> CreateAtAsync(string directory)
    {
        var root = WorkspaceRoot.Create(Path.Combine(
            AppContext.BaseDirectory,
            directory));
        Assert.True(root.IsValid);
        var workspace = await new WindowsFileSystem().OpenWorkspaceAsync(
            root.Value,
            CancellationToken.None);
        var source = BlueprintPackageSource.Create(
            BuiltInBlueprintCatalog.SourceId,
            workspace,
            BlueprintSourceProvenance.BuiltIn);
        Assert.True(source.IsValid);
        var catalog = new BlueprintCatalog([source.Value], new EmptyBlueprintMetadataStore());
        return new ProductionBlueprintCatalogFixture(source.Value, catalog);
    }

    public void Dispose()
    {
        Catalog.Dispose();
    }

    private sealed class EmptyBlueprintMetadataStore : IBlueprintMetadataStore
    {
        public Task<BlueprintMetadataRecord?> GetAsync(
            string id,
            string version,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<BlueprintMetadataRecord?>(null);
        }

        public Task<ImmutableArray<BlueprintMetadataRecord>> ListAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ImmutableArray<BlueprintMetadataRecord>.Empty);
        }

        public Task UpsertAsync(
            BlueprintMetadataRecord blueprint,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> RemoveAsync(
            string id,
            string version,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
