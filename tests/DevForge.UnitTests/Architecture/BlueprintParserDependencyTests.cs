namespace DevForge.UnitTests.Architecture;

public sealed class BlueprintParserDependencyTests
{
    private readonly RepositoryModel _repository = RepositoryModel.LoadFrom(AppContext.BaseDirectory);

    [Fact]
    public void YamlDotNetIsPinnedAndOwnedOnlyByInfrastructure()
    {
        var versions = _repository.CentralPackageVersions
            .Where(package => package.Name.Equals("YamlDotNet", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var owners = _repository.Projects.Values
            .Where(project => project.PackageReferences.Contains("YamlDotNet"))
            .Select(project => project.Name)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal("18.1.0", Assert.Single(versions).Version);
        Assert.Equal(["DevForge.Infrastructure"], owners);
    }

    [Fact]
    public void YamlAndEffectfulParserApisStayOutsidePureLayers()
    {
        var repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        var pureLayerRoots = new[]
        {
            Path.Combine(repositoryRoot, "src", "DevForge.Application"),
            Path.Combine(repositoryRoot, "src", "DevForge.Blueprints.Abstractions"),
        };
        string[] forbiddenTokens =
        [
            "YamlDotNet",
            "System.IO.File",
            "System.IO.Directory",
            "System.Diagnostics.Process",
            "System.Reflection",
            "Activator.",
        ];

        var sources = pureLayerRoots
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(path => !HasGeneratedSegment(path))
            .Select(path => new { Path = path, Text = File.ReadAllText(path) })
            .ToArray();

        Assert.All(
            forbiddenTokens,
            token => Assert.DoesNotContain(
                sources,
                source => source.Text.Contains(token, StringComparison.Ordinal)));
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
