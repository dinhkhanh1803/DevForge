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
                || !IsSemanticVersion(version))
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

    /// <inheritdoc />
    public override string ToString()
    {
        return Expression;
    }

    internal static SemanticVersionRange ParseValidated(string expression)
    {
        return TryParse(expression, out var range)
            ? range
            : throw new ArgumentException(
                "The expression must be a supported semantic-version range.",
                nameof(expression));
    }

    internal static bool IsSemanticVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Trim();
        var buildParts = candidate.Split('+');
        if (buildParts.Length > 2
            || (buildParts.Length == 2 && !AreValidIdentifiers(buildParts[1], false)))
        {
            return false;
        }

        var coreAndPrerelease = buildParts[0];
        var prereleaseSeparator = coreAndPrerelease.IndexOf('-', StringComparison.Ordinal);
        var core = prereleaseSeparator < 0
            ? coreAndPrerelease
            : coreAndPrerelease[..prereleaseSeparator];
        if (prereleaseSeparator >= 0
            && !AreValidIdentifiers(coreAndPrerelease[(prereleaseSeparator + 1)..], true))
        {
            return false;
        }

        var coreParts = core.Split('.');
        return coreParts.Length == 3 && coreParts.All(IsValidNumericIdentifier);
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

    private static bool AreValidIdentifiers(string value, bool forbidNumericLeadingZero)
    {
        var identifiers = value.Split('.');
        foreach (var identifier in identifiers)
        {
            if (identifier.Length == 0
                || identifier.Any(
                    character => !IsAsciiLetterOrDigit(character) && character != '-'))
            {
                return false;
            }

            if (forbidNumericLeadingZero
                && identifier.All(IsAsciiDigit)
                && !IsValidNumericIdentifier(identifier))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidNumericIdentifier(string value)
    {
        return value.Length > 0
            && value.All(IsAsciiDigit)
            && (value.Length == 1 || value[0] != '0');
    }

    private static string[] Split(string expression)
    {
        return expression.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool IsAsciiLetterOrDigit(char value)
    {
        return value is >= 'a' and <= 'z'
            || value is >= 'A' and <= 'Z'
            || IsAsciiDigit(value);
    }

    private static bool IsAsciiDigit(char value)
    {
        return value is >= '0' and <= '9';
    }
}
