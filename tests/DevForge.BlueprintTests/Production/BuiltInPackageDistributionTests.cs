using DevForge.Application.Contracts;
using DevForge.Blueprints.BuiltIn;

namespace DevForge.BlueprintTests.Production;

public sealed class BuiltInPackageDistributionTests
{
    [Fact]
    public async Task BuildOutputProvidesGuardedBuiltInCatalogSource()
    {
        using var fixture = await ProductionBlueprintCatalogFixture.CreateAsync();

        var files = await fixture.Source.Workspace.EnumerateAllFilesAsync(CancellationToken.None);

        Assert.Equal(BuiltInBlueprintCatalog.SourceId, fixture.Source.Id);
        Assert.Equal(BlueprintSourceProvenance.BuiltIn, fixture.Source.Provenance);
        Assert.Contains(files, path => path.Value == "README.md");
    }
}
