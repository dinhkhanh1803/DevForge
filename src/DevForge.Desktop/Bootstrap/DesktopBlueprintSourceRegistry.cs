using System.Collections;
using System.Collections.Immutable;
using System.IO;
using DevForge.Application.Contracts;
using DevForge.Application.Contracts.Persistence;

namespace DevForge.Desktop.Bootstrap;

public sealed class DesktopBlueprintSourceRegistry(
    DatabaseLocation location,
    IFileSystem fileSystem) : IEnumerable<BlueprintPackageSource>
{
    private readonly DatabaseLocation _location = location ?? throw new ArgumentNullException(nameof(location));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private ImmutableArray<BlueprintPackageSource> _sources = [];

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var applicationRoot = WorkspaceRoot.Create(_location.LocalDataRoot);
        var sourceRoot = WorkspaceRoot.Create(Path.Combine(
            _location.LocalDataRoot,
            "blueprints",
            "local"));
        var blueprintsDirectory = WorkspaceRelativePath.Create("blueprints");
        var localDirectory = WorkspaceRelativePath.Create("blueprints\\local");
        if (!applicationRoot.IsValid
            || !sourceRoot.IsValid
            || !blueprintsDirectory.IsValid
            || !localDirectory.IsValid)
        {
            throw new InvalidOperationException("The trusted-local blueprint source root is invalid.");
        }

        var applicationWorkspace = await _fileSystem.OpenWorkspaceAsync(
            applicationRoot.Value,
            cancellationToken).ConfigureAwait(false);
        await applicationWorkspace.CreateDirectoryAsync(
            blueprintsDirectory.Value,
            cancellationToken).ConfigureAwait(false);
        await applicationWorkspace.CreateDirectoryAsync(
            localDirectory.Value,
            cancellationToken).ConfigureAwait(false);
        var sourceWorkspace = await _fileSystem.OpenWorkspaceAsync(
            sourceRoot.Value,
            cancellationToken).ConfigureAwait(false);
        var source = BlueprintPackageSource.Create(
            "trusted-local",
            sourceWorkspace,
            BlueprintSourceProvenance.Local);
        if (!source.IsValid)
        {
            throw new InvalidOperationException("The trusted-local blueprint source is invalid.");
        }

        _sources = [source.Value];
    }

    public IEnumerator<BlueprintPackageSource> GetEnumerator() =>
        ((IEnumerable<BlueprintPackageSource>)_sources).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
