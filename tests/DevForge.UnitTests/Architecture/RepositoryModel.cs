using System.Xml.Linq;

namespace DevForge.UnitTests.Architecture;

internal sealed class RepositoryModel
{
    private RepositoryModel(
        IReadOnlyDictionary<string, ProjectModel> projects,
        IReadOnlyDictionary<string, ProjectModel> productionProjects,
        IReadOnlyDictionary<string, ProjectModel> testProjects,
        IReadOnlySet<string> solutionProjects,
        bool centralPackageManagementEnabled,
        bool centralPackageManagementDeclarationIsSingleAndUnconditional,
        IReadOnlyList<CentralPackageVersion> centralPackageVersions)
    {
        Projects = projects;
        ProductionProjects = productionProjects;
        TestProjects = testProjects;
        SolutionProjects = solutionProjects;
        CentralPackageManagementEnabled = centralPackageManagementEnabled;
        CentralPackageManagementDeclarationIsSingleAndUnconditional =
            centralPackageManagementDeclarationIsSingleAndUnconditional;
        CentralPackageVersions = centralPackageVersions;
    }

    public IReadOnlyDictionary<string, ProjectModel> Projects { get; }

    public IReadOnlyDictionary<string, ProjectModel> ProductionProjects { get; }

    public IReadOnlyDictionary<string, ProjectModel> TestProjects { get; }

    public IReadOnlySet<string> SolutionProjects { get; }

    public bool CentralPackageManagementEnabled { get; }

    public bool CentralPackageManagementDeclarationIsSingleAndUnconditional { get; }

    public IReadOnlyList<CentralPackageVersion> CentralPackageVersions { get; }

