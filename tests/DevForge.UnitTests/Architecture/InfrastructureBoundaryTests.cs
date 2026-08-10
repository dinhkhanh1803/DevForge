namespace DevForge.UnitTests.Architecture;

public sealed class InfrastructureBoundaryTests
{
    [Fact]
    public void AnalyzerReportsForbiddenProcessStartOutsideInfrastructure()
    {
        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["src/DevForge.Desktop/UnsafeLauncher.cs"] =
                "using System.Diagnostics; class UnsafeLauncher { void Run() => Process.Start(\"git\"); }",
        };

        var violations = InfrastructureBoundary.FindViolationsFromSources(sources);

        Assert.Equal(
            ["src/DevForge.Desktop/UnsafeLauncher.cs: forbidden Process.Start outside Infrastructure"],
            violations);
    }

    [Fact]
    public void AnalyzerAllowsOperatingSystemEffectsInsideInfrastructure()
    {
        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["src/DevForge.Infrastructure/Processes/WindowsProcessRunner.cs"] =
                "using System.Diagnostics; class Runner { void Run() => Process.Start(new ProcessStartInfo()); }",
        };

        var violations = InfrastructureBoundary.FindViolationsFromSources(sources);

        Assert.Empty(violations);
    }

    [Fact]
    public void ProductionSourcesKeepOperatingSystemEffectsInsideInfrastructure()
    {
        var repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        var sources = Directory
            .EnumerateFiles(Path.Combine(repositoryRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !HasGeneratedSegment(path))
            .ToDictionary(
                path => Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'),
                File.ReadAllText,
                StringComparer.OrdinalIgnoreCase);

        var violations = InfrastructureBoundary.FindViolationsFromSources(sources);

        Assert.Empty(violations);
    }

    private static string FindRepositoryRoot(string startDirectory)
    {
        for (var directory = new DirectoryInfo(Path.GetFullPath(startDirectory));
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DevForge.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static bool HasGeneratedSegment(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Contains("bin", StringComparer.OrdinalIgnoreCase)
            || segments.Contains("obj", StringComparer.OrdinalIgnoreCase);
    }
}

internal static class InfrastructureBoundary
{
    private static readonly (string Pattern, string Description)[] _forbiddenPatterns =
    [
        ("Process.Start(", "forbidden Process.Start outside Infrastructure"),
        ("new ProcessStartInfo", "forbidden ProcessStartInfo outside Infrastructure"),
        ("UseShellExecute = true", "forbidden shell execution"),
        ("File.WriteAll", "forbidden direct file write outside Infrastructure"),
        ("File.Delete(", "forbidden direct file delete outside Infrastructure"),
        ("File.Move(", "forbidden direct file move outside Infrastructure"),
        ("Directory.CreateDirectory(", "forbidden direct directory creation outside Infrastructure"),
        ("Directory.Delete(", "forbidden direct directory delete outside Infrastructure"),
        ("Directory.Move(", "forbidden direct directory move outside Infrastructure"),
        ("cmd /c", "forbidden command shell text"),
        ("powershell.exe", "forbidden PowerShell execution"),
        ("pwsh.exe", "forbidden PowerShell execution"),
    ];

    public static string[] FindViolationsFromSources(
        IReadOnlyDictionary<string, string> sources)
    {
        var violations = new List<string>();
        foreach (var source in sources.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            var normalizedPath = source.Key.Replace('\\', '/');
            if (normalizedPath.StartsWith(
                "src/DevForge.Infrastructure/",
                StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var pattern in _forbiddenPatterns)
            {
                if (source.Value.Contains(pattern.Pattern, StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add($"{normalizedPath}: {pattern.Description}");
                }
            }
        }

        return [.. violations];
    }
}
