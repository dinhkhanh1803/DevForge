using System.Text.RegularExpressions;

namespace DevForge.Blueprints.Abstractions.Validation;

internal static partial class BlueprintPrivacyPolicy
{
    private static readonly HashSet<string> _sensitiveIdentifiers =
        new(StringComparer.Ordinal)
        {
            "apikey",
            "accesstoken",
            "authtoken",
            "connectionstring",
            "credential",
            "credentials",
            "password",
            "privatekey",
            "secret",
            "token",
        };

    internal static bool IsSensitiveIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = NormalizeLettersAndDigits(value);
        if (_sensitiveIdentifiers.Any(
            sensitiveName => normalized.EndsWith(
                sensitiveName,
                StringComparison.Ordinal)))
        {
            return true;
        }

        return value
            .Split(['.', '-', '_', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeLettersAndDigits)
            .Any(_sensitiveIdentifiers.Contains);
    }

    internal static bool ContainsSensitiveDefault(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return ContainsCredentialAssignment(value)
            || GitHubTokenPattern().IsMatch(value)
            || OpenAiTokenPattern().IsMatch(value)
            || AwsAccessKeyPattern().IsMatch(value)
            || BearerTokenPattern().IsMatch(value)
            || JwtPattern().IsMatch(value)
            || PrivateKeyPattern().IsMatch(value)
            || LooksLikeEnvironmentFileContent(value);
    }

    private static bool ContainsCredentialAssignment(string value)
    {
        foreach (var segment in value.Split(['\r', '\n', ';', ',']))
        {
            var separatorIndex = segment.IndexOfAny(['=', ':']);
            if (separatorIndex <= 0 || separatorIndex == segment.Length - 1)
            {
                continue;
            }

            var name = NormalizeLettersAndDigits(segment[..separatorIndex]);
            if (_sensitiveIdentifiers.Any(
                sensitiveName => name.EndsWith(sensitiveName, StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeEnvironmentFileContent(string value)
    {
        return EnvironmentAssignmentPattern().Count(value) >= 2;
    }

    private static string NormalizeLettersAndDigits(string value)
    {
        return string.Concat(
            value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant));
    }

    [GeneratedRegex(
        @"\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,})\b",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex GitHubTokenPattern();

    [GeneratedRegex(
        @"\bsk-(?:(?:proj|svcacct)-)?[A-Za-z0-9_-]{20,}\b",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex OpenAiTokenPattern();

    [GeneratedRegex(
        @"\b(?:AKIA|ASIA)[A-Z0-9]{16}\b",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex AwsAccessKeyPattern();

    [GeneratedRegex(
        @"\bBearer[ \t]+[A-Za-z0-9._~+/=-]{16,}",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex BearerTokenPattern();

    [GeneratedRegex(
        @"\beyJ[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}\b",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex JwtPattern();

    [GeneratedRegex(
        @"-----BEGIN (?:[A-Z0-9]+ )*PRIVATE KEY-----",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex PrivateKeyPattern();

    [GeneratedRegex(
        @"^[ \t]*(?:export[ \t]+)?[A-Za-z_][A-Za-z0-9_]*[ \t]*=.+$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex EnvironmentAssignmentPattern();
}
