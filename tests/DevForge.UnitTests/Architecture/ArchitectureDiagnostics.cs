namespace DevForge.UnitTests.Architecture;

internal static class ArchitectureDiagnostics
{
    public static string[] CentralPackagePolicyViolations(RepositoryModel repository)
    {
        var violations = new List<string>();
        if (!repository.CentralPackageManagementDeclarationIsSingleAndUnconditional)
        {
            violations.Add(
                "Directory.Packages.props must declare ManagePackageVersionsCentrally exactly once and unconditionally.");
        }

        if (!repository.CentralPackageManagementEnabled)
        {
            violations.Add("Directory.Packages.props must set ManagePackageVersionsCentrally to true.");
        }

        var duplicateNames = repository.CentralPackageVersions
            .GroupBy(package => package.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.First().Name);
        violations.AddRange(duplicateNames.Select(
            name => $"Central PackageVersion '{name}' is declared more than once."));

        violations.AddRange(repository.CentralPackageVersions
            .Where(package => !IsExactNuGetLikeVersion(package.Version))
            .Select(package =>
                $"Central PackageVersion '{package.Name}' must use an exact version, but uses '{package.Version}'."));

        var centrallyVersionedNames = repository.CentralPackageVersions
            .Select(package => package.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        violations.AddRange(repository.Projects.Values.SelectMany(project =>
            project.PackageReferences
                .Where(package => !centrallyVersionedNames.Contains(package))
                .Select(package =>
                    $"{project.Name}: PackageReference '{package}' has no central PackageVersion.")));
        violations.AddRange(repository.Projects.Values
            .Where(project => !project.HasLockFile)
            .Select(project => $"{project.Name} is missing packages.lock.json."));

        return violations.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static string[] ProjectSetDifferences(
        IReadOnlySet<string> actualProjects,
        IEnumerable<string> expectedProjects,
        string? scope = null)
    {
        var projectDescription = DescribeProject(scope);
        var expected = expectedProjects.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return expected
            .Except(actualProjects, StringComparer.OrdinalIgnoreCase)
            .Select(project => $"{project} (missing {projectDescription})")
            .Concat(
                actualProjects
                    .Except(expected, StringComparer.OrdinalIgnoreCase)
                    .Select(project => $"{project} (unexpected {projectDescription})"))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string[] ProjectReferenceDifferences(
        IReadOnlyDictionary<string, ProjectModel> projects,
        IReadOnlyDictionary<string, string[]> allowlist,
        string? scope = null)
    {
        var projectDescription = DescribeProject(scope);
        var differences = new List<string>();

        foreach (var (projectName, expectedReferences) in allowlist)
        {
            if (!projects.TryGetValue(projectName, out var project))
            {
                differences.Add($"{projectName} (missing {projectDescription})");
                continue;
            }

            differences.AddRange(
                expectedReferences
                    .Except(project.ProjectReferences, StringComparer.OrdinalIgnoreCase)
                    .Select(reference => $"{projectName} -> {reference} (missing)"));
            differences.AddRange(
                project.ProjectReferences
                    .Except(expectedReferences, StringComparer.OrdinalIgnoreCase)
                    .Select(reference => $"{projectName} -> {reference} (unexpected)"));
        }

        differences.AddRange(
            projects.Keys
                .Except(allowlist.Keys, StringComparer.OrdinalIgnoreCase)
                .Select(project => $"{project} (unexpected {projectDescription})"));

        return differences.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string DescribeProject(string? scope)
    {
        return string.IsNullOrWhiteSpace(scope) ? "project" : $"{scope} project";
    }

    public static string[] FrameworkAndWpfViolations(
        IReadOnlyDictionary<string, ProjectModel> projects)
    {
        var violations = new List<string>();

        foreach (var (projectName, project) in projects)
        {
            if (projectName.Equals("DevForge.Desktop", StringComparison.OrdinalIgnoreCase))
            {
                if (!project.UseWpf)
                {
                    violations.Add($"{projectName} must set UseWPF to true.");
                }

                if (!project.TargetFramework.Equals("net10.0-windows", StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add(
                        $"{projectName} must target net10.0-windows, but targets {project.TargetFramework}.");
                }
            }
            else
            {
                if (project.UseWpf)
                {
                    violations.Add($"{projectName} must not enable WPF.");
                }

                if (!project.TargetFramework.Equals("net10.0", StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add($"{projectName} must target net10.0, but targets {project.TargetFramework}.");
                }
            }
        }

        return violations.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool IsExactNuGetLikeVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        var metadataParts = version.Split('+');
        if (metadataParts.Length > 2 ||
            (metadataParts.Length == 2 && !IsValidLabelList(metadataParts[1])))
        {
            return false;
        }

        var versionAndPrerelease = metadataParts[0];
        var prereleaseSeparator = versionAndPrerelease.IndexOf('-');
        var numericVersion = prereleaseSeparator < 0
            ? versionAndPrerelease
            : versionAndPrerelease[..prereleaseSeparator];
        if (prereleaseSeparator >= 0 &&
            !IsValidLabelList(versionAndPrerelease[(prereleaseSeparator + 1)..]))
        {
            return false;
        }

        var numericParts = numericVersion.Split('.');
        return numericParts.Length is >= 2 and <= 4 &&
               numericParts.All(part => part.Length > 0 && part.All(char.IsAsciiDigit));
    }

    private static bool IsValidLabelList(string value)
    {
        return value.Split('.').All(
            part => part.Length > 0 && part.All(character =>
                char.IsAsciiLetterOrDigit(character) || character == '-'));
    }
}
