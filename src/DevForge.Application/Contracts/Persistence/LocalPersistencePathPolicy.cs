namespace DevForge.Application.Contracts.Persistence;

internal static class LocalPersistencePathPolicy
{
    private static readonly HashSet<string> _reservedNames = new(
        ["AUX", "CON", "NUL", "PRN", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"],
        StringComparer.OrdinalIgnoreCase);

    public static string? TryNormalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 1_024
            || value.Any(char.IsControl)
            || value.StartsWith("\\\\", StringComparison.Ordinal)
            || !Path.IsPathFullyQualified(value)
            || value.Length < 3
            || !char.IsAsciiLetter(value[0])
            || value[1] != ':'
            || value[2] != Path.DirectorySeparatorChar
            || value.AsSpan(2).Contains(':'))
        {
            return null;
        }

        var rawSegments = value[3..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        if (rawSegments.Any(segment =>
            segment is "." or ".."
            || !string.Equals(segment, segment.TrimEnd(' ', '.'), StringComparison.Ordinal)
            || IsReservedSegment(segment)))
        {
            return null;
        }

        try
        {
            var fullPath = Path.GetFullPath(value);
            return fullPath.Length == 3
                ? fullPath
                : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    public static bool IsReservedSegment(string segment)
    {
        var stem = segment.Split('.')[0];
        return _reservedNames.Contains(stem);
    }
}
