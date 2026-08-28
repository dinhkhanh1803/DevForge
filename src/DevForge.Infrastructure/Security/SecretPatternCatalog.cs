using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace DevForge.Infrastructure.Security;

internal static class SecretPatternCatalog
{
    private static readonly ImmutableArray<SecretPattern> _patterns =
    [
        new(
            "private key",
            new Regex(
                @"(?i)-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----",
                RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100))),
        new(
            "bearer credential",
            new Regex(
                @"(?i)(?<![a-z0-9_-])Bearer\s+[a-z0-9._~+/=-]{8,}",
                RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100))),
        new(
            "JWT credential",
            new Regex(
                @"(?i)(?<![a-z0-9_-])eyJ[a-z0-9_-]{6,}\.eyJ[a-z0-9_-]{6,}\.[a-z0-9_-]{6,}",
                RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100))),
        new(
            "service token",
            new Regex(
                @"(?i)(?<![a-z0-9_-])(?:sk-(?:proj-|svcacct-)?[a-z0-9_-]{16,}|(?:AKIA|ASIA)[A-Z0-9]{16}|gh[pousr]_[a-z0-9]{12,}|github_pat_[a-z0-9_]{12,})",
                RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100))),
        new(
            "secret assignment",
            new Regex(
                """(?i)(?<![a-z0-9_])(?:token|password|passwd|pwd|secret|credential|api[_-]?key|api[_-]?token|(?:auth|access|refresh|github)[_-]?token|(?:db|database)[_-]?password|openai[_-]?api[_-]?key|(?:aws[_-]?)?secret[_-]?access[_-]?key|connection[_-]?string)["']?\s*[:=]\s*["']?[^\s;,}"']+["']?""",
                RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100)), "generic-assignment"),
        new(
            "secret assignment",
            new Regex(
                @"(?i)<(?:token|password|passwd|pwd|secret|credential|api[_-]?key|api[_-]?token|(?:auth|access|refresh|github)[_-]?token|(?:db|database)[_-]?password|openai[_-]?api[_-]?key|(?:aws[_-]?)?secret[_-]?access[_-]?key|connection[_-]?string)(?:\s[^>]*)?>\s*[^<\s][^<]*",
                RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100))),
    ];

    public static ImmutableArray<string> FindCategories(string line, bool reviewedReactInputMapLine = false)
    {
        return
        [
            .. _patterns
                .Where(pattern => reviewedReactInputMapLine && pattern.Id == "generic-assignment"
                    ? pattern.Expression.Matches(line).Any(match =>
                        match.Index != 21275 || match.Length != 11 || match.Value != "password:!0")
                    : pattern.Expression.IsMatch(line))
                .Select(pattern => pattern.Category)
                .Distinct(StringComparer.OrdinalIgnoreCase),
        ];
    }

    private sealed record SecretPattern(string Category, Regex Expression, string? Id = null);
}
