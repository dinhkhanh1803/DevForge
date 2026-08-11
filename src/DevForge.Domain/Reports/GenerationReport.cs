using System.Collections.Immutable;
using DevForge.Domain.Diagnostics;
using DevForge.Domain.Privacy;
using DevForge.Domain.Validation;

namespace DevForge.Domain.Reports;

public enum ValidationCheckStatus
{
    Passed = 1,
    Failed = 2,
    Skipped = 3,
    Warning = 4,
}

public sealed record ValidationCheck(
    string Id,
    ValidationCheckStatus Status,
    string Summary,
    RedactedText? Detail);

public sealed record ReportToolStatus(
    string Id,
    bool Required,
    bool IsAvailable,
    bool IsCompatible,
    string? DetectedVersion);

public sealed record ReportWarning(string Code, RedactedText Message);

public sealed class GenerationReport
{
    private GenerationReport(
        string runId,
        DateTimeOffset generatedAt,
        IEnumerable<ValidationCheck> validations,
        IEnumerable<ReportToolStatus> toolStatuses,
        IEnumerable<ReportWarning> warnings,
        IEnumerable<DevForgeError> errors,
        IEnumerable<string> generatedArtifacts)
    {
        RunId = runId;
        GeneratedAt = generatedAt;
        Validations = [.. validations];
        ToolStatuses = [.. toolStatuses];
        Warnings = [.. warnings];
        Errors = [.. errors];
        GeneratedArtifacts = [.. generatedArtifacts];
    }

    public string RunId { get; }

    public DateTimeOffset GeneratedAt { get; }

    public ImmutableArray<ValidationCheck> Validations { get; }

    public ImmutableArray<ReportToolStatus> ToolStatuses { get; }

    public ImmutableArray<ReportWarning> Warnings { get; }

    public ImmutableArray<DevForgeError> Errors { get; }

    public ImmutableArray<string> GeneratedArtifacts { get; }

    public static ValidationResult<GenerationReport> Create(
        string? runId,
        DateTimeOffset generatedAt,
        IEnumerable<ValidationCheck?>? validations,
        IEnumerable<DevForgeError?>? errors,
        IEnumerable<string?>? generatedArtifacts) => Create(
            runId,
            generatedAt,
            validations,
            [],
            [],
            errors,
            generatedArtifacts);

    public static ValidationResult<GenerationReport> Create(
        string? runId,
        DateTimeOffset generatedAt,
        IEnumerable<ValidationCheck?>? validations,
        IEnumerable<ReportToolStatus?>? toolStatuses,
        IEnumerable<ReportWarning?>? warnings,
        IEnumerable<DevForgeError?>? errors,
        IEnumerable<string?>? generatedArtifacts)
    {
        var issues = new List<ValidationIssue>();
        if (string.IsNullOrWhiteSpace(runId))
        {
            issues.Add(new ValidationIssue("report.run-id.required", "Report run identifier is required.", "runId"));
        }

        var toolStatusSnapshot = toolStatuses?.ToImmutableArray() ?? [];
        ValidateToolStatuses(toolStatuses, toolStatusSnapshot, issues);
        var warningSnapshot = warnings?.ToImmutableArray() ?? [];
        ValidateWarnings(warnings, warningSnapshot, issues);

        var validationsSnapshot = validations?.ToImmutableArray() ?? [];
        if (validations is null)
        {
            issues.Add(
                new ValidationIssue(
                    "report.validations.required",
                    "Report validation results are required.",
                    "validations"));
        }
        else
        {
            var validationIds = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < validationsSnapshot.Length; index++)
            {
                var validation = validationsSnapshot[index];
                if (validation is null || string.IsNullOrWhiteSpace(validation.Id))
                {
                    issues.Add(
                        new ValidationIssue(
                            "report.validation.id.required",
                            "A report validation identifier is required.",
                            $"validations[{index}].id"));
                }
                else
                {
                    var normalizedId = validation.Id.Trim();
                    if (!validationIds.Add(normalizedId))
                    {
                        issues.Add(
                            new ValidationIssue(
                                "report.validation.id.duplicate",
                                "Report validation identifiers must be unique.",
                                $"validations[{index}].id"));
                    }
                }

                if (validation is not null && !Enum.IsDefined(validation.Status))
                {
                    issues.Add(
                        new ValidationIssue(
                            "report.validation.status.invalid",
                            "The validation status is not defined.",
                            $"validations[{index}].status"));
                }

                if (validation is not null && string.IsNullOrWhiteSpace(validation.Summary))
                {
                    issues.Add(
                        new ValidationIssue(
                            "report.validation.summary.required",
                            "A report validation summary is required.",
                            $"validations[{index}].summary"));
                }
            }
        }

