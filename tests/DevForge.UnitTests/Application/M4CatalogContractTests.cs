using System.Collections.Immutable;
using DevForge.Application.Contracts;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Execution;
using DevForge.Domain.Validation;

namespace DevForge.UnitTests.Application;

public sealed class M4CatalogContractTests
{
    [Fact]
    public void CatalogExposesOnlyTheExactM4AsyncSurface()
    {
        var methods = typeof(IBlueprintCatalog).GetMethods().OrderBy(method => method.Name).ToArray();

        Assert.Equal(
            ["FindAsync", "InspectAsync", "ListAsync", "RefreshAsync"],
            methods.Select(method => method.Name));
        Assert.Equal(typeof(Task<ResolvedBlueprint?>), methods[0].ReturnType);
        Assert.Equal(typeof(Task<BlueprintCatalogSnapshot>), methods[1].ReturnType);
        Assert.Equal(typeof(Task<ImmutableArray<ResolvedBlueprint>>), methods[2].ReturnType);
        Assert.Equal(typeof(Task), methods[3].ReturnType);
        Assert.All(methods, method => Assert.Equal(typeof(CancellationToken), method.GetParameters()[^1].ParameterType));
    }

    [Fact]
    public void PlannerReturnsAPlannedProjectInsteadOfAnExecutionPlan()
    {
        var method = Assert.Single(typeof(IProjectPlanner).GetMethods());

        Assert.Equal(
            typeof(Task<ValidationResult<PlannedProject>>),
            method.ReturnType);
    }

