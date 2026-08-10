namespace DevForge.UnitTests.Architecture;

public sealed class PersistenceDependencyTests
{
    private static readonly IReadOnlyDictionary<string, string> _persistencePackages =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Microsoft.Data.Sqlite"] = "10.0.10",
            ["Microsoft.EntityFrameworkCore.Design"] = "10.0.10",
            ["Microsoft.EntityFrameworkCore.Sqlite"] = "10.0.10",
            ["SQLitePCLRaw.lib.e_sqlite3"] = "2.1.12",
        };

    private readonly RepositoryModel _repository = RepositoryModel.LoadFrom(AppContext.BaseDirectory);

    [Fact]
    public void EfCoreAndSqliteRemainInsideThePersistenceBoundary()
    {
        var violations = FindViolations(_repository);

        Assert.True(
            violations.Length == 0,
            $"Persistence dependency violations:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    private static string[] FindViolations(RepositoryModel repository)
    {
        var violations = new List<string>();
        var centralVersions = repository.CentralPackageVersions.ToDictionary(
            package => package.Name,
            package => package.Version,
            StringComparer.OrdinalIgnoreCase);

        foreach (var (packageName, expectedVersion) in _persistencePackages)
        {
            if (!centralVersions.TryGetValue(packageName, out var version))
            {
                violations.Add($"Central package '{packageName}' is missing.");
            }
            else if (!string.Equals(version, expectedVersion, StringComparison.Ordinal))
            {
                violations.Add(
                    $"Central package '{packageName}' must be {expectedVersion}, but is {version}.");
            }
        }

        foreach (var project in repository.Projects.Values)
        {
            foreach (var packageName in project.PackageReferences.Where(IsPersistencePackage))
            {
                var isAllowed = project.Name.Equals(
                        "DevForge.Infrastructure",
                        StringComparison.OrdinalIgnoreCase)
                    || project.Name.Equals(
                        "DevForge.IntegrationTests",
                        StringComparison.OrdinalIgnoreCase);
                if (!isAllowed)
                {
                    violations.Add(
                        $"{project.Name} must not reference persistence package '{packageName}'.");
                }
            }
        }

        if (!repository.Projects["DevForge.Infrastructure"].PackageReferences.Contains(
                "Microsoft.EntityFrameworkCore.Sqlite"))
        {
            violations.Add("DevForge.Infrastructure must reference Microsoft.EntityFrameworkCore.Sqlite.");
        }

        if (!repository.Projects["DevForge.Infrastructure"].PackageReferences.Contains(
                "Microsoft.EntityFrameworkCore.Design"))
        {
            violations.Add("DevForge.Infrastructure must reference Microsoft.EntityFrameworkCore.Design.");
        }

        if (!repository.Projects["DevForge.Infrastructure"].PackageReferences.Contains(
                "SQLitePCLRaw.lib.e_sqlite3"))
        {
            violations.Add("DevForge.Infrastructure must pin SQLitePCLRaw.lib.e_sqlite3.");
        }

        if (!repository.Projects["DevForge.IntegrationTests"].PackageReferences.Contains(
                "Microsoft.Data.Sqlite"))
        {
            violations.Add("DevForge.IntegrationTests must reference Microsoft.Data.Sqlite.");
        }

        return violations.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool IsPersistencePackage(string packageName)
    {
        return packageName.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase)
            || packageName.StartsWith("Microsoft.Data.Sqlite", StringComparison.OrdinalIgnoreCase)
            || packageName.StartsWith("SQLitePCLRaw", StringComparison.OrdinalIgnoreCase);
    }
}
