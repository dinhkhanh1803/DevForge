using System.Collections.Immutable;
using DevForge.Domain.Privacy;
using DevForge.Domain.Validation;

namespace DevForge.Application.Contracts;

public enum DiagnosticLevel
{
    Trace = 1,
    Debug = 2,
    Information = 3,
    Warning = 4,
    Error = 5,
    Critical = 6,
}

public sealed record DiagnosticEvent
{
    private DiagnosticEvent(
        DateTimeOffset timestampUtc,
        DiagnosticLevel level,
        string eventId,
        string? runId,
        string? stepId,
        int? attempt,
        string source,
        RedactedText message,
        long? durationMs,
        string? errorCode)
    {
        TimestampUtc = timestampUtc;
        Level = level;
        EventId = eventId;
        RunId = runId;
        StepId = stepId;
        Attempt = attempt;
        Source = source;
        Message = message;
        DurationMs = durationMs;
        ErrorCode = errorCode;
    }

    public DateTimeOffset TimestampUtc { get; }

    public DiagnosticLevel Level { get; }

    public string EventId { get; }

    public string? RunId { get; }

    public string? StepId { get; }

    public int? Attempt { get; }

    public string Source { get; }

    public RedactedText Message { get; }

    public long? DurationMs { get; }

    public string? ErrorCode { get; }

    public static ValidationResult<DiagnosticEvent> Create(
        DateTimeOffset timestampUtc,
        DiagnosticLevel level,
        string? eventId,
        string? runId,
        string? stepId,
        int? attempt,
        string? source,
        RedactedText? message,
        long? durationMs,
        string? errorCode)
    {
        var issues = new List<ValidationIssue>();
        if (timestampUtc.Offset != TimeSpan.Zero)
        {
            issues.Add(Issue("diagnostics.timestamp.utc", "A UTC diagnostic timestamp is required.", "timestampUtc"));
        }

        if (!Enum.IsDefined(level))
        {
            issues.Add(Issue("diagnostics.level.invalid", "A supported diagnostic level is required.", "level"));
        }

        var normalizedEventId = NormalizeIdentifier(eventId, 128, "eventId", issues);
        var normalizedRunId = NormalizeOptionalIdentifier(runId, 128, "runId", issues);
        var normalizedStepId = NormalizeOptionalIdentifier(stepId, 128, "stepId", issues);
        var normalizedSource = NormalizeIdentifier(source, 128, "source", issues);
        var normalizedErrorCode = NormalizeOptionalIdentifier(errorCode, 64, "errorCode", issues);
        if (attempt is <= 0)
        {
            issues.Add(Issue("diagnostics.attempt.invalid", "A diagnostic attempt must be positive.", "attempt"));
        }

        if (durationMs is < 0)
        {
            issues.Add(Issue("diagnostics.duration.invalid", "A diagnostic duration cannot be negative.", "durationMs"));
        }

        if (message is null)
        {
            issues.Add(Issue("diagnostics.message.required", "A redacted diagnostic message is required.", "message"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new DiagnosticEvent(
                timestampUtc,
                level,
                normalizedEventId!,
                normalizedRunId,
                normalizedStepId,
                attempt,
                normalizedSource!,
                message!,
                durationMs,
                normalizedErrorCode))
            : ValidationResult.Failure<DiagnosticEvent>(issues);
    }

    private static string? NormalizeOptionalIdentifier(
        string? value,
        int maximumLength,
        string field,
        List<ValidationIssue> issues)
    {
        return value is null ? null : NormalizeIdentifier(value, maximumLength, field, issues);
    }

    private static string? NormalizeIdentifier(
        string? value,
        int maximumLength,
        string field,
        List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value != value.Trim()
            || value.Length > maximumLength
            || value.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_'))
            || RedactedText.IsSecretShapedKey(value)
            || RedactedText.IsSecretShapedValue(value))
        {
            issues.Add(Issue(
                "diagnostics.identifier.invalid",
                "A bounded non-secret diagnostic identifier is required.",
                field));
            return null;
        }

        return value;
    }

    private static ValidationIssue Issue(string code, string message, string field) =>
        new(code, message, field);
}

public sealed record DiagnosticRetentionPolicy
{
    public const int MinimumAgeDays = 1;
    public const int MaximumAgeDays = 365;
    public const long MinimumTotalBytes = 16L * 1024 * 1024;
    public const long MaximumTotalBytes = 2L * 1024 * 1024 * 1024;

    private DiagnosticRetentionPolicy(int maxAgeDays, long maxTotalBytes)
    {
        MaxAgeDays = maxAgeDays;
        MaxTotalBytes = maxTotalBytes;
    }

    public static DiagnosticRetentionPolicy Default { get; } =
        new(30, 256L * 1024 * 1024);

    public int MaxAgeDays { get; }

    public long MaxTotalBytes { get; }

