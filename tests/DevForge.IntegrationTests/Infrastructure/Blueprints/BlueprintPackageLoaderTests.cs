using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DevForge.Application.Contracts;
using DevForge.Application.Contracts.Persistence;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Infrastructure.Blueprints;
using DevForge.Infrastructure.FileSystem;

namespace DevForge.IntegrationTests.Infrastructure.Blueprints;

public sealed class BlueprintPackageLoaderTests
{
    [Theory]
    [InlineData(BlueprintSourceProvenance.BuiltIn, BlueprintTrust.BuiltIn)]
    [InlineData(BlueprintSourceProvenance.Local, BlueprintTrust.Untrusted)]
    public async Task LoadNormalizesVerifiedPackageAndAssignsTrustFromProvenance(
        BlueprintSourceProvenance provenance,
        BlueprintTrust expectedTrust)
    {
        await using var fixture = await PackageFixture.CreateAsync(provenance);
        var packageDirectory = await fixture.WriteValidPackageAsync();

        var result = await new BlueprintPackageLoader().LoadAsync(
            fixture.Source,
            packageDirectory,
            CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Package);
        Assert.Equal("sample.blueprint", result.Package.Manifest.Id);
        Assert.Equal("1.2.3", result.Package.Manifest.Version);
        Assert.Equal(expectedTrust, result.Package.Manifest.Trust);
        Assert.Equal(expectedTrust, result.Package.Fingerprint.Trust);
        Assert.StartsWith("sha256:", result.Package.Fingerprint.AggregateChecksum, StringComparison.Ordinal);
        Assert.Equal("project-name", Assert.Single(result.Package.InputSchema).Id);
        Assert.Equal("create-directory", Assert.Single(result.Package.Manifest.Actions).HandlerId);
        Assert.Empty(result.Inspection.Issues);
    }

