namespace DevForge.UnitTests.Architecture;

public sealed class RepositoryModelRegressionTests
{
    [Fact]
    public void MissingProjectReferencePathIsRejected()
    {
        using var fixture = RepositoryFixture.Create();
        var missingReference = Path.Combine(
            fixture.RootDirectory,
            "src",
            "DevForge.Domain",
            "DevForge.Domain.csproj");
        fixture.WriteProject(
            "src/DevForge.Application/DevForge.Application.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="../DevForge.Domain/DevForge.Domain.csproj" />
              </ItemGroup>
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var exception = Assert.Throws<InvalidDataException>(
            () => RepositoryModel.LoadFrom(fixture.RootDirectory));

        Assert.Contains("DevForge.Application", exception.Message, StringComparison.Ordinal);
        Assert.Contains(Path.GetFullPath(missingReference), exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingSolutionProjectPathIsRejected()
    {
        using var fixture = RepositoryFixture.Create();
        fixture.WriteProject(
            "src/DevForge.Domain/DevForge.Domain.csproj",
            MinimalProject);
        var missingProject = Path.Combine(
            fixture.RootDirectory,
            "src",
            "Missing",
            "DevForge.Domain.csproj");
        fixture.WriteSolution(
            """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{00000000-0000-0000-0000-000000000000}") = "DevForge.Domain", "src\Missing\DevForge.Domain.csproj", "{00000000-0000-0000-0000-000000000001}"
            EndProject
            Global
            EndGlobal
            """);

        var exception = Assert.Throws<InvalidDataException>(
            () => RepositoryModel.LoadFrom(fixture.RootDirectory));

        Assert.Contains(Path.GetFullPath(missingProject), exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("""<PackageReference Include="Example" VersionOverride="1.2.3" />""")]
    [InlineData("""<PackageReference Include="Example"><VersionOverride>1.2.3</VersionOverride></PackageReference>""")]
    public void VersionOverrideIsReportedAsALocalPackageVersion(string packageReference)
    {
        using var fixture = RepositoryFixture.Create();
        fixture.WriteProject(
            "src/DevForge.Domain/DevForge.Domain.csproj",
            $"""
             <Project Sdk="Microsoft.NET.Sdk">
               <PropertyGroup>
                 <TargetFramework>net10.0</TargetFramework>
               </PropertyGroup>
               <ItemGroup>
                 {packageReference}
               </ItemGroup>
             </Project>
             """);

        var repository = RepositoryModel.LoadFrom(fixture.RootDirectory);

        Assert.Contains(
            "Example",
            repository.ProductionProjects["DevForge.Domain"].LocallyVersionedPackages);
    }

    [Fact]
    public void ProjectDiscoveryIsSortedByNormalizedPath()
    {
        using var fixture = RepositoryFixture.Create();
        fixture.WriteProject("src/Z.Project/Z.Project.csproj", MinimalProject);
        fixture.WriteProject("src/A.Project/A.Project.csproj", MinimalProject);

        var repository = RepositoryModel.LoadFrom(fixture.RootDirectory);

        Assert.Equal(["A.Project", "Z.Project"], repository.ProductionProjects.Keys);
    }

    [Fact]
    public void ProjectDiscoveryIgnoresTransientWpfCompilerProjects()
    {
        using var fixture = RepositoryFixture.Create();
        fixture.WriteProject("src/DevForge.Desktop/DevForge.Desktop.csproj", MinimalProject);
        fixture.WriteProject(
            "src/DevForge.Desktop/DevForge.Desktop_fixture_wpftmp.csproj",
            MinimalProject);

        var repository = RepositoryModel.LoadFrom(fixture.RootDirectory);

        Assert.Equal(["DevForge.Desktop"], repository.ProductionProjects.Keys);
    }

    [Fact]
    public void ProjectPathSortingNormalizesAndUsesWindowsCaseInsensitiveOrder()
    {
        using var fixture = RepositoryFixture.Create();
        var laterPath = Path.Combine(fixture.RootDirectory, "src", "z.Project", "..", "z.Project.csproj");
        var earlierPath = Path.Combine(fixture.RootDirectory, "src", "A.Project.csproj");

        var sortedPaths = RepositoryModel.SortProjectPaths([laterPath, earlierPath]);

        Assert.Equal(
            [Path.GetFullPath(earlierPath), Path.GetFullPath(laterPath)],
            sortedPaths);
    }

    [Fact]
    public void ProjectReferenceDifferencesAreSorted()
    {
        using var fixture = RepositoryFixture.Create();
        fixture.WriteProject("src/A.Actual/A.Actual.csproj", MinimalProject);
        fixture.WriteProject("src/Z.Expected/Z.Expected.csproj", MinimalProject);
        fixture.WriteProject(
            "src/DevForge.Root/DevForge.Root.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="../A.Actual/A.Actual.csproj" />
              </ItemGroup>
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        var repository = RepositoryModel.LoadFrom(fixture.RootDirectory);
        IReadOnlyDictionary<string, string[]> allowlist =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["A.Actual"] = [],
                ["DevForge.Root"] = ["Z.Expected"],
                ["Z.Expected"] = [],
            };

        var differences = ArchitectureDiagnostics.ProjectReferenceDifferences(
            repository.ProductionProjects,
            allowlist);

        Assert.Equal(
            [
                "DevForge.Root -> A.Actual (unexpected)",
                "DevForge.Root -> Z.Expected (missing)",
            ],
            differences);
    }

    [Fact]
    public void FrameworkAndWpfViolationsAreAccumulatedAndSorted()
    {
        using var fixture = RepositoryFixture.Create();
        fixture.WriteProject(
            "src/Z.Bad/Z.Bad.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
                <UseWPF>true</UseWPF>
              </PropertyGroup>
            </Project>
            """);
        fixture.WriteProject(
            "src/A.Bad/A.Bad.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        var repository = RepositoryModel.LoadFrom(fixture.RootDirectory);

        var violations = ArchitectureDiagnostics.FrameworkAndWpfViolations(
            repository.ProductionProjects);

        Assert.Equal(
            [
                "A.Bad must target net10.0, but targets net8.0.",
                "Z.Bad must not enable WPF.",
                "Z.Bad must target net10.0, but targets net9.0.",
            ],
            violations);
    }

    [Fact]
    public void UnexpectedSolutionProjectIsReported()
    {
        using var fixture = RepositoryFixture.Create();
        fixture.WriteProject("src/Unexpected/Unexpected.csproj", MinimalProject);
        fixture.WriteSolution(
            """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{00000000-0000-0000-0000-000000000000}") = "Unexpected", "src\Unexpected\Unexpected.csproj", "{00000000-0000-0000-0000-000000000001}"
            EndProject
            Global
            EndGlobal
            """);
        var repository = RepositoryModel.LoadFrom(fixture.RootDirectory);

        var differences = ArchitectureDiagnostics.ProjectSetDifferences(
            repository.SolutionProjects,
            [],
            "solution");

        Assert.Equal(["Unexpected (unexpected solution project)"], differences);
    }

    [Fact]
    public void UnexpectedDiscoveredTestProjectIsReportedRegardlessOfName()
    {
        using var fixture = RepositoryFixture.Create();
        fixture.WriteProject("tests/Helper/Helper.csproj", MinimalProject);
        var repository = RepositoryModel.LoadFrom(fixture.RootDirectory);

        var differences = ArchitectureDiagnostics.ProjectReferenceDifferences(
            repository.TestProjects,
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase));

        Assert.Equal(["Helper (unexpected project)"], differences);
    }

    [Fact]
    public void SharedDiagnosticsUseTheRequestedProjectScope()
    {
        var setDifferences = ArchitectureDiagnostics.ProjectSetDifferences(
            new HashSet<string>(["Unexpected"], StringComparer.OrdinalIgnoreCase),
            ["Missing"],
            "test");
        var graphDifferences = ArchitectureDiagnostics.ProjectReferenceDifferences(
            new Dictionary<string, ProjectModel>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["Missing"] = [],
            },
            "test");

        Assert.Equal(
            [
                "Missing (missing test project)",
                "Unexpected (unexpected test project)",
            ],
            setDifferences);
        Assert.Equal(["Missing (missing test project)"], graphDifferences);
    }

    private const string MinimalProject =
        """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>
        </Project>
        """;

    private sealed class RepositoryFixture : IDisposable
    {
        private RepositoryFixture(string rootDirectory)
        {
            RootDirectory = rootDirectory;
            Directory.CreateDirectory(Path.Combine(rootDirectory, "src"));
            Directory.CreateDirectory(Path.Combine(rootDirectory, "tests"));
            WriteSolution(
                """
                Microsoft Visual Studio Solution File, Format Version 12.00
                Global
                EndGlobal
                """);
        }

        public string RootDirectory { get; }

        public static RepositoryFixture Create()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "DevForge.UnitTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return new RepositoryFixture(directory);
        }

        public void WriteProject(string relativePath, string contents)
        {
            WriteFile(relativePath, contents);
        }

        public void WriteSolution(string contents)
        {
            WriteFile("DevForge.sln", contents);
        }

        public void Dispose()
        {
            Directory.Delete(RootDirectory, recursive: true);
        }

        private void WriteFile(string relativePath, string contents)
        {
            var path = Path.Combine(
                RootDirectory,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents);
        }
    }
}
