namespace DevForge.UnitTests.Architecture;

public sealed class TemplateRendererDependencyTests
{
    private readonly RepositoryModel _repository = RepositoryModel.LoadFrom(AppContext.BaseDirectory);

    [Fact]
    public void ScribanIsPinnedAndOwnedOnlyByInfrastructure()
    {
        var versions = _repository.CentralPackageVersions
            .Where(package => package.Name.Equals("Scriban", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var owners = _repository.Projects.Values
            .Where(project => project.PackageReferences.Contains("Scriban"))
            .Select(project => project.Name)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal("7.2.5", Assert.Single(versions).Version);
        Assert.Equal(["DevForge.Infrastructure"], owners);
    }

    [Fact]
    public void ScribanAndEffectfulApisStayInsideApprovedBoundaries()
    {
        var repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        var productionSources = Directory
            .EnumerateFiles(
                Path.Combine(repositoryRoot, "src"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path => !HasGeneratedSegment(path))
            .ToArray();
        var scribanOutsideInfrastructure = productionSources
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}DevForge.Infrastructure{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path).Contains("Scriban", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(repositoryRoot, path))
            .ToArray();
        var rendererDirectory = Path.Combine(
            repositoryRoot,
            "src",
            "DevForge.Infrastructure",
            "Templates");
        var rendererText = Directory.Exists(rendererDirectory)
            ? string.Concat(
                Directory
                    .EnumerateFiles(rendererDirectory, "*.cs")
                    .Select(File.ReadAllText))
            : string.Empty;
        string[] forbiddenRendererTokens =
        [
            "System.Diagnostics.Process",
            "System.IO.File",
            "System.IO.Directory",
            "HttpClient",
            "Environment.",
            "ILogger",
            "Activator.",
            "System.Reflection",
        ];

        Assert.Empty(scribanOutsideInfrastructure);
        Assert.All(
            forbiddenRendererTokens,
            token => Assert.DoesNotContain(token, rendererText, StringComparison.Ordinal));
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
