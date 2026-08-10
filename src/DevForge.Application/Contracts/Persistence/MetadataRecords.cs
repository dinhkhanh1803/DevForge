using System.Text.RegularExpressions;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Validation;

namespace DevForge.Application.Contracts.Persistence;

public enum IdeKind
{
    VisualStudioCode = 1,
    VisualStudio = 2,
    Rider = 3,
    Unity = 4,
}

public enum InstallationValidationState
{
    Valid = 1,
    Missing = 2,
    Invalid = 3,
}

public enum EnvironmentToolStatus
{
    Installed = 1,
    Compatible = 2,
    Missing = 3,
    Outdated = 4,
    Conflicting = 5,
    Unknown = 6,
}

public enum BlueprintSource
{
    BuiltIn = 1,
    Local = 2,
}

public sealed class IdeInstallationRecord
{
    private IdeInstallationRecord(
        string id,
        IdeKind kind,
        string executablePath,
        string? version,
        InstallationValidationState validationState,
        DateTimeOffset scannedAt)
    {
        Id = id;
        Kind = kind;
        ExecutablePath = executablePath;
        Version = version;
        ValidationState = validationState;
        ScannedAt = scannedAt;
    }

    public string Id { get; }

    public IdeKind Kind { get; }

    public string ExecutablePath { get; }

    public string? Version { get; }

    public InstallationValidationState ValidationState { get; }

    public DateTimeOffset ScannedAt { get; }

    public static ValidationResult<IdeInstallationRecord> Create(
        string? id,
        IdeKind kind,
        string? executablePath,
        string? version,
        InstallationValidationState validationState,
        DateTimeOffset scannedAt)
    {
        var issues = new List<ValidationIssue>();
        var normalizedId = MetadataRules.NormalizeIdentifier(id, "persistence.ide.id.invalid", "id", issues);
        var normalizedPath = MetadataRules.NormalizeLocalPath(
            executablePath,
            "persistence.ide.path.invalid",
            "executablePath",
            issues);
        var normalizedVersion = MetadataRules.NormalizeOptionalText(
            version,
            64,
            "persistence.ide.version.invalid",
            "version",
            issues);
        MetadataRules.AddEnumIssue(kind, "persistence.ide.kind.invalid", "kind", issues);
        MetadataRules.AddEnumIssue(
            validationState,
            "persistence.ide.validation-state.invalid",
            "validationState",
            issues);

        return issues.Count == 0
            ? ValidationResult.Success(
                new IdeInstallationRecord(
                    normalizedId!,
                    kind,
                    normalizedPath!,
                    normalizedVersion,
                    validationState,
                    scannedAt))
            : ValidationResult.Failure<IdeInstallationRecord>(issues);
    }
}

public sealed class EnvironmentToolRecord
{
    private EnvironmentToolRecord(
        string id,
        string? executablePath,
        string? version,
        EnvironmentToolStatus status,
        DateTimeOffset scannedAt,
        DateTimeOffset expiresAt)
    {
        Id = id;
        ExecutablePath = executablePath;
        Version = version;
        Status = status;
        ScannedAt = scannedAt;
        ExpiresAt = expiresAt;
    }

    public string Id { get; }

    public string? ExecutablePath { get; }

    public string? Version { get; }

    public EnvironmentToolStatus Status { get; }

    public DateTimeOffset ScannedAt { get; }

    public DateTimeOffset ExpiresAt { get; }

