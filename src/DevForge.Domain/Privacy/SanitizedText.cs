using System.Text.RegularExpressions;
using DevForge.Domain.Validation;

namespace DevForge.Domain.Privacy;

public sealed class SanitizedText
{
    private static readonly Regex _assignmentPattern = new(
        @"(?i)(token|password|passwd|pwd|secret|api[_-]?key|connection[_-]?string)\s*=",
        RegexOptions.CultureInvariant);

    private static readonly Regex _githubTokenPattern = new(
        @"(?i)(^|[^a-z0-9])(gh[pousr]_|github_pat_)",
        RegexOptions.CultureInvariant);

    private static readonly Regex _environmentFilePattern = new(
        @"(?i)(^|[\s/\\])\.env($|[.\s/\\])",
        RegexOptions.CultureInvariant);

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

    private SanitizedText(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static ValidationResult<SanitizedText> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ValidationResult.Failure<SanitizedText>(
            [
                new ValidationIssue("privacy.value.required", "A sanitized value is required.", "value"),
            ]);
        }

        var trimmed = value.Trim();
        if (_assignmentPattern.IsMatch(trimmed)
            || _githubTokenPattern.IsMatch(trimmed)
            || _environmentFilePattern.IsMatch(trimmed))
        {
            return ValidationResult.Failure<SanitizedText>(
            [
                new ValidationIssue(
                    "privacy.value.secret-shaped",
                    "The value resembles credential material and cannot be retained.",
                    "value"),
            ]);
        }

        return ValidationResult.Success(new SanitizedText(trimmed));
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
}
