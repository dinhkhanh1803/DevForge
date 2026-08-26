using System.IO;

namespace DevForge.Desktop.Bootstrap;

public static class DesktopLocalDataRootResolver
{
    private const string LocalDataArgument = "--local-data-root";

    public static string Resolve(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count == 0)
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DevForge");
        }

        if (arguments.Count != 2
            || !StringComparer.Ordinal.Equals(arguments[0], LocalDataArgument)
            || string.IsNullOrWhiteSpace(arguments[1])
            || arguments[1] != arguments[1].Trim()
            || !Path.IsPathFullyQualified(arguments[1]))
        {
            throw new ArgumentException(
                "Desktop startup arguments must be empty or contain one absolute local-data root.",
                nameof(arguments));
        }

        var resolved = Path.GetFullPath(arguments[1]);
        var testOwnerRoot = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            "DevForge-ReleasePackageTests"));
        if (!resolved.StartsWith(
            testOwnerRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The isolated local-data root is outside the test-owned boundary.",
                nameof(arguments));
        }

        return resolved;
    }
}