    public static ValidationResult<EnvironmentToolRecord> Create(
        string? id,
        string? executablePath,
        string? version,
        EnvironmentToolStatus status,
        DateTimeOffset scannedAt,
        DateTimeOffset expiresAt)
    {
        var issues = new List<ValidationIssue>();
        var normalizedId = MetadataRules.NormalizeIdentifier(id, "persistence.tool.id.invalid", "id", issues);
        string? normalizedPath = null;
        if (executablePath is not null)
        {
            normalizedPath = MetadataRules.NormalizeLocalPath(
                executablePath,
                "persistence.tool.path.invalid",
                "executablePath",
                issues);
        }

        var normalizedVersion = MetadataRules.NormalizeOptionalText(
            version,
            64,
            "persistence.tool.version.invalid",
            "version",
            issues);
        MetadataRules.AddEnumIssue(status, "persistence.tool.status.invalid", "status", issues);
        if (expiresAt < scannedAt)
        {
            issues.Add(
                new ValidationIssue(
                    "persistence.tool.expiry.invalid",
                    "Tool cache expiry cannot precede its scan time.",
                    "expiresAt"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(
                new EnvironmentToolRecord(
                    normalizedId!,
                    normalizedPath,
                    normalizedVersion,
                    status,
                    scannedAt,
                    expiresAt))
            : ValidationResult.Failure<EnvironmentToolRecord>(issues);
    }
}

public sealed class BlueprintMetadataRecord
{
    private static readonly Regex _semanticVersionPattern = new(
        "^[0-9]+\\.[0-9]+\\.[0-9]+(?:-[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?(?:\\+[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?$",
        RegexOptions.CultureInvariant);

    private BlueprintMetadataRecord(
        string id,
        string version,
        BlueprintSource source,
        BlueprintTrust trust,
        string checksum,
        bool isDisabled,
        DateTimeOffset discoveredAt)
    {
        Id = id;
        Version = version;
        Source = source;
        Trust = trust;
        Checksum = checksum;
        IsDisabled = isDisabled;
        DiscoveredAt = discoveredAt;
    }

    public string Id { get; }

    public string Version { get; }

    public BlueprintSource Source { get; }

    public BlueprintTrust Trust { get; }

    public string Checksum { get; }

    public bool IsDisabled { get; }

    public DateTimeOffset DiscoveredAt { get; }

    public static ValidationResult<BlueprintMetadataRecord> Create(
        string? id,
        string? version,
        BlueprintSource source,
        BlueprintTrust trust,
        string? checksum,
        bool isDisabled,
        DateTimeOffset discoveredAt)
    {
        var issues = new List<ValidationIssue>();
        var normalizedId = MetadataRules.NormalizeIdentifier(
            id,
            "persistence.blueprint.id.invalid",
            "id",
            issues);
        var normalizedVersion = version?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedVersion)
            || normalizedVersion.Length > 64
            || !_semanticVersionPattern.IsMatch(normalizedVersion))
        {
            issues.Add(
                new ValidationIssue(
                    "persistence.blueprint.version.invalid",
                    "A valid semantic blueprint version is required.",
                    "version"));
        }

        MetadataRules.AddEnumIssue(source, "persistence.blueprint.source.invalid", "source", issues);
        MetadataRules.AddEnumIssue(trust, "persistence.blueprint.trust.invalid", "trust", issues);
        if (source == BlueprintSource.BuiltIn && trust != BlueprintTrust.BuiltIn
            || source == BlueprintSource.Local && trust == BlueprintTrust.BuiltIn)
        {
            issues.Add(
                new ValidationIssue(
                    "persistence.blueprint.provenance.invalid",
                    "Blueprint source and trust provenance are inconsistent.",
                    "trust"));
        }

        var normalizedChecksum = checksum?.Trim();
        if (normalizedChecksum is null
            || normalizedChecksum.Length != 64
            || normalizedChecksum.Any(character =>
                !(char.IsAsciiDigit(character) || character is >= 'a' and <= 'f')))
        {
            issues.Add(
                new ValidationIssue(
                    "persistence.blueprint.checksum.invalid",
                    "A lowercase SHA-256 checksum is required.",
                    "checksum"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(
                new BlueprintMetadataRecord(
                    normalizedId!,
                    normalizedVersion!,
                    source,
                    trust,
                    normalizedChecksum!,
                    isDisabled,
                    discoveredAt))
            : ValidationResult.Failure<BlueprintMetadataRecord>(issues);
    }
}

public sealed class TeamProfileRecord
{
    private TeamProfileRecord(
        string id,
        string name,
        int schemaVersion,
        PersistableJson policy,
        DateTimeOffset updatedAt)
    {
        Id = id;
        Name = name;
        SchemaVersion = schemaVersion;
        Policy = policy;
        UpdatedAt = updatedAt;
    }

    public string Id { get; }

    public string Name { get; }

    public int SchemaVersion { get; }

    public PersistableJson Policy { get; }

    public DateTimeOffset UpdatedAt { get; }

    public static ValidationResult<TeamProfileRecord> Create(
        string? id,
        string? name,
        int schemaVersion,
        PersistableJson? policy,
        DateTimeOffset updatedAt)
    {
        var issues = new List<ValidationIssue>();
        var normalizedId = MetadataRules.NormalizeIdentifier(
            id,
            "persistence.team-profile.id.invalid",
            "id",
            issues);
        var normalizedName = MetadataRules.NormalizeRequiredText(
            name,
            200,
            "persistence.team-profile.name.invalid",
            "name",
            issues);
        MetadataRules.AddDocumentIssues(
            schemaVersion,
            policy,
            "persistence.team-profile",
            issues);

        return issues.Count == 0
            ? ValidationResult.Success(
                new TeamProfileRecord(
                    normalizedId!,
                    normalizedName!,
                    schemaVersion,
                    policy!,
                    updatedAt))
            : ValidationResult.Failure<TeamProfileRecord>(issues);
    }
}

public sealed class PresetRecord
{
    private PresetRecord(
        string id,
        string name,
        int schemaVersion,
        PersistableJson recipe,
        DateTimeOffset updatedAt)
    {
        Id = id;
        Name = name;
        SchemaVersion = schemaVersion;
        Recipe = recipe;
        UpdatedAt = updatedAt;
    }

    public string Id { get; }

    public string Name { get; }

    public int SchemaVersion { get; }

    public PersistableJson Recipe { get; }

    public DateTimeOffset UpdatedAt { get; }

    public static ValidationResult<PresetRecord> Create(
        string? id,
        string? name,
        int schemaVersion,
        PersistableJson? recipe,
        DateTimeOffset updatedAt)
    {
        var issues = new List<ValidationIssue>();
        var normalizedId = MetadataRules.NormalizeIdentifier(
            id,
            "persistence.preset.id.invalid",
            "id",
            issues);
        var normalizedName = MetadataRules.NormalizeRequiredText(
            name,
            200,
            "persistence.preset.name.invalid",
            "name",
            issues);
        MetadataRules.AddDocumentIssues(schemaVersion, recipe, "persistence.preset", issues);

        return issues.Count == 0
            ? ValidationResult.Success(
                new PresetRecord(
                    normalizedId!,
                    normalizedName!,
                    schemaVersion,
                    recipe!,
                    updatedAt))
            : ValidationResult.Failure<PresetRecord>(issues);
    }
}

public sealed class RecentProjectRecord
{
    private RecentProjectRecord(
        string projectPath,
        string displayName,
        string? repositoryUrl,
        string? ideId,
        DateTimeOffset lastOpenedAt)
    {
        ProjectPath = projectPath;
        DisplayName = displayName;
        RepositoryUrl = repositoryUrl;
        IdeId = ideId;
        LastOpenedAt = lastOpenedAt;
    }

    public string ProjectPath { get; }

    public string DisplayName { get; }

    public string? RepositoryUrl { get; }

    public string? IdeId { get; }

    public DateTimeOffset LastOpenedAt { get; }

    public static ValidationResult<RecentProjectRecord> Create(
        string? projectPath,
        string? displayName,
        string? repositoryUrl,
        string? ideId,
        DateTimeOffset lastOpenedAt)
    {
        var issues = new List<ValidationIssue>();
        var normalizedPath = MetadataRules.NormalizeLocalPath(
            projectPath,
            "persistence.recent.path.invalid",
            "projectPath",
            issues);
        var normalizedName = MetadataRules.NormalizeRequiredText(
            displayName,
            200,
            "persistence.recent.name.invalid",
            "displayName",
            issues);
        string? normalizedRepositoryUrl = null;
        if (repositoryUrl is not null)
        {
            if (!Uri.TryCreate(repositoryUrl.Trim(), UriKind.Absolute, out var uri)
                || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrEmpty(uri.UserInfo)
                || !string.IsNullOrEmpty(uri.Query)
                || !string.IsNullOrEmpty(uri.Fragment)
                || repositoryUrl.Length > 2_048)
            {
                issues.Add(
                    new ValidationIssue(
                        "persistence.recent.repository-url.invalid",
                        "A safe HTTPS repository URL is required.",
                        "repositoryUrl"));
            }
            else
            {
                normalizedRepositoryUrl = uri.AbsoluteUri;
            }
        }

        string? normalizedIdeId = null;
        if (ideId is not null)
        {
            normalizedIdeId = MetadataRules.NormalizeIdentifier(
                ideId,
                "persistence.recent.ide-id.invalid",
                "ideId",
                issues);
        }

        return issues.Count == 0
            ? ValidationResult.Success(
                new RecentProjectRecord(
                    normalizedPath!,
                    normalizedName!,
                    normalizedRepositoryUrl,
                    normalizedIdeId,
                    lastOpenedAt))
            : ValidationResult.Failure<RecentProjectRecord>(issues);
    }
}

internal static class MetadataRules
{
    public static string? NormalizeIdentifier(
        string? value,
        string code,
        string location,
        List<ValidationIssue> issues)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Length > 128
            || normalized.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')))
        {
            issues.Add(new ValidationIssue(code, "A bounded identifier is required.", location));
            return null;
        }

        return normalized;
    }

    public static string? NormalizeLocalPath(
        string? value,
        string code,
        string location,
        List<ValidationIssue> issues)
    {
        var normalized = LocalPersistencePathPolicy.TryNormalize(value);
        if (normalized is null)
        {
            issues.Add(new ValidationIssue(code, "A canonical local drive path is required.", location));
            return null;
        }

        return normalized;
    }

    public static string? NormalizeOptionalText(
        string? value,
        int maximumLength,
        string code,
        string location,
        List<ValidationIssue> issues)
    {
        if (value is null)
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length == 0 || normalized.Length > maximumLength || normalized.Any(char.IsControl))
        {
            issues.Add(new ValidationIssue(code, "The optional text is invalid.", location));
            return null;
        }

        return normalized;
    }

    public static string? NormalizeRequiredText(
        string? value,
        int maximumLength,
        string code,
        string location,
        List<ValidationIssue> issues)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Length > maximumLength
            || normalized.Any(char.IsControl))
        {
            issues.Add(new ValidationIssue(code, "Required text is invalid.", location));
            return null;
        }

        return normalized;
    }

    public static void AddEnumIssue<TEnum>(
        TEnum value,
        string code,
        string location,
        List<ValidationIssue> issues)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            issues.Add(new ValidationIssue(code, "The value is not defined.", location));
        }
    }

    public static void AddDocumentIssues(
        int schemaVersion,
        PersistableJson? document,
        string codePrefix,
        List<ValidationIssue> issues)
    {
        if (schemaVersion < 1)
        {
            issues.Add(
                new ValidationIssue(
                    $"{codePrefix}.schema-version.invalid",
                    "The document schema version must be positive.",
                    "schemaVersion"));
        }

        if (document is null)
        {
            issues.Add(
                new ValidationIssue(
                    $"{codePrefix}.document.required",
                    "A validated persistence document is required.",
                    "document"));
        }
    }
}
