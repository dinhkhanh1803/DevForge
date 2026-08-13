using System.Collections.Immutable;
using DevForge.Application.Contracts;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Desktop.BlueprintCatalog;
using DevForge.Domain.Validation;

namespace DevForge.E2ETests.Desktop;

public sealed class BlueprintCatalogViewModelTests
{
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
        var sut = new BlueprintCatalogViewModel(new CatalogWorkflow(
            BlueprintCatalogSnapshot.Create([executable], [inspection]).Value));

        await sut.LoadAsync(CancellationToken.None);

        Assert.Equal(["alpha.local", "zeta.local"], sut.Items.Select(item => item.Id));
        Assert.False(sut.Items[0].CanCreate);
        Assert.Equal("Package is disabled.", sut.Items[0].Issue);
        Assert.True(sut.Items[1].CanCreate);
        Assert.Equal("TrustedLocal", sut.Items[1].TrustLabel);
    }

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
