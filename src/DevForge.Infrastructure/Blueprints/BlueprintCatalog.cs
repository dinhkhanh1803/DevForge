using System.Collections.Immutable;
using DevForge.Application.Contracts;
using DevForge.Application.Contracts.Persistence;
using DevForge.Blueprints.Abstractions.Models;

namespace DevForge.Infrastructure.Blueprints;

public sealed class BlueprintCatalog : IBlueprintCatalog, IDisposable
{
    internal const int MaximumPackagesPerSource = 256;

    private readonly ImmutableArray<BlueprintPackageSource> _sources;
    private readonly IBlueprintMetadataStore _metadataStore;
    private readonly IBlueprintPackageLoader _loader;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private BlueprintCatalogSnapshot _snapshot = BlueprintCatalogSnapshot.Create([], []).Value;

    public BlueprintCatalog(
        IEnumerable<BlueprintPackageSource> sources,
        IBlueprintMetadataStore metadataStore)
        : this(sources, metadataStore, new BlueprintPackageLoader())
    {
    }

    internal BlueprintCatalog(
        IEnumerable<BlueprintPackageSource> sources,
        IBlueprintMetadataStore metadataStore,
        IBlueprintPackageLoader loader)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _metadataStore = metadataStore ?? throw new ArgumentNullException(nameof(metadataStore));
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _sources = [.. sources];
        if (_sources.Any(source => source is null)
            || _sources.Select(source => source.Id).Distinct(StringComparer.Ordinal).Count() != _sources.Length)
        {
            throw new ArgumentException("Blueprint catalog sources must be non-null and unique.", nameof(sources));
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var loaded = await LoadAllPackagesAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var metadata = await _metadataStore.ListAsync(cancellationToken).ConfigureAwait(false);
            var next = BuildSnapshot(loaded, metadata, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Volatile.Write(ref _snapshot, next);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public Task<BlueprintCatalogSnapshot> InspectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Volatile.Read(ref _snapshot));
    }

