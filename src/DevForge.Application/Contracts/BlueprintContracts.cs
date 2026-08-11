using System.Collections.Immutable;
using System.Globalization;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Blueprints.Abstractions.Validation;
using DevForge.Domain.Privacy;
using DevForge.Domain.Validation;

namespace DevForge.Application.Contracts;

public sealed record BlueprintReference
{
    private BlueprintReference(string id, string version)
    {
        Id = id;
        Version = version;
    }

    public string Id { get; }

    public string Version { get; }

    public static ValidationResult<BlueprintReference> Create(string? id, string? version)
    {
        var issues = new List<ValidationIssue>();
        if (string.IsNullOrWhiteSpace(id))
        {
            issues.Add(
                new ValidationIssue(
                    "blueprint.reference.id.required",
                    "A blueprint identifier is required.",
                    "id"));
        }
        else if (!BlueprintIdentifierValidator.IsValid(id))
        {
            issues.Add(
                new ValidationIssue(
                    "blueprint.reference.id.invalid",
                    "A canonical blueprint identifier is required.",
                    "id"));
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            issues.Add(
                new ValidationIssue(
                    "blueprint.reference.version.required",
                    "A blueprint version is required.",
                    "version"));
        }
        else if (!SemanticVersion.TryParse(version, out _))
        {
            issues.Add(
                new ValidationIssue(
                    "blueprint.reference.version.invalid",
                    "An exact semantic blueprint version is required.",
                    "version"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new BlueprintReference(id!.Trim(), version!.Trim()))
            : ValidationResult.Failure<BlueprintReference>(issues);
    }
}

public interface IBlueprintCatalog
{
    Task RefreshAsync(CancellationToken cancellationToken);

    Task<BlueprintCatalogSnapshot> InspectAsync(CancellationToken cancellationToken);

    Task<ImmutableArray<ResolvedBlueprint>> ListAsync(CancellationToken cancellationToken);

    Task<ResolvedBlueprint?> FindAsync(
        BlueprintReference reference,
        CancellationToken cancellationToken);
}

public enum BlueprintSourceProvenance
{
    BuiltIn = 1,
    Local = 2,
}

public sealed class BlueprintPackageSource
{
    private BlueprintPackageSource(
        string id,
        IWorkspaceFileSystem workspace,
        BlueprintSourceProvenance provenance)
    {
        Id = id;
        Workspace = workspace;
        Provenance = provenance;
    }

    public string Id { get; }

    public IWorkspaceFileSystem Workspace { get; }

    public BlueprintSourceProvenance Provenance { get; }

    public static ValidationResult<BlueprintPackageSource> Create(
        string? id,
        IWorkspaceFileSystem? workspace,
        BlueprintSourceProvenance provenance)
    {
        var issues = new List<ValidationIssue>();
        AddRequired(issues, id, "blueprint.source.id.required", "A blueprint source identifier is required.", "id");
        if (workspace is null)
        {
            issues.Add(new ValidationIssue(
                "blueprint.source.workspace.required",
                "A guarded blueprint source workspace is required.",
                "workspace"));
        }

        if (!Enum.IsDefined(provenance))
        {
            issues.Add(new ValidationIssue(
                "blueprint.source.provenance.invalid",
                "The blueprint source provenance is not defined.",
                "provenance"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new BlueprintPackageSource(id!.Trim(), workspace!, provenance))
            : ValidationResult.Failure<BlueprintPackageSource>(issues);
    }

    private static void AddRequired(
        List<ValidationIssue> issues,
        string? value,
        string code,
        string message,
        string location)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(new ValidationIssue(code, message, location));
        }
    }
}

public sealed record BlueprintFingerprint
{
    private const string ChecksumPrefix = "sha256:";

    private BlueprintFingerprint(
        string sourceId,
        WorkspaceRelativePath packageDirectory,
        BlueprintTrust trust,
        string aggregateChecksum)
    {
        SourceId = sourceId;
        PackageDirectory = packageDirectory;
        Trust = trust;
        AggregateChecksum = aggregateChecksum;
    }

    public string SourceId { get; }

    public WorkspaceRelativePath PackageDirectory { get; }

    public BlueprintTrust Trust { get; }

    public string AggregateChecksum { get; }

    public static ValidationResult<BlueprintFingerprint> Create(
        string? sourceId,
        WorkspaceRelativePath? packageDirectory,
        BlueprintTrust trust,
        string? aggregateChecksum)
    {
        var issues = new List<ValidationIssue>();
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            issues.Add(new ValidationIssue(
                "blueprint.fingerprint.source.required",
                "A blueprint fingerprint source is required.",
                "sourceId"));
        }

        if (packageDirectory is null)
        {
            issues.Add(new ValidationIssue(
                "blueprint.fingerprint.package.required",
                "A blueprint package-relative directory is required.",
                "packageDirectory"));
        }

        if (!Enum.IsDefined(trust))
        {
            issues.Add(new ValidationIssue(
                "blueprint.fingerprint.trust.invalid",
                "The blueprint fingerprint trust state is not defined.",
                "trust"));
        }

        if (!IsChecksum(aggregateChecksum))
        {
            issues.Add(new ValidationIssue(
                "blueprint.fingerprint.checksum.invalid",
                "A lowercase SHA-256 aggregate checksum is required.",
                "aggregateChecksum"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new BlueprintFingerprint(
                sourceId!.Trim(),
                packageDirectory!,
                trust,
                aggregateChecksum!))
            : ValidationResult.Failure<BlueprintFingerprint>(issues);
    }

    private static bool IsChecksum(string? value)
    {
        return value is not null
            && value.Length == ChecksumPrefix.Length + 64
            && value.StartsWith(ChecksumPrefix, StringComparison.Ordinal)
            && value.AsSpan(ChecksumPrefix.Length).ToArray().All(character =>
                character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }
}

public sealed class ResolvedBlueprint
{
    private ResolvedBlueprint(
        BlueprintManifest manifest,
        ImmutableArray<BlueprintInputPropertyDefinition> inputSchema,
        BlueprintFingerprint fingerprint)
    {
        Manifest = manifest;
        InputSchema = inputSchema;
        Fingerprint = fingerprint;
    }

    public BlueprintManifest Manifest { get; }

    public ImmutableArray<BlueprintInputPropertyDefinition> InputSchema { get; }

    public BlueprintFingerprint Fingerprint { get; }

    public static ValidationResult<ResolvedBlueprint> Create(
        BlueprintManifest? manifest,
        IEnumerable<BlueprintInputPropertyDefinition?>? inputSchema,
        BlueprintFingerprint? fingerprint)
    {
        var schemaSnapshot = inputSchema?.ToImmutableArray() ?? [];
        var issues = new List<ValidationIssue>();
        if (manifest is null)
        {
            issues.Add(new ValidationIssue(
                "blueprint.resolved.manifest.required",
                "A normalized blueprint manifest is required.",
                "manifest"));
        }

        if (inputSchema is null)
        {
            issues.Add(new ValidationIssue(
                "blueprint.resolved.schema.required",
                "A normalized blueprint input schema is required.",
                "inputSchema"));
        }
        else
        {
            var identifiers = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < schemaSnapshot.Length; index++)
            {
                var property = schemaSnapshot[index];
                if (property is null)
                {
                    issues.Add(new ValidationIssue(
                        "blueprint.resolved.schema.item.required",
                        "A normalized blueprint input property is required.",
                        $"inputSchema[{index}]"));
                }
                else if (!identifiers.Add(property.Id))
                {
                    issues.Add(new ValidationIssue(
                        "blueprint.resolved.schema.id.duplicate",
                        "Resolved blueprint input identifiers must be unique.",
                        $"inputSchema[{index}].id"));
                }
            }

            if (manifest is not null
                && schemaSnapshot.All(item => item is not null)
                && !SchemaMatchesManifest(
                    manifest.Inputs,
                    [.. schemaSnapshot.Select(item => item!)]))
            {
                issues.Add(new ValidationIssue(
                    "blueprint.resolved.schema.mismatch",
                    "The resolved input schema must match the normalized manifest inputs.",
                    "inputSchema"));
            }
        }

        if (fingerprint is null)
        {
            issues.Add(new ValidationIssue(
                "blueprint.resolved.fingerprint.required",
                "A blueprint fingerprint is required.",
                "fingerprint"));
        }
        else if (manifest is not null && manifest.Trust != fingerprint.Trust)
        {
            issues.Add(new ValidationIssue(
                "blueprint.resolved.trust.mismatch",
                "Blueprint manifest and fingerprint trust must match.",
                "fingerprint.trust"));
        }

        if (fingerprint is not null
            && fingerprint.Trust is not (BlueprintTrust.BuiltIn or BlueprintTrust.TrustedLocal))
        {
            issues.Add(new ValidationIssue(
                "blueprint.resolved.trust.not-executable",
                "Only built-in or trusted-local blueprints can be resolved for execution.",
                "fingerprint.trust"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new ResolvedBlueprint(
                manifest!,
                [.. schemaSnapshot.Select(item => item!)],
                fingerprint!))
            : ValidationResult.Failure<ResolvedBlueprint>(issues);
    }

    private static bool SchemaMatchesManifest(
        ImmutableArray<InputDefinition> inputs,
        ImmutableArray<BlueprintInputPropertyDefinition> schema)
    {
        if (inputs.Length != schema.Length)
        {
            return false;
        }

        var properties = new Dictionary<string, BlueprintInputPropertyDefinition>(StringComparer.Ordinal);
        foreach (var property in schema)
        {
            if (!properties.TryAdd(property.Id, property))
            {
                return false;
            }
        }

        return inputs.All(input =>
            properties.TryGetValue(input.Id, out var property)
            && property.Kind == input.Kind
            && property.Required == input.Required
            && StringComparer.Ordinal.Equals(input.DefaultValue, FormatDefault(property.DefaultValue)));
    }

    private static string? FormatDefault(BlueprintValue? value)
    {
        return value?.Kind switch
        {
            null => null,
            BlueprintValueKind.Text => value.StringValue,
            BlueprintValueKind.Boolean => value.BooleanValue ? "true" : "false",
            BlueprintValueKind.WholeNumber => value.IntegerValue.ToString(CultureInfo.InvariantCulture),
            _ => null,
        };
    }
}

public sealed record BlueprintInspectionIssue
{
    private BlueprintInspectionIssue(string code, string summary)
    {
        Code = code;
        Summary = summary;
    }

    public string Code { get; }

    public string Summary { get; }

    public static ValidationResult<BlueprintInspectionIssue> Create(string? code, string? summary)
    {
        var issues = new List<ValidationIssue>();
        if (string.IsNullOrWhiteSpace(code))
        {
            issues.Add(new ValidationIssue(
                "blueprint.inspection-issue.code.required",
                "A blueprint inspection issue code is required.",
                "code"));
        }

        var safeSummary = RedactedText.FromTrustedRedaction(summary);
        if (!safeSummary.IsValid)
        {
            issues.Add(new ValidationIssue(
                "blueprint.inspection-issue.summary.invalid",
                "A safe blueprint inspection summary is required.",
                "summary"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new BlueprintInspectionIssue(code!.Trim(), safeSummary.Value.Value))
            : ValidationResult.Failure<BlueprintInspectionIssue>(issues);
    }
}

public sealed class BlueprintInspection
{
    private BlueprintInspection(
        string sourceId,
        WorkspaceRelativePath packageDirectory,
        BlueprintReference? reference,
        BlueprintTrust trust,
        bool isDisabled,
        ImmutableArray<BlueprintInspectionIssue> issues)
    {
        SourceId = sourceId;
        PackageDirectory = packageDirectory;
        Reference = reference;
        Trust = trust;
        IsDisabled = isDisabled;
        Issues = issues;
    }

    public string SourceId { get; }

    public WorkspaceRelativePath PackageDirectory { get; }

    public BlueprintReference? Reference { get; }

    public BlueprintTrust Trust { get; }

    public bool IsDisabled { get; }

    public ImmutableArray<BlueprintInspectionIssue> Issues { get; }

    public static ValidationResult<BlueprintInspection> Create(
        string? sourceId,
        WorkspaceRelativePath? packageDirectory,
        BlueprintReference? reference,
        BlueprintTrust trust,
        IEnumerable<BlueprintInspectionIssue?>? issues,
        bool isDisabled = false)
    {
        var snapshot = issues?.ToImmutableArray() ?? [];
        var validationIssues = new List<ValidationIssue>();
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            validationIssues.Add(new ValidationIssue(
                "blueprint.inspection.source.required",
                "A blueprint inspection source is required.",
                "sourceId"));
        }

        if (packageDirectory is null)
        {
            validationIssues.Add(new ValidationIssue(
                "blueprint.inspection.package.required",
                "A blueprint inspection package directory is required.",
                "packageDirectory"));
        }

        if (!Enum.IsDefined(trust))
        {
            validationIssues.Add(new ValidationIssue(
                "blueprint.inspection.trust.invalid",
                "The blueprint inspection trust state is not defined.",
                "trust"));
        }

        AddCollectionIssues(issues, snapshot, "blueprint.inspection.issues", validationIssues);
        return validationIssues.Count == 0
            ? ValidationResult.Success(new BlueprintInspection(
                sourceId!.Trim(),
                packageDirectory!,
                reference,
                trust,
                isDisabled,
                [.. snapshot.Select(issue => issue!)]))
            : ValidationResult.Failure<BlueprintInspection>(validationIssues);
    }

    private static void AddCollectionIssues<T>(
        IEnumerable<T?>? source,
        ImmutableArray<T?> snapshot,
        string location,
        List<ValidationIssue> issues)
        where T : class
    {
        if (source is null)
        {
            issues.Add(new ValidationIssue(
                $"{location}.required",
                "A blueprint inspection collection is required.",
                location));
        }

        for (var index = 0; index < snapshot.Length; index++)
        {
            if (snapshot[index] is null)
            {
                issues.Add(new ValidationIssue(
                    $"{location}.item.required",
                    "A blueprint inspection collection item is required.",
                    $"{location}[{index}]"));
            }
        }
    }
}

public sealed class BlueprintCatalogSnapshot
{
    private BlueprintCatalogSnapshot(
        ImmutableArray<ResolvedBlueprint> executableBlueprints,
        ImmutableArray<BlueprintInspection> inspections)
    {
        ExecutableBlueprints = executableBlueprints;
        Inspections = inspections;
    }

    public ImmutableArray<ResolvedBlueprint> ExecutableBlueprints { get; }

    public ImmutableArray<BlueprintInspection> Inspections { get; }

    public static ValidationResult<BlueprintCatalogSnapshot> Create(
        IEnumerable<ResolvedBlueprint?>? executableBlueprints,
        IEnumerable<BlueprintInspection?>? inspections)
    {
        var executableSnapshot = executableBlueprints?.ToImmutableArray() ?? [];
        var inspectionSnapshot = inspections?.ToImmutableArray() ?? [];
        var issues = new List<ValidationIssue>();
        AddNullCollectionIssues(executableBlueprints, executableSnapshot, "executableBlueprints", issues);
        AddNullCollectionIssues(inspections, inspectionSnapshot, "inspections", issues);

        return issues.Count == 0
            ? ValidationResult.Success(new BlueprintCatalogSnapshot(
                [.. executableSnapshot.Select(item => item!)],
                [.. inspectionSnapshot.Select(item => item!)]))
            : ValidationResult.Failure<BlueprintCatalogSnapshot>(issues);
    }

    private static void AddNullCollectionIssues<T>(
        IEnumerable<T?>? source,
        ImmutableArray<T?> snapshot,
        string location,
        List<ValidationIssue> issues)
        where T : class
    {
        if (source is null)
        {
            issues.Add(new ValidationIssue(
                $"blueprint.catalog.{location}.required",
                "A blueprint catalog collection is required.",
                location));
        }

        for (var index = 0; index < snapshot.Length; index++)
        {
            if (snapshot[index] is null)
            {
                issues.Add(new ValidationIssue(
                    $"blueprint.catalog.{location}.item.required",
                    "A blueprint catalog collection item is required.",
                    $"{location}[{index}]"));
            }
        }
    }
}
