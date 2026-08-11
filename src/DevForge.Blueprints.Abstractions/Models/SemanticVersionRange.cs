using System.Diagnostics.CodeAnalysis;

namespace DevForge.Blueprints.Abstractions.Models;

/// <summary>
/// Represents a normalized dependency-free semantic-version range expression.
/// </summary>
/// <remarks>
/// Supported grammar is either one exact Semantic Version 2.0 value or one or more
/// comparator clauses using <c>&lt;</c>, <c>&lt;=</c>, <c>=</c>, <c>&gt;=</c>, or
/// <c>&gt;</c>. Whitespace joins comparator clauses with logical AND, and
/// <c>||</c> joins non-empty groups with logical OR. Prerelease and build metadata
/// follow Semantic Version 2.0. Caret, tilde, wildcard, hyphen, and NuGet interval
/// syntax are intentionally unsupported.
/// </remarks>
public sealed record SemanticVersionRange
{
    private SemanticVersionRange(string expression)
    {
        Expression = expression;
    }

    /// <summary>
    /// Gets the normalized range expression.
    /// </summary>
    public string Expression { get; }

    /// <summary>
    /// Attempts to parse a supported semantic-version range expression.
    /// </summary>
    /// <param name="expression">The expression to parse.</param>
    /// <param name="range">The normalized range when parsing succeeds.</param>
    /// <returns><see langword="true"/> when the expression uses the supported grammar.</returns>
    public static bool TryParse(
        string? expression,
        [NotNullWhen(true)] out SemanticVersionRange? range)
    {
        range = null;
        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        var tokens = Split(expression);
        var clauseCount = 0;
        bool? groupUsesComparators = null;

        foreach (var token in tokens)
        {
            if (token == "||")
            {
                if (clauseCount == 0)
                {
                    return false;
                }

                clauseCount = 0;
                groupUsesComparators = null;
                continue;
            }

            if (!TrySeparateComparator(token, out var hasComparator, out var version)
                || !SemanticVersion.TryParse(version, out _))
            {
                return false;
            }

            groupUsesComparators ??= hasComparator;
            if (groupUsesComparators != hasComparator
                || (!hasComparator && clauseCount > 0))
            {
                return false;
            }

            clauseCount++;
        }

        if (clauseCount == 0)
        {
            return false;
        }

        range = new SemanticVersionRange(string.Join(' ', tokens));
        return true;
    }

    /// <summary>
    /// Determines whether a semantic version satisfies this range.
    /// </summary>
    public bool Contains(SemanticVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        var currentGroupMatches = true;
        foreach (var token in Split(Expression))
        {
            if (token == "||")
            {
                if (currentGroupMatches)
                {
                    return true;
                }

                currentGroupMatches = true;
                continue;
            }

            _ = TrySeparateComparator(token, out var hasComparator, out var candidateText);
            _ = SemanticVersion.TryParse(candidateText, out var candidate);
            var comparison = version.CompareTo(candidate);
            currentGroupMatches &= hasComparator
                ? MatchesComparator(token, comparison)
                : comparison == 0;
        }

        return currentGroupMatches;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Expression;
    }

    internal static bool IsSemanticVersion(string? value)
    {
        return SemanticVersion.TryParse(value, out _);
    }

    private static bool MatchesComparator(string token, int comparison)
    {
        return token[0] switch
        {
            '>' when token.StartsWith(">=", StringComparison.Ordinal) => comparison >= 0,
            '<' when token.StartsWith("<=", StringComparison.Ordinal) => comparison <= 0,
            '>' => comparison > 0,
            '<' => comparison < 0,
            '=' => comparison == 0,
            _ => false,
        };
    }

    private static bool TrySeparateComparator(
        string token,
        out bool hasComparator,
        out string version)
    {
        hasComparator = true;
        if (token.StartsWith(">=", StringComparison.Ordinal)
            || token.StartsWith("<=", StringComparison.Ordinal))
        {
            version = token[2..];
        }
        else if (token[0] is '>' or '<' or '=')
        {
            version = token[1..];
        }
        else
        {
            hasComparator = false;
            version = token;
        }

        return version.Length > 0;
    }

    private static string[] Split(string expression)
    {
        return expression.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

}