    public Task<ImmutableArray<ResolvedBlueprint>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Volatile.Read(ref _snapshot).ExecutableBlueprints);
    }

    public Task<ResolvedBlueprint?> FindAsync(
        BlueprintReference reference,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reference);
        cancellationToken.ThrowIfCancellationRequested();
        var match = Volatile.Read(ref _snapshot).ExecutableBlueprints.FirstOrDefault(item =>
            StringComparer.Ordinal.Equals(item.Manifest.Id, reference.Id)
            && StringComparer.Ordinal.Equals(item.Manifest.Version, reference.Version));
        return Task.FromResult(match);
    }

    public void Dispose()
    {
        _refreshLock.Dispose();
    }

    private async Task<ImmutableArray<LoadedEntry>> LoadAllPackagesAsync(
        CancellationToken cancellationToken)
    {
        var entries = ImmutableArray.CreateBuilder<LoadedEntry>();
        foreach (var source in _sources.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directories = await source.Workspace
                .EnumerateRootDirectoriesAsync(cancellationToken)
                .ConfigureAwait(false);
            if (directories.Length > MaximumPackagesPerSource)
            {
                throw new InfrastructureOperationException(
                    "DF-BP-004",
                    "A blueprint source exceeds the supported package bound.");
            }

            foreach (var directory in directories.OrderBy(item => item.Value, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await _loader.LoadAsync(source, directory, cancellationToken).ConfigureAwait(false);
                entries.Add(new LoadedEntry(source, result));
            }
        }

        return entries.ToImmutable();
    }

    private static BlueprintCatalogSnapshot BuildSnapshot(
        ImmutableArray<LoadedEntry> loadedEntries,
        ImmutableArray<BlueprintMetadataRecord> metadataRecords,
        CancellationToken cancellationToken)
    {
        var metadata = CreateMetadataMap(metadataRecords);
        var validCandidates = new List<CatalogCandidate>();
        var inspections = new List<BlueprintInspection>();
        foreach (var entry in loadedEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!entry.Result.IsValid)
            {
                inspections.Add(entry.Result.Inspection);
                continue;
            }

            var candidate = Reconcile(entry.Source, entry.Result.Package!, metadata);
            validCandidates.Add(candidate);
        }

        var executable = new List<ResolvedBlueprint>();
        foreach (var group in validCandidates.GroupBy(
                     item => IdentityKey(item.Package.Manifest.Id, item.Package.Manifest.Version),
                     StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidates = group.ToArray();
            if (candidates.Length > 1)
            {
                inspections.AddRange(candidates.Select(CreateConflictInspection));
                continue;
            }

            var candidate = candidates[0];
            inspections.Add(candidate.Inspection);
            if (candidate.IsDisabled
                || candidate.Package.Manifest.Trust is not (BlueprintTrust.BuiltIn or BlueprintTrust.TrustedLocal))
            {
                continue;
            }

            var resolved = ResolvedBlueprint.Create(
                candidate.Package.Manifest,
                candidate.Package.InputSchema,
                candidate.Package.Fingerprint);
            if (!resolved.IsValid)
            {
                throw new InvalidDataException("The reconciled executable blueprint is inconsistent.");
            }

            executable.Add(resolved.Value);
        }

        var orderedExecutable = executable
            .OrderBy(item => item.Manifest.Id, StringComparer.Ordinal)
            .ThenByDescending(item => ParseVersion(item.Manifest.Version))
            .ThenBy(item => item.Fingerprint.SourceId, StringComparer.Ordinal)
            .ToImmutableArray();
        var orderedInspections = inspections
            .OrderBy(item => item.Reference?.Id ?? string.Empty, StringComparer.Ordinal)
            .ThenByDescending(item => item.Reference is null
                ? ParseVersion("0.0.0")
                : ParseVersion(item.Reference.Version))
            .ThenBy(item => item.SourceId, StringComparer.Ordinal)
            .ThenBy(item => item.PackageDirectory.Value, StringComparer.Ordinal)
            .ToImmutableArray();
        var snapshot = BlueprintCatalogSnapshot.Create(orderedExecutable, orderedInspections);
        return snapshot.IsValid
            ? snapshot.Value
            : throw new InvalidDataException("The blueprint catalog snapshot is inconsistent.");
    }

    private static ImmutableDictionary<string, BlueprintMetadataRecord> CreateMetadataMap(
        ImmutableArray<BlueprintMetadataRecord> records)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, BlueprintMetadataRecord>(
            StringComparer.Ordinal);
        foreach (var record in records)
        {
            if (record is null || !builder.TryAdd(IdentityKey(record.Id, record.Version), record))
            {
                throw new InvalidDataException("Blueprint metadata contains an ambiguous identity.");
            }
        }

        return builder.ToImmutable();
    }

    private static CatalogCandidate Reconcile(
        BlueprintPackageSource source,
        LoadedBlueprintPackage package,
        ImmutableDictionary<string, BlueprintMetadataRecord> metadata)
    {
        metadata.TryGetValue(
            IdentityKey(package.Manifest.Id, package.Manifest.Version),
            out var persisted);
        var expectedSource = source.Provenance == BlueprintSourceProvenance.BuiltIn
            ? BlueprintSource.BuiltIn
            : BlueprintSource.Local;
        var matchingSource = persisted?.Source == expectedSource;
        var isDisabled = matchingSource && persisted!.IsDisabled;
        var trust = source.Provenance == BlueprintSourceProvenance.BuiltIn
            ? BlueprintTrust.BuiltIn
            : matchingSource
                && persisted!.Trust == BlueprintTrust.TrustedLocal
                && StringComparer.Ordinal.Equals(
                    persisted.Checksum,
                    package.Fingerprint.AggregateChecksum["sha256:".Length..])
                    ? BlueprintTrust.TrustedLocal
                    : BlueprintTrust.Untrusted;
        var reconciled = AssignTrust(package, trust);
        var reference = BlueprintReference.Create(
            reconciled.Manifest.Id,
            reconciled.Manifest.Version).Value;
        var inspection = BlueprintInspection.Create(
            source.Id,
            reconciled.Fingerprint.PackageDirectory,
            reference,
            trust,
            [],
            isDisabled).Value;
        return new CatalogCandidate(reconciled, inspection, isDisabled);
    }

    private static LoadedBlueprintPackage AssignTrust(
        LoadedBlueprintPackage package,
        BlueprintTrust trust)
    {
        if (package.Manifest.Trust == trust && package.Fingerprint.Trust == trust)
        {
            return package;
        }

        var draft = new BlueprintManifestDraft(
            package.Manifest.Id,
            package.Manifest.Version,
            package.Manifest.EngineVersionRange,
            package.Manifest.Tools,
            package.Manifest.Inputs,
            package.Manifest.CompatibilityRules,
            package.Manifest.Steps,
            package.Manifest.Validators,
            package.Manifest.Name,
            package.Manifest.Features,
            package.Manifest.Actions,
            package.Manifest.Dependencies,
            package.Manifest.Artifacts);
        var manifest = BlueprintManifest.Create(draft, new BlueprintTrustAssignment(trust));
        var fingerprint = BlueprintFingerprint.Create(
            package.Fingerprint.SourceId,
            package.Fingerprint.PackageDirectory,
            trust,
            package.Fingerprint.AggregateChecksum);
        if (!manifest.IsValid || !fingerprint.IsValid)
        {
            throw new InvalidDataException("Blueprint trust reconciliation produced an invalid package.");
        }

        return new LoadedBlueprintPackage(manifest.Value, package.InputSchema, fingerprint.Value);
    }

    private static BlueprintInspection CreateConflictInspection(CatalogCandidate candidate)
    {
        var issue = BlueprintInspectionIssue.Create(
            "DF-BP-005",
            "The blueprint identity conflicts with another discovered package.").Value;
        return BlueprintInspection.Create(
            candidate.Inspection.SourceId,
            candidate.Inspection.PackageDirectory,
            candidate.Inspection.Reference,
            BlueprintTrust.Quarantined,
            [issue],
            candidate.IsDisabled).Value;
    }

    private static SemanticVersion ParseVersion(string value)
    {
        return SemanticVersion.TryParse(value, out var version)
            ? version
            : throw new InvalidDataException("A normalized blueprint version is invalid.");
    }

    private static string IdentityKey(string id, string version) => id + "\0" + version;

    private sealed record LoadedEntry(
        BlueprintPackageSource Source,
        BlueprintPackageLoadResult Result);

    private sealed record CatalogCandidate(
        LoadedBlueprintPackage Package,
        BlueprintInspection Inspection,
        bool IsDisabled);
}
