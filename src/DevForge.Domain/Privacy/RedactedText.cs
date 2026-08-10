using System.Text.RegularExpressions;
using DevForge.Domain.Validation;

namespace DevForge.Domain.Privacy;

/// <summary>
/// A value object for diagnostic text that the caller attests has already been redacted or scanned.
/// </summary>
/// <remarks>
/// Callers must remove secrets before crossing <see cref="FromTrustedRedaction(string?)"/>.
/// A future ISecretScanner integration belongs before this boundary. The built-in pattern checks are
/// defense in depth only and do not turn arbitrary raw text into trusted redacted text.
/// </remarks>
public sealed record RedactedText
{
    private static readonly TimeSpan _regexTimeout = TimeSpan.FromMilliseconds(100);

    private static readonly Regex _assignmentPattern = new(
        """(?i)(?<![a-z0-9_])(?:token|password|passwd|pwd|secret|credential|api[_-]?key|api[_-]?token|(?:auth|access|refresh|github)[_-]?token|(?:db|database)[_-]?password|openai[_-]?api[_-]?key|(?:aws[_-]?)?secret[_-]?access[_-]?key|connection[_-]?string)["']?\s*[:=]""",
        RegexOptions.CultureInvariant,
        _regexTimeout);

    private static readonly Regex _xmlAssignmentPattern = new(
        @"(?i)<(?:token|password|passwd|pwd|secret|credential|api[_-]?key|api[_-]?token|(?:auth|access|refresh|github)[_-]?token|(?:db|database)[_-]?password|openai[_-]?api[_-]?key|(?:aws[_-]?)?secret[_-]?access[_-]?key|connection[_-]?string)(?:\s[^>]*)?>\s*[^<\s]",
        RegexOptions.CultureInvariant,
        _regexTimeout);

    private static readonly Regex _environmentContentPattern = new(
        @"(?i)(?:\.env(?:\s+file)?\s+(?:contents?|values?|dump)|(?:contents?|values?|dump)\s+(?:of|from)\s+\.env)",
        RegexOptions.CultureInvariant,
        _regexTimeout);

    private static readonly Regex _privateKeyPattern = new(
        @"(?i)-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----",
        RegexOptions.CultureInvariant,
        _regexTimeout);

    private static readonly Regex _bearerPattern = new(
        @"(?i)(?<![a-z0-9_-])Bearer\s+[a-z0-9._~+/=-]{8,}",
        RegexOptions.CultureInvariant,
        _regexTimeout);

    private static readonly Regex _jwtPattern = new(
        @"(?i)(?<![a-z0-9_-])eyJ[a-z0-9_-]{6,}\.eyJ[a-z0-9_-]{6,}\.[a-z0-9_-]{6,}",
        RegexOptions.CultureInvariant,
        _regexTimeout);

    private static readonly Regex _serviceTokenPattern = new(
        @"(?i)(?<![a-z0-9_-])(?:sk-(?:proj-|svcacct-)?[a-z0-9_-]{16,}|(?:AKIA|ASIA)[A-Z0-9]{16}|gh[pousr]_[a-z0-9]{12,}|github_pat_[a-z0-9_]{12,})",
        RegexOptions.CultureInvariant,
        _regexTimeout);

    private static readonly string[] _secretKeyFragments =
    [
        "apikey",
        "connectionstring",
        "credential",
        "password",
        "privatekey",
        "secret",
        "token",
    ];

    private RedactedText(string value)
    {
        Value = value;
    }

    public string Value { get; }

    /// <summary>
    /// Creates redacted text after the caller has already redacted or scanned the supplied value.
    /// </summary>
    /// <remarks>
    /// The heuristic checks here reject common credential shapes as defense in depth. They are not a
    /// substitute for the caller's redaction responsibility or a future ISecretScanner implementation.
    /// </remarks>
    public static ValidationResult<RedactedText> FromTrustedRedaction(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ValidationResult.Failure<RedactedText>(
            [
                new ValidationIssue("privacy.value.required", "A redacted value is required.", "value"),
            ]);
        }

        var trimmed = value.Trim();
        if (LooksSecretShaped(trimmed))
        {
            return ValidationResult.Failure<RedactedText>(
            [
                new ValidationIssue(
                    "privacy.value.secret-shaped",
                    "The value resembles credential material and cannot be retained.",
                    "value"),
            ]);
        }

        return ValidationResult.Success(new RedactedText(trimmed));
    }

    public static bool IsSecretShapedKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var normalized = string.Concat(key.Where(char.IsLetterOrDigit));
        return _secretKeyFragments.Any(
            fragment => normalized.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksSecretShaped(string value)
    {
        try
        {
            return _assignmentPattern.IsMatch(value)
                || _xmlAssignmentPattern.IsMatch(value)
                || _environmentContentPattern.IsMatch(value)
                || _privateKeyPattern.IsMatch(value)
                || _bearerPattern.IsMatch(value)
                || _jwtPattern.IsMatch(value)
                || _serviceTokenPattern.IsMatch(value);
        }
        catch (RegexMatchTimeoutException)
        {
            return true;
        }
    }
}