    public static ValidationResult<DiagnosticRetentionPolicy> Create(
        int maxAgeDays,
        long maxTotalBytes)
    {
        var issues = new List<ValidationIssue>();
        if (maxAgeDays is < MinimumAgeDays or > MaximumAgeDays)
        {
            issues.Add(new ValidationIssue(
                "diagnostics.retention.age.invalid",
                "Diagnostic retention must be between 1 and 365 days.",
                "maxAgeDays"));
        }

        if (maxTotalBytes is < MinimumTotalBytes or > MaximumTotalBytes)
        {
            issues.Add(new ValidationIssue(
                "diagnostics.retention.bytes.invalid",
                "Diagnostic retention must be between 16 MiB and 2 GiB.",
                "maxTotalBytes"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new DiagnosticRetentionPolicy(maxAgeDays, maxTotalBytes))
            : ValidationResult.Failure<DiagnosticRetentionPolicy>(issues);
    }
}

public interface IDiagnosticSink
{
    Task WriteAsync(DiagnosticEvent diagnosticEvent, CancellationToken cancellationToken);
}

public interface IDiagnosticRetentionService
{
    Task<DiagnosticRetentionResult> ApplyAsync(
        DiagnosticRetentionPolicy policy,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);
}

public enum DiagnosticRetentionReason
{
    OwnershipUnverified = 1,
    LeaseUnavailable = 2,
    DeleteFailed = 3,
    Cancelled = 4,
}

public sealed record DiagnosticRetentionResult(
    int DeletedCount,
    int DeferredCount,
    int UnownedCount,
    bool WasCancelled,
    ImmutableArray<DiagnosticRetentionReason> Reasons)
{
    public static DiagnosticRetentionResult Empty { get; } =
        new(0, 0, 0, false, []);
}

public sealed record SupportBundleRequest
{
    private SupportBundleRequest(string runId, bool includeEnvironmentSnapshot)
    {
        RunId = runId;
        IncludeEnvironmentSnapshot = includeEnvironmentSnapshot;
    }

    public string RunId { get; }

    public bool IncludeEnvironmentSnapshot { get; }

    public static ValidationResult<SupportBundleRequest> Create(
        string? runId,
        bool includeEnvironmentSnapshot)
    {
        if (!IsCanonicalIdentifier(runId, 128))
        {
            return ValidationResult.Failure<SupportBundleRequest>(
                [new ValidationIssue(
                    "support-bundle.run-id.invalid",
                    "A canonical non-secret run identifier is required.",
                    "runId")]);
        }

        return ValidationResult.Success(
            new SupportBundleRequest(runId!, includeEnvironmentSnapshot));
    }

    internal static bool IsCanonicalIdentifier(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= maximumLength
        && value == value.Trim()
        && value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')
        && !RedactedText.IsSecretShapedKey(value)
        && !RedactedText.IsSecretShapedValue(value);
}

public sealed record SupportBundleReceipt
{
    private SupportBundleReceipt(
        string bundleId,
        WorkspaceRelativePath relativePath,
        string sha256,
        long length,
        DateTimeOffset createdAtUtc)
    {
        BundleId = bundleId;
        RelativePath = relativePath;
        Sha256 = sha256;
        Length = length;
        CreatedAtUtc = createdAtUtc;
    }

    public string BundleId { get; }

    public WorkspaceRelativePath RelativePath { get; }

    public string Sha256 { get; }

    public long Length { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public static ValidationResult<SupportBundleReceipt> Create(
        string? bundleId,
        WorkspaceRelativePath? relativePath,
        string? sha256,
        long length,
        DateTimeOffset createdAtUtc)
    {
        var issues = new List<ValidationIssue>();
        if (!SupportBundleRequest.IsCanonicalIdentifier(bundleId, 128))
        {
            issues.Add(new ValidationIssue(
                "support-bundle.bundle-id.invalid",
                "A canonical non-secret bundle identifier is required.",
                "bundleId"));
        }

        var expectedPath = bundleId is null ? null : $"support-bundles\\{bundleId}.zip";
        if (relativePath is null
            || !StringComparer.Ordinal.Equals(relativePath.Value, expectedPath))
        {
            issues.Add(new ValidationIssue(
                "support-bundle.path.invalid",
                "The bundle path must use the owned support-bundles directory.",
                "relativePath"));
        }

        if (sha256 is null
            || sha256.Length != 64
            || sha256.Any(character => character is not (>= '0' and <= '9')
                and not (>= 'a' and <= 'f')))
        {
            issues.Add(new ValidationIssue(
                "support-bundle.sha256.invalid",
                "A lowercase SHA-256 digest is required.",
                "sha256"));
        }

        if (length <= 0)
        {
            issues.Add(new ValidationIssue(
                "support-bundle.length.invalid",
                "A positive bundle length is required.",
                "length"));
        }

        if (createdAtUtc.Offset != TimeSpan.Zero)
        {
            issues.Add(new ValidationIssue(
                "support-bundle.created-at.utc",
                "A UTC creation timestamp is required.",
                "createdAtUtc"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new SupportBundleReceipt(
                bundleId!, relativePath!, sha256!, length, createdAtUtc))
            : ValidationResult.Failure<SupportBundleReceipt>(issues);
    }
}

public interface ISupportBundleWriter
{
    Task<ExecutionOperationResult<SupportBundleReceipt>> WriteAsync(
        RunCheckpoint checkpoint,
        bool includeEnvironmentSnapshot,
        CancellationToken cancellationToken);
}

public interface ISupportBundleCoordinator
{
    Task<ExecutionOperationResult<SupportBundleReceipt>> ExportAsync(
        SupportBundleRequest request,
        CancellationToken cancellationToken);
}

public sealed record SupportBundleCleanupReceipt(string BundleId, bool WasPresent);

public interface ISupportBundleCleanupService
{
    Task<ExecutionOperationResult<SupportBundleCleanupReceipt>> CleanupAsync(
        SupportBundleReceipt receipt,
        CancellationToken cancellationToken);
}