        var errorsSnapshot = errors?.ToImmutableArray() ?? [];
        if (errors is null)
        {
            issues.Add(new ValidationIssue("report.errors.required", "Report errors are required.", "errors"));
        }
        else
        {
            for (var index = 0; index < errorsSnapshot.Length; index++)
            {
                if (errorsSnapshot[index] is null)
                {
                    issues.Add(
                        new ValidationIssue(
                            "report.error.required",
                            "Report errors cannot contain null values.",
                            $"errors[{index}]"));
                }
            }
        }

        var artifactsSnapshot = generatedArtifacts?.ToImmutableArray() ?? [];
        if (generatedArtifacts is null)
        {
            issues.Add(
                new ValidationIssue(
                    "report.artifacts.required",
                    "Generated artifacts are required.",
                    "generatedArtifacts"));
        }
        else
        {
            for (var index = 0; index < artifactsSnapshot.Length; index++)
            {
                if (string.IsNullOrWhiteSpace(artifactsSnapshot[index]))
                {
                    issues.Add(
                        new ValidationIssue(
                            "report.artifact.invalid",
                            "Generated artifacts cannot contain blank values.",
                            $"generatedArtifacts[{index}]"));
                }
            }
        }

        var normalizedValidations = validationsSnapshot.Select(
            validation => new ValidationCheck(
                validation!.Id.Trim(),
                validation.Status,
                validation.Summary.Trim(),
                validation.Detail));

        return issues.Count == 0
            ? ValidationResult.Success(
                new GenerationReport(
                    runId!.Trim(),
                    generatedAt,
                    normalizedValidations,
                    toolStatusSnapshot.Select(status => status!),
                    warningSnapshot.Select(warning => warning!),
                    errorsSnapshot.Select(error => error!),
                    artifactsSnapshot.Select(artifact => artifact!)))
            : ValidationResult.Failure<GenerationReport>(issues);
    }

    private static void ValidateToolStatuses(
        IEnumerable<ReportToolStatus?>? source,
        ImmutableArray<ReportToolStatus?> snapshot,
        List<ValidationIssue> issues)
    {
        if (source is null)
        {
            issues.Add(new ValidationIssue(
                "report.tool-statuses.required",
                "Report tool statuses are required.",
                "toolStatuses"));
        }

        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < snapshot.Length; index++)
        {
            var status = snapshot[index];
            if (status is null
                || string.IsNullOrWhiteSpace(status.Id)
                || status.Id.Length > 128
                || !identifiers.Add(status.Id.Trim())
                || !status.IsAvailable && status.IsCompatible
                || status.DetectedVersion?.Length > 128)
            {
                issues.Add(new ValidationIssue(
                    "report.tool-status.invalid",
                    "A report tool status is invalid.",
                    $"toolStatuses[{index}]"));
            }
        }
    }

    private static void ValidateWarnings(
        IEnumerable<ReportWarning?>? source,
        ImmutableArray<ReportWarning?> snapshot,
        List<ValidationIssue> issues)
    {
        if (source is null)
        {
            issues.Add(new ValidationIssue(
                "report.warnings.required",
                "Report warnings are required.",
                "warnings"));
        }

        for (var index = 0; index < snapshot.Length; index++)
        {
            var warning = snapshot[index];
            if (warning is null
                || string.IsNullOrWhiteSpace(warning.Code)
                || warning.Code.Length > 128
                || warning.Message is null)
            {
                issues.Add(new ValidationIssue(
                    "report.warning.invalid",
                    "A report warning is invalid.",
                    $"warnings[{index}]"));
            }
        }
    }
}
