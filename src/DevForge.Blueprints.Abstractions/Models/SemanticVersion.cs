using System.Diagnostics.CodeAnalysis;

namespace DevForge.Blueprints.Abstractions.Models;

/// <summary>
/// Represents one normalized Semantic Version 2.0 value.
/// </summary>
public sealed record SemanticVersion : IComparable<SemanticVersion>
{
    private const int MaximumLength = 256;

    private SemanticVersion(
        string normalized,
        string major,
        string minor,
        string patch,
        string? prerelease)
    {
        Normalized = normalized;
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
    }

    /// <summary>
    /// Gets the normalized version text, including prerelease and build metadata.
    /// </summary>
    public string Normalized { get; }

    private string Major { get; }

    private string Minor { get; }

    private string Patch { get; }

    private string? Prerelease { get; }

    /// <summary>
    /// Attempts to parse a Semantic Version 2.0 value.
    /// </summary>
    public static bool TryParse(
        string? value,
        [NotNullWhen(true)] out SemanticVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Trim();
        if (candidate.Length > MaximumLength)
        {
            return false;
        }

        var buildSeparator = candidate.IndexOf('+', StringComparison.Ordinal);
        if (buildSeparator >= 0
            && (candidate.IndexOf('+', buildSeparator + 1) >= 0
                || !AreValidIdentifiers(candidate[(buildSeparator + 1)..], false)))
        {
            return false;
        }

        var coreAndPrerelease = buildSeparator < 0
            ? candidate
            : candidate[..buildSeparator];
        var prereleaseSeparator = coreAndPrerelease.IndexOf('-', StringComparison.Ordinal);
        var core = prereleaseSeparator < 0
            ? coreAndPrerelease
            : coreAndPrerelease[..prereleaseSeparator];
        var prerelease = prereleaseSeparator < 0
            ? null
            : coreAndPrerelease[(prereleaseSeparator + 1)..];

        if (prerelease is not null && !AreValidIdentifiers(prerelease, true))
        {
            return false;
        }

        var coreParts = core.Split('.');
        if (coreParts.Length != 3 || coreParts.Any(part => !IsValidNumericIdentifier(part)))
        {
            return false;
        }

        version = new SemanticVersion(
            candidate,
            coreParts[0],
            coreParts[1],
            coreParts[2],
            prerelease);
        return true;
    }

    /// <inheritdoc />
    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var coreComparison = CompareNumeric(Major, other.Major);
        if (coreComparison != 0)
        {
            return coreComparison;
        }

        coreComparison = CompareNumeric(Minor, other.Minor);
        if (coreComparison != 0)
        {
            return coreComparison;
        }

        coreComparison = CompareNumeric(Patch, other.Patch);
        if (coreComparison != 0)
        {
            return coreComparison;
        }

        return ComparePrerelease(Prerelease, other.Prerelease);
    }

    public static bool operator <(SemanticVersion? left, SemanticVersion? right)
    {
        return Compare(left, right) < 0;
    }

    public static bool operator <=(SemanticVersion? left, SemanticVersion? right)
    {
        return Compare(left, right) <= 0;
    }

    public static bool operator >(SemanticVersion? left, SemanticVersion? right)
    {
        return Compare(left, right) > 0;
    }

    public static bool operator >=(SemanticVersion? left, SemanticVersion? right)
    {
        return Compare(left, right) >= 0;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Normalized;
    }

    private static int ComparePrerelease(string? left, string? right)
    {
        if (left is null)
        {
            return right is null ? 0 : 1;
        }

        if (right is null)
        {
            return -1;
        }

        var leftParts = left.Split('.');
        var rightParts = right.Split('.');
        var commonLength = Math.Min(leftParts.Length, rightParts.Length);
        for (var index = 0; index < commonLength; index++)
        {
            var leftNumeric = leftParts[index].All(IsAsciiDigit);
            var rightNumeric = rightParts[index].All(IsAsciiDigit);
            int comparison;
            if (leftNumeric && rightNumeric)
            {
                comparison = CompareNumeric(leftParts[index], rightParts[index]);
            }
            else if (leftNumeric != rightNumeric)
            {
                comparison = leftNumeric ? -1 : 1;
            }
            else
            {
                comparison = string.CompareOrdinal(leftParts[index], rightParts[index]);
            }

            if (comparison != 0)
            {
                return comparison;
            }
        }

        return leftParts.Length.CompareTo(rightParts.Length);
    }

    private static int Compare(SemanticVersion? left, SemanticVersion? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        return left is null ? -1 : left.CompareTo(right);
    }

    private static int CompareNumeric(string left, string right)
    {
        var lengthComparison = left.Length.CompareTo(right.Length);
        return lengthComparison != 0 ? lengthComparison : string.CompareOrdinal(left, right);
    }

    private static bool AreValidIdentifiers(string value, bool forbidNumericLeadingZero)
    {
        var identifiers = value.Split('.');
        foreach (var identifier in identifiers)
        {
            if (identifier.Length == 0
                || identifier.Any(character => !IsAsciiLetterOrDigit(character) && character != '-'))
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