    [Fact]
    public async Task LoadVerifiesChecksumBeforeParsingPackageControlledContent()
    {
        await using var fixture = await PackageFixture.CreateAsync(BlueprintSourceProvenance.BuiltIn);
        var packageDirectory = await fixture.WriteValidPackageAsync(
            manifest: "malformed: [",
            corruptManifestChecksum: true);

        var result = await new BlueprintPackageLoader().LoadAsync(
            fixture.Source,
            packageDirectory,
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Null(result.Package);
        Assert.Equal("DF-BP-002", Assert.Single(result.Inspection.Issues).Code);
    }

    [Fact]
    public async Task LoadParsesTheExactControlBytesThatWereChecksumVerified()
    {
        await using var fixture = await PackageFixture.CreateAsync(BlueprintSourceProvenance.Local);
        var packageDirectory = await fixture.WriteValidPackageAsync();
        var changingWorkspace = new ChangingManifestWorkspace(fixture.Workspace);
        var source = BlueprintPackageSource.Create(
            "changing-source",
            changingWorkspace,
            BlueprintSourceProvenance.Local).Value;

        var result = await new BlueprintPackageLoader().LoadAsync(
            source,
            packageDirectory,
            CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal(1, changingWorkspace.ManifestOpenCount);
    }

    [Fact]
    public async Task LoadRejectsMissingMandatoryControlFile()
    {
        await using var fixture = await PackageFixture.CreateAsync(BlueprintSourceProvenance.BuiltIn);
        var packageDirectory = await fixture.WriteValidPackageAsync(includeRules: false);

        var result = await new BlueprintPackageLoader().LoadAsync(
            fixture.Source,
            packageDirectory,
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal("DF-BP-001", Assert.Single(result.Inspection.Issues).Code);
    }

    [Fact]
    public async Task LoadRejectsPackageDirectoryThatDoesNotMatchManifestIdentity()
    {
        await using var fixture = await PackageFixture.CreateAsync(BlueprintSourceProvenance.BuiltIn);
        var packageDirectory = await fixture.WriteValidPackageAsync(directoryName: "different.blueprint");

        var result = await new BlueprintPackageLoader().LoadAsync(
            fixture.Source,
            packageDirectory,
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal("DF-BP-001", Assert.Single(result.Inspection.Issues).Code);
    }

    [Fact]
    public async Task LoadQuarantinesUnsafeActionWithoutExposingPackageContent()
    {
        await using var fixture = await PackageFixture.CreateAsync(BlueprintSourceProvenance.BuiltIn);
        var packageDirectory = await fixture.WriteValidPackageAsync(actionPath: ".env");

        var result = await new BlueprintPackageLoader().LoadAsync(
            fixture.Source,
            packageDirectory,
            CancellationToken.None);

        Assert.False(result.IsValid);
        var issue = Assert.Single(result.Inspection.Issues);
        Assert.Equal("DF-BP-003", issue.Code);
        Assert.DoesNotContain(".env", issue.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(fixture.RootPath, issue.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("templates/generation-report.json")]
    [InlineData("overlays/base/devforge.lock.json")]
    [InlineData("overlays/base/.devforge/project.recipe.yaml")]
    [InlineData("templates/policy.snapshot.json")]
    public async Task LoadAllowsHarmlessUnusedPackageFilesWithEvidenceBasenames(string packagePath)
    {
        await using var fixture = await PackageFixture.CreateAsync(BlueprintSourceProvenance.BuiltIn);
        var packageDirectory = await fixture.WriteValidPackageAsync(extraPackageFile: packagePath);

        var result = await new BlueprintPackageLoader().LoadAsync(
            fixture.Source,
            packageDirectory,
            CancellationToken.None);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(".devforge/project.recipe.yaml")]
    [InlineData("devforge.lock.json")]
    [InlineData("generation-report.json")]
    [InlineData("policy.snapshot.json")]
    public async Task LoadRejectsManifestArtifactThatClaimsEngineOwnedEvidence(string artifactPath)
    {
        await using var fixture = await PackageFixture.CreateAsync(BlueprintSourceProvenance.BuiltIn);
        var packageDirectory = await fixture.WriteValidPackageAsync(artifactPath: artifactPath);

        var result = await new BlueprintPackageLoader().LoadAsync(
            fixture.Source,
            packageDirectory,
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal("DF-BP-003", Assert.Single(result.Inspection.Issues).Code);
    }

    [Fact]
    public async Task PublicCatalogRefreshesARealGuardedPackageThroughTheProductionLoader()
    {
        await using var fixture = await PackageFixture.CreateAsync(BlueprintSourceProvenance.BuiltIn);
        await fixture.WriteValidPackageAsync();
        using var catalog = new BlueprintCatalog([fixture.Source], new EmptyMetadataStore());

        await catalog.RefreshAsync(CancellationToken.None);

        var resolved = Assert.Single(await catalog.ListAsync(CancellationToken.None));
        Assert.Equal("sample.blueprint", resolved.Manifest.Id);
        Assert.Equal("project-name", Assert.Single(resolved.InputSchema).Id);
    }

    [Theory]
    [InlineData("environment.PATH == \"value\"", "Compatibility requirement was not satisfied.")]
    [InlineData("runtime.os == \"windows\"", "Bearer abcdefghijklmnop")]
    public async Task LoadQuarantinesUnsupportedOrUnsafeCompatibilityRules(
        string condition,
        string message)
    {
        await using var fixture = await PackageFixture.CreateAsync(BlueprintSourceProvenance.BuiltIn);
        var rules = $"""
            - id: guarded-rule
              condition: {condition}
              severity: blocking
              message: {message}
              remediation: Choose a compatible option.
              override: none
            """;
        var packageDirectory = await fixture.WriteValidPackageAsync(rules: rules);

        var result = await new BlueprintPackageLoader().LoadAsync(
            fixture.Source,
            packageDirectory,
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal("DF-BP-001", Assert.Single(result.Inspection.Issues).Code);
    }

    private sealed class PackageFixture : IAsyncDisposable
    {
        private const string DefaultManifest = """
            id: sample.blueprint
            name: Sample Blueprint
            version: 1.2.3
            engineVersion: ">=1.0.0 <2.0.0"
            tools:
              - id: dotnet
                version: ">=10.0.0 <11.0.0"
                required: true
            features:
              - id: api
                defaultEnabled: true
            actions:
              - id: create-source
                handler: create-directory
                timeoutSeconds: 30
                parameters:
                  path: __ACTION_PATH__
            validators: []
            artifacts:
              - path: __ARTIFACT_PATH__
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

        private PackageFixture(
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

        public static async Task<PackageFixture> CreateAsync(BlueprintSourceProvenance provenance)
        {
            var rootPath = Path.Combine(
                Path.GetTempPath(),
                "DevForge-M4-Package-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootPath);
            var workspace = await new WindowsFileSystem().OpenWorkspaceAsync(
                WorkspaceRoot.Create(rootPath).Value,
                CancellationToken.None);
            var source = BlueprintPackageSource.Create("fixture", workspace, provenance).Value;
            return new PackageFixture(rootPath, workspace, source);
        }

        public async Task<WorkspaceRelativePath> WriteValidPackageAsync(
            string directoryName = "sample.blueprint",
            string? manifest = null,
            string actionPath = "src",
            bool includeRules = true,
            bool corruptManifestChecksum = false,
            string? rules = null,
            string? extraPackageFile = null,
            string artifactPath = "src")
        {
            var package = Relative(directoryName);
            await Workspace.CreateDirectoryAsync(package, CancellationToken.None);
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["manifest.yaml"] = Encoding.UTF8.GetBytes(
                    (manifest ?? DefaultManifest)
                        .Replace("__ACTION_PATH__", actionPath, StringComparison.Ordinal)
                        .Replace("__ARTIFACT_PATH__", artifactPath, StringComparison.Ordinal)),
                ["inputs.schema.json"] = Encoding.UTF8.GetBytes(InputSchema),
                ["templates/app.txt"] = Encoding.UTF8.GetBytes("{{ project.name }}"),
            };
            if (includeRules)
            {
                files["rules.yaml"] = Encoding.UTF8.GetBytes(rules ?? Rules);
            }

            if (extraPackageFile is not null)
            {
                files[extraPackageFile] = "forged"u8.ToArray();
            }

            foreach (var file in files)
            {
                await WriteAsync(Relative(directoryName + "\\" + file.Key.Replace('/', '\\')), file.Value);
            }

            var checksums = files.ToDictionary(
                item => item.Key,
                item => Convert.ToHexString(SHA256.HashData(item.Value)).ToLowerInvariant(),
                StringComparer.Ordinal);
            if (corruptManifestChecksum)
            {
                checksums["manifest.yaml"] = new string('0', 64);
            }

            await WriteAsync(
                Relative(directoryName + "\\checksums.json"),
                JsonSerializer.SerializeToUtf8Bytes(checksums));
            return package;
        }

        private async Task WriteAsync(WorkspaceRelativePath path, byte[] content)
        {
            var separator = path.Value.LastIndexOf('\\');
            if (separator > 0)
            {
                var directory = Relative(path.Value[..separator]);
                if (!await Workspace.DirectoryExistsAsync(directory, CancellationToken.None))
                {
                    await Workspace.CreateDirectoryAsync(directory, CancellationToken.None);
                }
            }

            await using var stream = await Workspace.OpenWriteAsync(
                path,
                overwrite: false,
                CancellationToken.None);
            await stream.WriteAsync(content);
        }

        public ValueTask DisposeAsync()
        {
            var fullPath = Path.GetFullPath(RootPath);
            if (!fullPath.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(fullPath).StartsWith("DevForge-M4-Package-", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Refusing to clean an unexpected package fixture path.");
            }

            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, recursive: true);
            }

            return ValueTask.CompletedTask;
        }

        private static WorkspaceRelativePath Relative(string value)
        {
            return WorkspaceRelativePath.Create(value).Value;
        }
    }

    private sealed class ChangingManifestWorkspace(IWorkspaceFileSystem inner)
        : IWorkspaceFileSystem
    {
        private int _manifestOpenCount;

        public int ManifestOpenCount => Volatile.Read(ref _manifestOpenCount);

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
            if (path.Value.EndsWith("\\manifest.yaml", StringComparison.OrdinalIgnoreCase)
                && Interlocked.Increment(ref _manifestOpenCount) > 1)
            {
                return new MemoryStream("malformed: ["u8.ToArray(), writable: false);
            }

            return await inner.OpenReadAsync(path, cancellationToken);
        }

        public Task<Stream> OpenWriteAsync(
            WorkspaceRelativePath path,
            bool overwrite,
            CancellationToken cancellationToken) => inner.OpenWriteAsync(path, overwrite, cancellationToken);

        public Task DeleteFileAsync(
            WorkspaceRelativePath path,
            CancellationToken cancellationToken) => inner.DeleteFileAsync(path, cancellationToken);

        public Task<System.Collections.Immutable.ImmutableArray<WorkspaceRelativePath>> EnumerateAllFilesAsync(
            CancellationToken cancellationToken) => inner.EnumerateAllFilesAsync(cancellationToken);

        public Task<System.Collections.Immutable.ImmutableArray<WorkspaceRelativePath>> EnumerateRootDirectoriesAsync(
            CancellationToken cancellationToken) => inner.EnumerateRootDirectoriesAsync(cancellationToken);

        public Task<System.Collections.Immutable.ImmutableArray<WorkspaceRelativePath>> EnumerateFilesAsync(
            WorkspaceRelativePath directory,
            bool recursive,
            CancellationToken cancellationToken) => inner.EnumerateFilesAsync(directory, recursive, cancellationToken);

        public Task<System.Collections.Immutable.ImmutableArray<WorkspaceRelativePath>> EnumerateDirectoriesAsync(
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

    private sealed class EmptyMetadataStore : IBlueprintMetadataStore
    {
        public Task<System.Collections.Immutable.ImmutableArray<BlueprintMetadataRecord>> ListAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(System.Collections.Immutable.ImmutableArray<BlueprintMetadataRecord>.Empty);
        }

        public Task<BlueprintMetadataRecord?> GetAsync(
            string id,
            string version,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task UpsertAsync(BlueprintMetadataRecord blueprint, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> RemoveAsync(
            string id,
            string version,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
