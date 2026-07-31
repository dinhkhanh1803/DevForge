using System.Collections.Immutable;
using DevForge.Domain.Privacy;
using DevForge.Domain.Validation;

namespace DevForge.Domain.Diagnostics;

public sealed class DevForgeError
{
    private DevForgeError(
        string code,
        string summary,
        SanitizedText technicalDetail,
        string phase,
        string? stepId,
        bool isRetryable,
        IEnumerable<string> suggestedActions,
        IEnumerable<KeyValuePair<string, SanitizedText>> redactedContext)
    {
        Code = code;
        Summary = summary;
        TechnicalDetail = technicalDetail;
        Phase = phase;
        StepId = stepId;
        IsRetryable = isRetryable;
        SuggestedActions = [.. suggestedActions];
        RedactedContext = redactedContext.ToImmutableDictionary(StringComparer.Ordinal);
    }

    public string Code { get; }

    public string Summary { get; }

    public SanitizedText TechnicalDetail { get; }

    public string Phase { get; }

    public string? StepId { get; }

    public bool IsRetryable { get; }

    public ImmutableArray<string> SuggestedActions { get; }

    public ImmutableDictionary<string, SanitizedText> RedactedContext { get; }


    public static ValidationResult<DevForgeError> Create(
        string? code,
        string? summary,
        SanitizedText? technicalDetail,
        string? phase,
        string? stepId,
        bool isRetryable,
        IEnumerable<string?>? suggestedActions,
        IEnumerable<KeyValuePair<string, SanitizedText>>? redactedContext)
    {
        var issues = new List<ValidationIssue>();
        AddRequiredIssue(issues, code, "error.code.required", "Error code is required.", "code");
        AddRequiredIssue(issues, summary, "error.summary.required", "Error summary is required.", "summary");
        if (technicalDetail is null)
        {
            issues.Add(
                new ValidationIssue(
                    "error.technical-detail.required",
                    "Error technical detail is required.",
                    "technicalDetail"));
        }
        AddRequiredIssue(issues, phase, "error.phase.required", "Error phase is required.", "phase");

        var actionsSnapshot = suggestedActions?.ToImmutableArray() ?? [];
        if (suggestedActions is null)
        {
            issues.Add(
                new ValidationIssue(
                    "error.suggested-actions.required",
                    "Suggested actions are required.",
                    "suggestedActions"));
        }
        else
        {
            for (var index = 0; index < actionsSnapshot.Length; index++)
            {
                if (string.IsNullOrWhiteSpace(actionsSnapshot[index]))
                {
                    issues.Add(
                        new ValidationIssue(
                            "error.suggested-action.invalid",
                            "Suggested actions cannot contain blank values.",
                            $"suggestedActions[{index}]"));
                }
            }
        }

        var contextSnapshot = redactedContext?.ToImmutableArray() ?? [];
        if (redactedContext is null)
        {
            issues.Add(new ValidationIssue("error.context.required", "Redacted context is required.", "redactedContext"));
        }
        else
        {
            var contextKeys = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < contextSnapshot.Length; index++)
            {
                var item = contextSnapshot[index];
                if (string.IsNullOrWhiteSpace(item.Key))
                {
                    issues.Add(
                        new ValidationIssue(
                            "error.context.key.required",
                            "A redacted context key is required.",
                            $"redactedContext[{index}].key"));
                }
                else
                {
                    var normalizedKey = item.Key.Trim();
                    if (!contextKeys.Add(normalizedKey))
                    {
                        issues.Add(
                            new ValidationIssue(
                                "error.context.key.duplicate",
                                "Redacted context keys must be unique.",
                                $"redactedContext[{index}].key"));
                    }
                    else if (SanitizedText.IsSecretShapedKey(normalizedKey))
                    {
                        issues.Add(
                            new ValidationIssue(
                                "error.context.key.secret-shaped",
                                "Redacted context keys cannot describe secrets.",
                                $"redactedContext[{index}].key"));
                    }
                }

                if (item.Value is null)
                {
                    issues.Add(
                        new ValidationIssue(
                            "error.context.value.required",
                            "A redacted context value is required.",
                            $"redactedContext[{index}].value"));
                }
            }
        }

        var normalizedContext = contextSnapshot.Select(
            item => KeyValuePair.Create(item.Key.Trim(), item.Value));

        return issues.Count == 0
            ? ValidationResult.Success(
                new DevForgeError(
                    code!.Trim(),
                    summary!.Trim(),
                    technicalDetail!,
                    phase!.Trim(),
                    string.IsNullOrWhiteSpace(stepId) ? null : stepId.Trim(),
                    isRetryable,
                    actionsSnapshot.Select(action => action!),
                    normalizedContext))
            : ValidationResult.Failure<DevForgeError>(issues);
    }

    private static void AddRequiredIssue(
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
