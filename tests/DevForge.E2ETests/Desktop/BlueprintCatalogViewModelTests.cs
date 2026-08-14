using System.Collections.Immutable;
using DevForge.Application.Contracts;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Desktop.BlueprintCatalog;
using DevForge.Desktop.Navigation;
using DevForge.Domain.Validation;

namespace DevForge.E2ETests.Desktop;

public sealed class BlueprintCatalogViewModelTests
{
    [Fact]
    public void SafeReadOnlyModeDisablesCatalogRefresh()
    {
        var snapshot = BlueprintCatalogSnapshot.Create([], []).Value;
        var sut = CreateViewModel(snapshot);

        sut.EnterReadOnlyMode();

        Assert.True(sut.IsReadOnly);
        Assert.False(sut.RefreshCommand.CanExecute(null));
    }

    [Fact]
    public async Task CatalogSeparatesExecutableAndInspectOnlyInDeterministicOrder()
    {
        var executable = CreateResolved("zeta.local", BlueprintTrust.TrustedLocal);
        var issue = BlueprintInspectionIssue.Create("blueprint.disabled", "Package is disabled.").Value;
        var inspection = BlueprintInspection.Create(
            "local",
            WorkspaceRelativePath.Create("alpha.local\\1.0.0").Value,
            BlueprintReference.Create("alpha.local", "1.0.0").Value,
            BlueprintTrust.Untrusted,
            [issue],
            isDisabled: true).Value;
        var sut = CreateViewModel(
            BlueprintCatalogSnapshot.Create([executable], [inspection]).Value);

        await sut.LoadAsync(CancellationToken.None);

        Assert.Equal(["alpha.local", "zeta.local"], sut.Items.Select(item => item.Id));
        Assert.False(sut.Items[0].CanCreate);
        Assert.Equal("Package is disabled.", sut.Items[0].Issue);
        Assert.True(sut.Items[1].CanCreate);
        Assert.Equal("TrustedLocal", sut.Items[1].TrustLabel);
    }

    [Fact]
    public async Task CreateActionCarriesExactBlueprintSelectionToCreateRoute()
    {
        var blueprint = CreateResolved("sample.local", BlueprintTrust.TrustedLocal);
        var navigation = new NavigationService();
        var selection = new ProjectCreationSelection();
        var sut = new BlueprintCatalogViewModel(
            new CatalogWorkflow(BlueprintCatalogSnapshot.Create([blueprint], []).Value),
            navigation,
            selection);
        await sut.LoadAsync(CancellationToken.None);

        sut.CreateCommand.Execute(sut.Items.Single());

        Assert.Equal(DesktopRoute.CreateProject, navigation.CurrentRoute);
        Assert.Equal("sample.local", selection.Blueprint?.Id);
        Assert.Equal("1.0.0", selection.Blueprint?.Version);
    }

    [Theory]
    [InlineData(BlueprintTrust.BuiltIn, true)]
    [InlineData(BlueprintTrust.TrustedLocal, true)]
    [InlineData(BlueprintTrust.Untrusted, false)]
    [InlineData(BlueprintTrust.Quarantined, false)]
    public async Task TrustMatrixEnablesOnlyExecutableCatalogEntries(
        BlueprintTrust trust,
        bool expectedCanCreate)
    {
        BlueprintCatalogSnapshot snapshot;
        if (expectedCanCreate)
        {
            snapshot = BlueprintCatalogSnapshot.Create([CreateResolved("sample.local", trust)], []).Value;
        }
        else
        {
            var issue = BlueprintInspectionIssue.Create(
                "blueprint.trust.refused",
                "The package trust does not permit execution.").Value;
            var inspection = BlueprintInspection.Create(
                "local",
                WorkspaceRelativePath.Create("sample.local\\1.0.0").Value,
                BlueprintReference.Create("sample.local", "1.0.0").Value,
                trust,
                [issue],
                isDisabled: true).Value;
            snapshot = BlueprintCatalogSnapshot.Create([], [inspection]).Value;
        }

        var sut = CreateViewModel(snapshot);
        await sut.LoadAsync(CancellationToken.None);

        var item = Assert.Single(sut.Items);
        Assert.Equal(expectedCanCreate, item.CanCreate);
        Assert.Equal(trust.ToString(), item.TrustLabel);
        Assert.Equal(expectedCanCreate, sut.CreateCommand.CanExecute(item));
    }

    private static BlueprintCatalogViewModel CreateViewModel(BlueprintCatalogSnapshot snapshot) =>
        new(new CatalogWorkflow(snapshot), new NavigationService(), new ProjectCreationSelection());

    private static ResolvedBlueprint CreateResolved(string id, BlueprintTrust trust)
    {
        var manifest = BlueprintManifest.Create(
            new BlueprintManifestDraft(
                id, "1.0.0", ">=1.0.0 <2.0.0", [], [], [],
                [new BlueprintStepDefinition("create", "create-directory", TimeSpan.FromSeconds(30))],
                []),
            new BlueprintTrustAssignment(trust)).Value;
        var fingerprint = BlueprintFingerprint.Create(
            "local", WorkspaceRelativePath.Create($"{id}\\1.0.0").Value,
            trust, $"sha256:{new string('2', 64)}").Value;
        return ResolvedBlueprint.Create(manifest, [], fingerprint).Value;
    }

    private sealed class CatalogWorkflow(BlueprintCatalogSnapshot snapshot) : IProjectCreationWorkflow
    {
        public Task<BlueprintCatalogSnapshot> LoadCatalogAsync(bool forceRefresh, CancellationToken cancellationToken) => Task.FromResult(snapshot);
        public Task<ValidationResult<ProjectCreationPlanSnapshot>> CreatePlanAsync(ProjectCreationDraft draft, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ValidationResult<ProjectCreationExecutionSnapshot>> ExecuteAsync(ProjectCreationPlanSnapshot plan, IProgress<ExecutionProgressLine>? progress, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
