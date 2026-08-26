using System.IO;

namespace DevForge.E2ETests.Release;

public sealed class ReleaseDocumentationContractTests
{
    private static readonly string[] _guides =
    [
        "docs/user-guide.md",
        "docs/maintainer-guide.md",
        "docs/blueprint-author-guide.md",
        "docs/troubleshooting.md",
        "docs/privacy-and-support-bundles.md",
    ];

    [Fact]
    public void ReleaseChecklistHasEveryMustGateAndExactEvidenceColumns()
    {
        var checklist = Read("docs/release-checklist.md");
        string[] gates =
        [
            "Build",
            "Recovery",
            "Security",
            "Blueprints",
            "UX",
            "Data",
            "Documentation",
            "Packaging",
        ];

        Assert.Contains(
            "| Gate | Requirement | Status | Evidence | Host | Timestamp | Blocker |",
            checklist,
            StringComparison.Ordinal);
        Assert.All(gates, gate => Assert.Equal(1, Count(checklist, $"| {gate} |")));
        Assert.Contains("Windows 10.0.19045 x64", checklist, StringComparison.Ordinal);
        Assert.Contains("Pending", checklist, StringComparison.Ordinal);
        Assert.Contains("Windows 11", checklist, StringComparison.Ordinal);
        Assert.DoesNotContain("TODO", checklist, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TBD", checklist, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("release ready", checklist, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RequiredGuidesCoverObservedProductAndSupportBoundaries()
    {
        Assert.All(_guides, path => Assert.True(File.Exists(PathAt(path)), path));
        var combined = string.Join('\n', _guides.Select(Read));
        string[] requiredTopics =
        [
            "Install",
            "First run",
            "Create Project",
            "Recovery",
            "GitHub",
            "Blueprint trust",
            "checksums.json",
            "Support bundle",
            "Privacy",
            "DF-FS-001",
            "DF-SUPPORT-001",
        ];

        Assert.All(requiredTopics, topic =>
            Assert.Contains(topic, combined, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("automatic updater", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("telemetry dashboard", combined, StringComparison.OrdinalIgnoreCase);
    }

    private static int Count(string value, string fragment)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(fragment, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += fragment.Length;
        }

        return count;
    }

    private static string Read(string relativePath) => File.ReadAllText(PathAt(relativePath));

    private static string PathAt(string relativePath) =>
        Path.Combine(FindRepositoryRoot(), relativePath);

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "DevForge.sln")))
        {
            current = current.Parent;
        }

        return current?.FullName
            ?? throw new InvalidOperationException("Repository root not found.");
    }
}
