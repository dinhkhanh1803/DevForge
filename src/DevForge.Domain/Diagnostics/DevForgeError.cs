using System.Collections.Immutable;

namespace DevForge.Domain.Diagnostics;

public sealed class DevForgeError
{
    public DevForgeError(
        string code,
        string summary,
        string technicalDetail,
        string phase,
        string? stepId,
        bool isRetryable,
        IEnumerable<string> suggestedActions,
        IEnumerable<KeyValuePair<string, string>> redactedContext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentNullException.ThrowIfNull(technicalDetail);
        ArgumentException.ThrowIfNullOrWhiteSpace(phase);
        ArgumentNullException.ThrowIfNull(suggestedActions);
        ArgumentNullException.ThrowIfNull(redactedContext);

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

    public string TechnicalDetail { get; }

    public string Phase { get; }

    public string? StepId { get; }

    public bool IsRetryable { get; }

    public ImmutableArray<string> SuggestedActions { get; }

    public ImmutableDictionary<string, string> RedactedContext { get; }
}
