using System.Collections.Immutable;
using System.Text.RegularExpressions;
using DevForge.Application.Contracts;
using DevForge.Domain.Privacy;

namespace DevForge.Infrastructure.Processes;

internal sealed class ProcessOutputRedactor
{
    private const string RedactedOutput = "[REDACTED OUTPUT]";

    private static readonly Regex _environmentContentPattern = new(
        @"(?i)(?:\.env(?:\s+file)?\s+(?:contents?|values?|dump)|(?:contents?|values?|dump)\s+(?:of|from)\s+\.env).*",
        RegexOptions.CultureInvariant);

    private static readonly Regex _privateKeyPattern = new(
        @"(?i)-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----",
        RegexOptions.CultureInvariant);

    private static readonly Regex _bearerPattern = new(
        @"(?i)(?<![a-z0-9_-])Bearer\s+[a-z0-9._~+/=-]{8,}",
        RegexOptions.CultureInvariant);

    private static readonly Regex _jwtPattern = new(
        @"(?i)(?<![a-z0-9_-])eyJ[a-z0-9_-]{6,}\.eyJ[a-z0-9_-]{6,}\.[a-z0-9_-]{6,}",
        RegexOptions.CultureInvariant);

    private static readonly Regex _serviceTokenPattern = new(
        @"(?i)(?<![a-z0-9_-])(?:sk-(?:proj-|svcacct-)?[a-z0-9_-]{16,}|(?:AKIA|ASIA)[A-Z0-9]{16}|gh[pousr]_[a-z0-9]{12,}|github_pat_[a-z0-9_]{12,})",
        RegexOptions.CultureInvariant);

    private static readonly Regex _assignmentPattern = new(
        @"(?i)(?<![a-z0-9_])(?:token|password|passwd|pwd|secret|credential|api[_-]?key|api[_-]?token|(?:auth|access|refresh|github)[_-]?token|(?:db|database)[_-]?password|openai[_-]?api[_-]?key|(?:aws[_-]?)?secret[_-]?access[_-]?key|connection[_-]?string)\s*[:=]\s*[^\s;,]+",
        RegexOptions.CultureInvariant);

    private readonly ImmutableArray<SensitiveProcessValue> _needles;

    public ProcessOutputRedactor(IEnumerable<SensitiveProcessValue> needles)
    {
        ArgumentNullException.ThrowIfNull(needles);
        _needles = [.. needles];
    }

    public bool TryRedact(string? rawText, out RedactedText? redactedText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            redactedText = null;
            return false;
        }

        var redacted = rawText;
        foreach (var needle in _needles)
        {
            redacted = redacted.Replace(
                needle.RevealForProcessStart(),
                "[REDACTED]",
                StringComparison.Ordinal);
        }

        redacted = _environmentContentPattern.Replace(redacted, "[REDACTED ENV CONTENT]");
        redacted = _privateKeyPattern.Replace(redacted, "[REDACTED PRIVATE KEY]");
        redacted = _bearerPattern.Replace(redacted, "[REDACTED BEARER]");
        redacted = _jwtPattern.Replace(redacted, "[REDACTED JWT]");
        redacted = _serviceTokenPattern.Replace(redacted, "[REDACTED TOKEN]");
        redacted = _assignmentPattern.Replace(redacted, "[REDACTED ASSIGNMENT]");

        var result = RedactedText.FromTrustedRedaction(redacted);
        if (!result.IsValid)
        {
            result = RedactedText.FromTrustedRedaction(RedactedOutput);
        }

        redactedText = result.Value;
        return true;
    }
}
