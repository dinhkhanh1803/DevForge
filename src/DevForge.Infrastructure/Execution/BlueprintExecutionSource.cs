using System.Collections.Immutable;
using DevForge.Application.Contracts;
using DevForge.Application.Contracts.Persistence;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Diagnostics;
using DevForge.Domain.Privacy;
using DevForge.Infrastructure.Blueprints;
using DevForge.Infrastructure.Persistence;

namespace DevForge.Infrastructure.Execution;

public sealed class BlueprintExecutionSource : IBlueprintExecutionSource
{
    private readonly ImmutableArray<BlueprintPackageSource> _sources;
    private readonly IBlueprintMetadataStore _metadataStore;
    private readonly IBlueprintPackageLoader _loader;

    public BlueprintExecutionSource(
        IEnumerable<BlueprintPackageSource> sources,
        IBlueprintMetadataStore metadataStore)
        : this(sources, metadataStore, new BlueprintPackageLoader())
    {
    }

    internal BlueprintExecutionSource(
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
            throw new ArgumentException("Blueprint execution sources must be non-null and unique.", nameof(sources));
        }
    }

    public async Task<ExecutionOperationResult<BlueprintExecutionPackage>> OpenAsync(
        BlueprintReference blueprint,
        BlueprintFingerprint fingerprint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        ArgumentNullException.ThrowIfNull(fingerprint);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var source = _sources.SingleOrDefault(candidate =>
                StringComparer.Ordinal.Equals(candidate.Id, fingerprint.SourceId));
            if (source is null || !IsProvenanceCompatible(source.Provenance, fingerprint.Trust))
            {
                return Failure();
            }

            var loaded = await _loader.LoadAsync(
                source,
                fingerprint.PackageDirectory,
                cancellationToken).ConfigureAwait(false);
            if (!loaded.IsValid
                || loaded.Package is null
                || !MatchesFingerprint(loaded.Package.Fingerprint, fingerprint)
                || !StringComparer.Ordinal.Equals(loaded.Package.Manifest.Id, blueprint.Id)
                || !StringComparer.Ordinal.Equals(loaded.Package.Manifest.Version, blueprint.Version)
                || !await IsTrustCurrentAsync(
                    blueprint,
                    fingerprint,
                    source.Provenance,
                    cancellationToken).ConfigureAwait(false))
            {
                return Failure();
            }

            var assigned = AssignTrust(loaded.Package, fingerprint.Trust);
            var resolved = ResolvedBlueprint.Create(
                assigned.Manifest,
                assigned.InputSchema,
                assigned.Fingerprint);
            if (!resolved.IsValid || !resolved.Value.Fingerprint.Equals(fingerprint))
            {
                return Failure();
            }

            var snapshot = await BlueprintChecksumVerifier.VerifyForExecutionAsync(
                source.Workspace,
                fingerprint.PackageDirectory,
                cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!snapshot.IsValid
                || !StringComparer.Ordinal.Equals(snapshot.AggregateChecksum, fingerprint.AggregateChecksum)
                || snapshot.DeclaredHashes.Count != snapshot.VerifiedFiles.Count)
            {
                return Failure();
            }

            var workspace = VerifiedBlueprintWorkspace.Create(
                snapshot.AggregateChecksum!,
                snapshot.VerifiedFiles,
                cancellationToken);
            if (!await IsTrustCurrentAsync(
                    blueprint,
                    fingerprint,
                    source.Provenance,
                    cancellationToken).ConfigureAwait(false))
            {
                return Failure();
            }

            var package = BlueprintExecutionPackage.Create(resolved.Value, workspace);
            return package.IsValid
                ? ExecutionOperationResult.Success(package.Value)
                : Failure();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InfrastructureOperationException
            or PersistenceDataException
            or IOException
            or InvalidDataException
            or ArgumentException
            or OverflowException
            or FormatException)
        {
            return Failure();
        }
    }

    private async Task<bool> IsTrustCurrentAsync(
        BlueprintReference blueprint,
        BlueprintFingerprint fingerprint,
        BlueprintSourceProvenance provenance,
        CancellationToken cancellationToken)
    {
        var metadata = await _metadataStore.GetAsync(
            blueprint.Id,
            blueprint.Version,
            cancellationToken).ConfigureAwait(false);
        if (fingerprint.Trust == BlueprintTrust.BuiltIn)
        {
            return provenance == BlueprintSourceProvenance.BuiltIn
                && metadata is not
                {
                    Source: BlueprintSource.BuiltIn,
                    IsDisabled: true,
                };
        }

        if (fingerprint.Trust != BlueprintTrust.TrustedLocal
            || provenance != BlueprintSourceProvenance.Local)
        {
            return false;
        }

        return metadata is
        {
            Source: BlueprintSource.Local,
            Trust: BlueprintTrust.TrustedLocal,
            IsDisabled: false,
        }
            && StringComparer.Ordinal.Equals(
                metadata.Checksum,
                fingerprint.AggregateChecksum["sha256:".Length..]);
    }

    private static bool IsProvenanceCompatible(
        BlueprintSourceProvenance provenance,
        BlueprintTrust trust)
    {
        return provenance == BlueprintSourceProvenance.BuiltIn && trust == BlueprintTrust.BuiltIn
            || provenance == BlueprintSourceProvenance.Local && trust == BlueprintTrust.TrustedLocal;
    }

    private static bool MatchesFingerprint(
        BlueprintFingerprint loaded,
        BlueprintFingerprint expected)
    {
        return StringComparer.Ordinal.Equals(loaded.SourceId, expected.SourceId)
            && loaded.PackageDirectory.Equals(expected.PackageDirectory)
            && StringComparer.Ordinal.Equals(loaded.AggregateChecksum, expected.AggregateChecksum);
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
            throw new InvalidDataException("Blueprint trust reconciliation was inconsistent.");
        }

        return new LoadedBlueprintPackage(manifest.Value, package.InputSchema, fingerprint.Value);
    }

    private static ExecutionOperationResult<BlueprintExecutionPackage> Failure()
    {
        var detail = RedactedText.FromTrustedRedaction(
            "The source, package, identity, checksum, or current trust assignment did not match the planned fingerprint.");
        var error = DevForgeError.Create(
            "DF-EXEC-003",
            "The planned blueprint content could not be reopened exactly.",
            detail.Value,
            "blueprint-execution",
            null,
            isRetryable: true,
            ["Restore the exact trusted blueprint package used during planning."],
            []);
        return ExecutionOperationResult.Failure<BlueprintExecutionPackage>(error.Value);
    }
}