    [Fact]
    public void PackageSourceRequiresGuardedWorkspaceAndExternalProvenance()
    {
        var workspace = new StubWorkspaceFileSystem();
        var result = BlueprintPackageSource.Create(
            " built-in ",
            workspace,
            BlueprintSourceProvenance.BuiltIn);

        Assert.True(result.IsValid);
        Assert.Equal("built-in", result.Value.Id);
        Assert.Same(workspace, result.Value.Workspace);
        Assert.Equal(BlueprintSourceProvenance.BuiltIn, result.Value.Provenance);
        Assert.DoesNotContain(
            typeof(BlueprintPackageSource).GetProperties(),
            property => property.PropertyType == typeof(string)
                && property.Name.Contains("Path", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FingerprintIsOpaqueRelativeAndRejectsMalformedChecksums()
    {
        var packageDirectory = Relative("desktop.csharp-wpf-tool\\1.0.0");
        var valid = BlueprintFingerprint.Create(
            "built-in",
            packageDirectory,
            BlueprintTrust.BuiltIn,
            $"sha256:{new string('a', 64)}");
        var invalid = BlueprintFingerprint.Create(
            "built-in",
            packageDirectory,
            BlueprintTrust.BuiltIn,
            "sha256:not-a-hash");

        Assert.True(valid.IsValid);
        Assert.Equal(packageDirectory, valid.Value.PackageDirectory);
        Assert.Equal("blueprint.fingerprint.checksum.invalid", Assert.Single(invalid.Issues).Code);
        Assert.DoesNotContain(
            typeof(BlueprintFingerprint).GetProperties(),
            property => property.Name.Contains("Absolute", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Invalid Id", "1.0.0")]
    [InlineData("desktop.csharp-wpf-tool", "latest")]
    [InlineData("desktop.csharp-wpf-tool", "1.0")]
    public void BlueprintReferenceRequiresCanonicalIdAndExactSemanticVersion(
        string id,
        string version)
    {
        var result = BlueprintReference.Create(id, version);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData(BlueprintTrust.Untrusted)]
    [InlineData(BlueprintTrust.Quarantined)]
    public void ResolvedBlueprintRejectsNonExecutableTrustStates(BlueprintTrust trust)
    {
        var fingerprint = BlueprintFingerprint.Create(
            "local",
            Relative("desktop.csharp-wpf-tool\\1.0.0"),
            trust,
            $"sha256:{new string('d', 64)}").Value;

        var result = ResolvedBlueprint.Create(ValidManifest(trust), fingerprint);

        Assert.Equal("blueprint.resolved.trust.not-executable", Assert.Single(result.Issues).Code);
    }

    [Fact]
    public void CatalogSnapshotTakesImmutableOrderedBoundarySnapshots()
    {
        var fingerprint = BlueprintFingerprint.Create(
            "built-in",
            Relative("desktop.csharp-wpf-tool\\1.0.0"),
            BlueprintTrust.BuiltIn,
            $"sha256:{new string('b', 64)}").Value;
        var resolved = ResolvedBlueprint.Create(ValidManifest(), fingerprint).Value;
        var inspectionIssue = BlueprintInspectionIssue.Create(
            "DF-BP-001",
            "The package is malformed.").Value;
        var inspection = BlueprintInspection.Create(
            "local",
            Relative("invalid-package"),
            null,
            BlueprintTrust.Quarantined,
            [inspectionIssue]).Value;
        var executable = new List<ResolvedBlueprint?> { resolved };
        var inspections = new List<BlueprintInspection?> { inspection };

        var result = BlueprintCatalogSnapshot.Create(executable, inspections);
        Assert.True(result.IsValid);
        executable.Clear();
        inspections.Clear();

        Assert.Same(resolved, Assert.Single(result.Value.ExecutableBlueprints));
        Assert.Same(inspection, Assert.Single(result.Value.Inspections));
    }

    [Fact]
    public void PlannedProjectSnapshotsAValidatedPreviewAlongsideThePlan()
    {
        var step = ExecutionStep.Create(
            "render",
            "Render",
            "render-template",
            [],
            TimeSpan.FromMinutes(1),
            RetryPolicy.None).Value;
        var plan = ExecutionPlan.Create("plan", [step]).Value;
        var reference = BlueprintReference.Create("desktop.csharp-wpf-tool", "1.0.0").Value;
        var previewSteps = new List<PlanPreviewStep?>
        {
            new("render", "render-template", TimeSpan.FromMinutes(1)),
        };
        var preview = PlanPreview.Create(
            reference,
            previewSteps,
            [new ToolRequirement("dotnet", ">=10.0.0 <11.0.0")],
            [new BlueprintDependency("microsoft.extensions.hosting", "10.0.0")],
            [new BlueprintArtifact("src/App.csproj")],
            [],
            $"sha256:{new string('c', 64)}");
        Assert.True(preview.IsValid);
        previewSteps.Clear();

        var planned = PlannedProject.Create(plan, preview.Value);
        Assert.True(planned.IsValid);
        Assert.Same(plan, planned.Value.Plan);
        Assert.Single(planned.Value.Preview.Steps);
    }

    private static BlueprintManifest ValidManifest(BlueprintTrust trust = BlueprintTrust.BuiltIn)
    {
        return BlueprintManifest.Create(
            new BlueprintManifestDraft(
                "desktop.csharp-wpf-tool",
                "1.0.0",
                ">=1.0.0 <2.0.0",
                [new ToolRequirement("dotnet", ">=10.0.0 <11.0.0")],
                [new InputDefinition("framework", BlueprintInputKind.Text, true, "net10.0")],
                [new CompatibilityRule("runtime.os == 'windows'", "Windows is required.")],
                [new BlueprintStepDefinition("render", "render-template", TimeSpan.FromMinutes(1))],
                [new ValidatorDefinition("build", "validate-command", TimeSpan.FromMinutes(5))]),
            new BlueprintTrustAssignment(trust)).Value;
    }

    private static WorkspaceRelativePath Relative(string value)
    {
        return WorkspaceRelativePath.Create(value).Value;
    }

    private sealed class StubWorkspaceFileSystem : IWorkspaceFileSystem
    {
        public WorkspaceRoot Root { get; } = WorkspaceRoot.Create("C:\\catalog").Value;

        public Task<bool> FileExistsAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<bool> DirectoryExistsAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task CreateDirectoryAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<Stream> OpenReadAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Stream> OpenWriteAsync(
            WorkspaceRelativePath path,
            bool overwrite,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DeleteFileAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateAllFilesAsync(
            CancellationToken cancellationToken) => Task.FromResult(ImmutableArray<WorkspaceRelativePath>.Empty);

        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateFilesAsync(
            WorkspaceRelativePath directory,
            bool recursive,
            CancellationToken cancellationToken) => Task.FromResult(ImmutableArray<WorkspaceRelativePath>.Empty);

        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateDirectoriesAsync(
            WorkspaceRelativePath directory,
            CancellationToken cancellationToken) => Task.FromResult(ImmutableArray<WorkspaceRelativePath>.Empty);

        public Task DeleteDirectoryAsync(
            WorkspaceRelativePath path,
            DirectoryCleanupIntent intent,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task MoveDirectoryAsync(
            WorkspaceRelativePath source,
            WorkspaceRelativePath destination,
            WorkspaceMoveIntent intent,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
