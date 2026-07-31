namespace DevForge.UnitTests.Architecture;

public sealed class ProjectDependencyTests
{
    private static readonly string[] _expectedProductionProjects =
    [
        "DevForge.Application",
        "DevForge.Blueprints.Abstractions",
        "DevForge.Blueprints.BuiltIn",
        "DevForge.Cli",
        "DevForge.Desktop",
        "DevForge.Domain",
        "DevForge.Infrastructure",
    ];

    private static readonly string[] _expectedSolutionProjects =
    [
        .. _expectedProductionProjects,
        "DevForge.BlueprintTests",
        "DevForge.E2ETests",
        "DevForge.IntegrationTests",
        "DevForge.UnitTests",
    ];

    private static readonly string[] _expectedTestProjects =
    [
        "DevForge.BlueprintTests",
        "DevForge.E2ETests",
        "DevForge.IntegrationTests",
        "DevForge.UnitTests",
    ];

    private static readonly IReadOnlyDictionary<string, string[]> _allowedProjectReferences =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["DevForge.Application"] =
            [
                "DevForge.Blueprints.Abstractions",
                "DevForge.Domain",
            ],
            ["DevForge.Blueprints.Abstractions"] = [],
            ["DevForge.Blueprints.BuiltIn"] = ["DevForge.Blueprints.Abstractions"],
            ["DevForge.Cli"] =
            [
                "DevForge.Application",
                "DevForge.Infrastructure",
            ],
            ["DevForge.Desktop"] =
            [
                "DevForge.Application",
                "DevForge.Infrastructure",
            ],
            ["DevForge.Domain"] = [],
            ["DevForge.Infrastructure"] =
            [
                "DevForge.Application",
                "DevForge.Blueprints.Abstractions",
                "DevForge.Domain",
            ],
        };

    private static readonly IReadOnlyDictionary<string, string[]> _allowedTestProjectReferences =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["DevForge.UnitTests"] =
            [
                "DevForge.Application",
                "DevForge.Blueprints.Abstractions",
                "DevForge.Domain",
            ],
            ["DevForge.IntegrationTests"] = ["DevForge.Infrastructure"],
            ["DevForge.BlueprintTests"] =
            [
                "DevForge.Blueprints.Abstractions",
                "DevForge.Blueprints.BuiltIn",
            ],
            ["DevForge.E2ETests"] =
            [
                "DevForge.Application",
                "DevForge.Infrastructure",
            ],
        };

    private readonly RepositoryModel _repository = RepositoryModel.LoadFrom(AppContext.BaseDirectory);

    [Fact]
    public void RepositoryContainsExactlyTheApprovedProductionProjects()
    {
        var actual = _repository.ProductionProjects.Keys.ToArray();
        var missing = _expectedProductionProjects.Except(actual, StringComparer.OrdinalIgnoreCase);
        var unexpected = actual.Except(_expectedProductionProjects, StringComparer.OrdinalIgnoreCase);
        var differences = missing.Select(project => $"{project} (missing production project)")
            .Concat(unexpected.Select(project => $"{project} (unexpected production project)"))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(
            differences.Length == 0,
            $"Production project set differs:{Environment.NewLine}{string.Join(Environment.NewLine, differences)}");
    }

    [Fact]
    public void ProductionProjectReferencesMatchTheApprovedGraph()
    {
        var differences = ArchitectureDiagnostics.ProjectReferenceDifferences(
            _repository.ProductionProjects,
            _allowedProjectReferences,
            "production");

        Assert.True(
            differences.Length == 0,
            $"Production project-reference graph differs:{Environment.NewLine}{string.Join(Environment.NewLine, differences)}");
    }

    [Fact]
    public void DesktopIsTheOnlyWpfProjectAndTargetsTheWindowsFramework()
    {
        var violations = ArchitectureDiagnostics.FrameworkAndWpfViolations(
            _repository.ProductionProjects);

        Assert.True(
            violations.Length == 0,
            $"Framework/WPF violations:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    [Fact]
    public void TestProjectReferencesMatchTheApprovedGraph()
    {
        var differences = ArchitectureDiagnostics.ProjectReferenceDifferences(
            _repository.TestProjects,
            _allowedTestProjectReferences,
            "test");

        Assert.True(
            differences.Length == 0,
            $"Test project-reference graph differs:{Environment.NewLine}{string.Join(Environment.NewLine, differences)}");
    }

    [Fact]
    public void RepositoryContainsExactlyTheApprovedTestProjects()
    {
        var differences = ArchitectureDiagnostics.ProjectSetDifferences(
            _repository.TestProjects.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            _expectedTestProjects,
            "test");

        Assert.True(
            differences.Length == 0,
            $"Test project set differs:{Environment.NewLine}{string.Join(Environment.NewLine, differences)}");
    }

    [Fact]
    public void SolutionContainsAllRequiredProjects()
    {
        var differences = ArchitectureDiagnostics.ProjectSetDifferences(
            _repository.SolutionProjects,
            _expectedSolutionProjects,
            "solution");

        Assert.True(
            differences.Length == 0,
            $"Solution project set differs:{Environment.NewLine}{string.Join(Environment.NewLine, differences)}");
    }

    [Fact]
    public void PackageReferencesDoNotDeclareVersionsLocally()
    {
        var localVersions = _repository.Projects.Values
            .SelectMany(project => project.LocallyVersionedPackages.Select(
                package => $"{project.Name}: PackageReference {package} declares Version locally"))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(
            localVersions.Length == 0,
            $"Central Package Management violations:{Environment.NewLine}{string.Join(Environment.NewLine, localVersions)}");
    }

    [Fact]
    public void CentralPackageAndLockPolicyIsValid()
    {
        var violations = ArchitectureDiagnostics.CentralPackagePolicyViolations(_repository);

        Assert.True(
            violations.Length == 0,
            $"Central package/lock policy violations:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }
}
