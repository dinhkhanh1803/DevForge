namespace DevForge.UnitTests.Architecture;

public sealed class CentralPackagePolicyRegressionTests
{
    [Fact]
    public void DisabledCentralPackageManagementIsReported()
    {
        using var fixture = PackagePolicyFixture.Create();
        fixture.WriteCentralPackages(
            """
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
              </PropertyGroup>
              <ItemGroup>
                <PackageVersion Include="Example" Version="1.2.3" />
              </ItemGroup>
            </Project>
            """);

        var violations = LoadViolations(fixture);

        Assert.Contains(
            "Directory.Packages.props must set ManagePackageVersionsCentrally to true.",
            violations);
    }

    [Fact]
    public void DuplicateCentralPackageNameIsReported()
    {
        using var fixture = PackagePolicyFixture.Create();
        fixture.WriteCentralPackages(
            """
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
              </PropertyGroup>
              <ItemGroup>
                <PackageVersion Include="Example" Version="1.2.3" />
                <PackageVersion Include="example" Version="1.2.4" />
              </ItemGroup>
            </Project>
            """);

        var violations = LoadViolations(fixture);

        Assert.Contains("Central PackageVersion 'Example' is declared more than once.", violations);
    }

    [Fact]
    public void DuplicateCentralPackageManagementDeclarationsAreReported()
    {
        using var fixture = PackagePolicyFixture.Create();
        fixture.WriteCentralPackages(
            """
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
                <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
              </PropertyGroup>
              <ItemGroup>
                <PackageVersion Include="Example" Version="1.2.3" />
              </ItemGroup>
            </Project>
            """);

        var violations = LoadViolations(fixture);

        Assert.Contains(
            "Directory.Packages.props must declare ManagePackageVersionsCentrally exactly once and unconditionally.",
            violations);
    }

    [Fact]
    public void ConditionalCentralPackageManagementDeclarationIsReported()
    {
        using var fixture = PackagePolicyFixture.Create();
        fixture.WriteCentralPackages(
            """
            <Project>
              <PropertyGroup Condition="'$(Configuration)' == 'Release'">
                <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
              </PropertyGroup>
              <ItemGroup>
                <PackageVersion Include="Example" Version="1.2.3" />
              </ItemGroup>
            </Project>
            """);

        var violations = LoadViolations(fixture);

        Assert.Contains(
            "Directory.Packages.props must declare ManagePackageVersionsCentrally exactly once and unconditionally.",
            violations);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("latest")]
    [InlineData("[1.0.0,2.0.0)")]
    public void NonExactCentralPackageVersionIsReported(string version)
    {
        using var fixture = PackagePolicyFixture.Create();
        fixture.WriteCentralPackages(EnabledCentralPackages("Example", version));

        var violations = LoadViolations(fixture);

        Assert.Contains(
            $"Central PackageVersion 'Example' must use an exact version, but uses '{version}'.",
            violations);
    }

    [Theory]
    [InlineData("1.2.3")]
    [InlineData("1.2.3-beta.1+build.5")]
    public void ExactStableAndPrereleaseVersionsAreAccepted(string version)
    {
        using var fixture = PackagePolicyFixture.Create();
        fixture.WriteCentralPackages(EnabledCentralPackages("Example", version));

        var violations = LoadViolations(fixture);

        Assert.Empty(violations);
    }

    [Fact]
    public void ExactVersionInChildMetadataIsAccepted()
    {
        using var fixture = PackagePolicyFixture.Create();
        fixture.WriteCentralPackages(
            """
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
              </PropertyGroup>
              <ItemGroup>
                <PackageVersion Include="Example">
                  <Version>1.2.3-beta.1</Version>
                </PackageVersion>
              </ItemGroup>
            </Project>
            """);

        var violations = LoadViolations(fixture);

        Assert.Empty(violations);
    }

    [Fact]
    public void PackageReferenceWithoutCentralVersionIsReported()
    {
        using var fixture = PackagePolicyFixture.Create();
        fixture.WriteCentralPackages(EnabledCentralPackages("Other", "1.2.3"));

        var violations = LoadViolations(fixture);

        Assert.Contains(
            "Fixture.Project: PackageReference 'Example' has no central PackageVersion.",
            violations);
    }

    [Fact]
    public void MissingLockFileIsReported()
    {
        using var fixture = PackagePolicyFixture.Create(createLockFile: false);

        var violations = LoadViolations(fixture);

        Assert.Contains("Fixture.Project is missing packages.lock.json.", violations);
    }

    private static string[] LoadViolations(PackagePolicyFixture fixture)
    {
        var repository = RepositoryModel.LoadFrom(fixture.RootDirectory);
        return ArchitectureDiagnostics.CentralPackagePolicyViolations(repository);
    }

    private static string EnabledCentralPackages(string packageName, string version) =>
        $"""
         <Project>
           <PropertyGroup>
             <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
           </PropertyGroup>
           <ItemGroup>
             <PackageVersion Include="{packageName}" Version="{version}" />
           </ItemGroup>
         </Project>
         """;

    private sealed class PackagePolicyFixture : IDisposable
    {
        private PackagePolicyFixture(string rootDirectory, bool createLockFile)
        {
            RootDirectory = rootDirectory;
            Directory.CreateDirectory(Path.Combine(rootDirectory, "src", "Fixture.Project"));
            Directory.CreateDirectory(Path.Combine(rootDirectory, "tests"));
            File.WriteAllText(Path.Combine(rootDirectory, "DevForge.sln"), EmptySolution);
            File.WriteAllText(
                Path.Combine(rootDirectory, "src", "Fixture.Project", "Fixture.Project.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="Example" />
                  </ItemGroup>
                </Project>
                """);
            WriteCentralPackages(EnabledCentralPackages("Example", "1.2.3"));

            if (createLockFile)
            {
                File.WriteAllText(
                    Path.Combine(rootDirectory, "src", "Fixture.Project", "packages.lock.json"),
                    "{}");
            }
        }

        public string RootDirectory { get; }

        public static PackagePolicyFixture Create(bool createLockFile = true)
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "DevForge.UnitTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return new PackagePolicyFixture(directory, createLockFile);
        }

        public void WriteCentralPackages(string contents)
        {
            File.WriteAllText(Path.Combine(RootDirectory, "Directory.Packages.props"), contents);
        }

        public void Dispose()
        {
            Directory.Delete(RootDirectory, recursive: true);
        }

        private const string EmptySolution =
            """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Global
            EndGlobal
            """;
    }
}
