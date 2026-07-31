using System.Collections.Immutable;
using DevForge.Domain.Diagnostics;

namespace DevForge.Domain.Reports;

public enum ValidationCheckStatus
{
    Passed,
    Failed,
    Skipped,
}

public sealed record ValidationCheck(
    string Id,
    ValidationCheckStatus Status,
    string Summary,
    string? Detail);

public sealed class GenerationReport
{
    public GenerationReport(
        string runId,
        DateTimeOffset generatedAt,
        IEnumerable<ValidationCheck> validations,
        IEnumerable<DevForgeError> errors,
        IEnumerable<string> generatedArtifacts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(validations);
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(generatedArtifacts);

        RunId = runId;
        GeneratedAt = generatedAt;
        Validations = [.. validations];
        Errors = [.. errors];
        GeneratedArtifacts = [.. generatedArtifacts];
    }

    public string RunId { get; }

    public DateTimeOffset GeneratedAt { get; }

    public ImmutableArray<ValidationCheck> Validations { get; }

    public ImmutableArray<DevForgeError> Errors { get; }

    public ImmutableArray<string> GeneratedArtifacts { get; }
}