    public static RepositoryModel LoadFrom(string startDirectory)
    {
        var repositoryDirectory = FindRepositoryDirectory(startDirectory);
        var sourceDirectory = Path.Combine(repositoryDirectory, "src");
        var testsDirectory = Path.Combine(repositoryDirectory, "tests");
        var productionProjectPaths = FindProjectFiles(sourceDirectory);
        var testProjectPaths = FindProjectFiles(testsDirectory);
        var allProjectPaths = productionProjectPaths
            .Concat(testProjectPaths)
            .ToArray();

        var namesByPath = allProjectPaths.ToDictionary(
            NormalizePath,
            path => Path.GetFileNameWithoutExtension(path)!,
            StringComparer.OrdinalIgnoreCase);
        var projects = allProjectPaths
            .Select(path => ProjectModel.Load(path, namesByPath))
            .ToDictionary(project => project.Name, StringComparer.OrdinalIgnoreCase);
        var productionProjectNames = productionProjectPaths
            .Select(path => Path.GetFileNameWithoutExtension(path)!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var productionProjects = projects
            .Where(pair => productionProjectNames.Contains(pair.Key))
            .ToDictionary(StringComparer.OrdinalIgnoreCase);
        var testProjectNames = testProjectPaths
            .Select(path => Path.GetFileNameWithoutExtension(path)!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var testProjects = projects
            .Where(pair => testProjectNames.Contains(pair.Key))
            .ToDictionary(StringComparer.OrdinalIgnoreCase);
        var solutionProjects = LoadSolutionProjects(
            Path.Combine(repositoryDirectory, "DevForge.sln"),
            repositoryDirectory,
            namesByPath);
        var centralPackages = LoadCentralPackages(
            Path.Combine(repositoryDirectory, "Directory.Packages.props"));

        return new RepositoryModel(
            projects,
            productionProjects,
            testProjects,
            solutionProjects,
            centralPackages.Enabled,
            centralPackages.DeclarationIsSingleAndUnconditional,
            centralPackages.Versions);
    }

    private static string FindRepositoryDirectory(string startDirectory)
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

        throw new DirectoryNotFoundException(
            $"Could not locate DevForge.sln from '{startDirectory}' or any parent directory.");
    }

    private static string[] FindProjectFiles(string directory)
    {
        return SortProjectPaths(
            Directory.GetFiles(directory, "*.csproj", SearchOption.AllDirectories)
                .Where(path => !Path.GetRelativePath(directory, path)
                    .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Any(segment => segment is "bin" or "obj")));
    }

    internal static string[] SortProjectPaths(IEnumerable<string> paths)
    {
        return paths
            .Select(NormalizePath)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static HashSet<string> LoadSolutionProjects(
        string solutionPath,
        string repositoryDirectory,
        Dictionary<string, string> namesByPath)
    {
        var projects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in File.ReadLines(solutionPath))
        {
            if (!line.StartsWith("Project(", StringComparison.Ordinal))
            {
                continue;
            }

            var quotedSegments = line.Split('"');
            if (quotedSegments.Length < 6 ||
                !quotedSegments[5].EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relativePath = quotedSegments[5]
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);
            var normalizedPath = NormalizePath(Path.Combine(repositoryDirectory, relativePath));
            if (!namesByPath.TryGetValue(normalizedPath, out var projectName))
            {
                throw new InvalidDataException(
                    $"Solution project '{quotedSegments[3]}' references unresolved path '{normalizedPath}'.");
            }

            projects.Add(projectName);
        }

        return projects;
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static (
        bool Enabled,
        bool DeclarationIsSingleAndUnconditional,
        CentralPackageVersion[] Versions) LoadCentralPackages(string path)
    {
        if (!File.Exists(path))
        {
            return (false, false, []);
        }

        var document = XDocument.Load(path);
        var declarations = document.Descendants()
            .Where(element => element.Name.LocalName.Equals(
                "ManagePackageVersionsCentrally",
                StringComparison.Ordinal))
            .ToArray();
        var declarationIsSingleAndUnconditional =
            declarations.Length == 1 &&
            declarations[0].Attribute("Condition") is null &&
            !declarations[0].Ancestors().Any(ancestor => ancestor.Attribute("Condition") is not null);
        var enabled =
            declarations.Length == 1 &&
            declarations[0].Value.Equals("true", StringComparison.OrdinalIgnoreCase);
        var versions = document.Descendants()
            .Where(element => element.Name.LocalName.Equals("PackageVersion", StringComparison.Ordinal))
            .Where(element => element.Attribute("Include") is not null)
            .Select(element => new CentralPackageVersion(
                element.Attribute("Include")!.Value,
                element.Attribute("Version")?.Value ??
                element.Elements().FirstOrDefault(child =>
                    child.Name.LocalName.Equals("Version", StringComparison.Ordinal))?.Value ??
                string.Empty))
            .ToArray();

        return (enabled, declarationIsSingleAndUnconditional, versions);
    }
}

internal sealed record CentralPackageVersion(string Name, string Version);

internal sealed class ProjectModel
{
    private ProjectModel(
        string name,
        string targetFramework,
        bool useWpf,
        IReadOnlySet<string> projectReferences,
        IReadOnlyList<string> locallyVersionedPackages,
        IReadOnlySet<string> packageReferences,
        bool hasLockFile)
    {
        Name = name;
        TargetFramework = targetFramework;
        UseWpf = useWpf;
        ProjectReferences = projectReferences;
        LocallyVersionedPackages = locallyVersionedPackages;
        PackageReferences = packageReferences;
        HasLockFile = hasLockFile;
    }

    public string Name { get; }

    public string TargetFramework { get; }

    public bool UseWpf { get; }

    public IReadOnlySet<string> ProjectReferences { get; }

    public IReadOnlyList<string> LocallyVersionedPackages { get; }

    public IReadOnlySet<string> PackageReferences { get; }

    public bool HasLockFile { get; }

    public static ProjectModel Load(
        string projectPath,
        Dictionary<string, string> namesByPath)
    {
        var document = XDocument.Load(projectPath);
        var projectName = Path.GetFileNameWithoutExtension(projectPath);
        var projectDirectory = Path.GetDirectoryName(projectPath)
            ?? throw new InvalidOperationException($"Project path has no directory: {projectPath}");
        var projectReferences = ElementsNamed(document, "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => ResolveProjectName(projectName, projectDirectory, include!, namesByPath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var locallyVersionedPackages = ElementsNamed(document, "PackageReference")
            .Where(DeclaresVersion)
            .Select(element =>
                element.Attribute("Include")?.Value ??
                element.Attribute("Update")?.Value ??
                "<unknown package>")
            .ToArray();
        var packageReferences = ElementsNamed(document, "PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => include!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new ProjectModel(
            projectName,
            ElementsNamed(document, "TargetFramework").Select(element => element.Value).FirstOrDefault() ?? string.Empty,
            ElementsNamed(document, "UseWPF").Any(
                element => element.Value.Equals("true", StringComparison.OrdinalIgnoreCase)),
            projectReferences,
            locallyVersionedPackages,
            packageReferences,
            File.Exists(Path.Combine(projectDirectory, "packages.lock.json")));
    }

    private static IEnumerable<XElement> ElementsNamed(XContainer document, string localName)
    {
        return document.Descendants().Where(
            element => element.Name.LocalName.Equals(localName, StringComparison.Ordinal));
    }

    private static bool DeclaresVersion(XElement packageReference)
    {
        return packageReference.Attribute("Version") is not null ||
               packageReference.Attribute("VersionOverride") is not null ||
               packageReference.Elements().Any(
                   element =>
                       element.Name.LocalName.Equals("Version", StringComparison.Ordinal) ||
                       element.Name.LocalName.Equals("VersionOverride", StringComparison.Ordinal));
    }

    private static string ResolveProjectName(
        string projectName,
        string projectDirectory,
        string referencePath,
        Dictionary<string, string> namesByPath)
    {
        var platformPath = referencePath
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        var normalizedPath = Path.GetFullPath(Path.Combine(projectDirectory, platformPath))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (!namesByPath.TryGetValue(normalizedPath, out var referencedProjectName))
        {
            throw new InvalidDataException(
                $"Project '{projectName}' references unresolved path '{normalizedPath}'.");
        }

        return referencedProjectName;
    }
}
