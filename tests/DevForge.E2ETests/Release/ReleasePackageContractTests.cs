using System.IO;
using System.Xml.Linq;
using DevForge.Desktop.Bootstrap;

namespace DevForge.E2ETests.Release;

public sealed class ReleasePackageContractTests
{
    [Fact]
    public void PublishProfilePinsTheSupportedSelfContainedShapeAndVersion()
    {
        var profile = XDocument.Load(PathAt(
            "src/DevForge.Desktop/Properties/PublishProfiles/ReleaseWinX64.pubxml"));
        var buildPolicy = XDocument.Load(PathAt("Directory.Build.props"));

        Assert.Equal("win-x64", Value(profile, "RuntimeIdentifier"));
        Assert.Equal("win-x64", Value(buildPolicy, "RuntimeIdentifiers"));
        Assert.Equal("true", Value(profile, "SelfContained"));
        Assert.Equal("false", Value(profile, "PublishSingleFile"));
        Assert.Equal("false", Value(profile, "PublishReadyToRun"));
        Assert.Equal("embedded", Value(profile, "DebugType"));
        Assert.Equal("1.0.0", Value(profile, "Version"));
        Assert.Equal("1.0.0.0", Value(profile, "AssemblyVersion"));
        Assert.Equal("1.0.0.0", Value(profile, "FileVersion"));
    }

    [Fact]
    public void PackageContentIsBoundedToTheThreeReviewedBlueprintRoots()
    {
        var desktop = File.ReadAllText(PathAt("src/DevForge.Desktop/DevForge.Desktop.csproj"));
        var builtIn = File.ReadAllText(PathAt(
            "src/DevForge.Blueprints.BuiltIn/DevForge.Blueprints.BuiltIn.csproj"));

        Assert.DoesNotContain("blueprints\\**\\*", desktop, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("blueprints\\**\\*", builtIn, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, Count(builtIn, "blueprints\\README.md"));
        Assert.Equal(1, Count(builtIn, "desktop.csharp-wpf-tool\\**\\*"));
        Assert.Equal(1, Count(builtIn, "web.react-vite-ts\\**\\*"));
        Assert.Equal(1, Count(builtIn, "tool.python-cli\\**\\*"));
    }

    [Fact]
    public void ReleaseAutomationUsesTheFixedProfileAndAuditsBeforeUpload()
    {
        var workflow = File.ReadAllText(PathAt(".github/workflows/ci.yml"));
        var audit = File.ReadAllText(PathAt("scripts/Test-ReleasePackage.ps1"));

        Assert.Contains("release-package:", workflow, StringComparison.Ordinal);
        Assert.Contains("ReleaseWinX64", workflow, StringComparison.Ordinal);
        Assert.Contains("Test-ReleasePackage.ps1", workflow, StringComparison.Ordinal);
        Assert.True(
            workflow.IndexOf("Test-ReleasePackage.ps1", StringComparison.Ordinal)
            < workflow.IndexOf("name: release-win-x64", StringComparison.Ordinal));
        Assert.Contains("support-bundles", audit, StringComparison.Ordinal);
        Assert.Contains(".sqlite", audit, StringComparison.Ordinal);
        Assert.Contains("coreclr.dll", audit, StringComparison.Ordinal);
        Assert.Contains("docs\\user-guide.md", audit, StringComparison.Ordinal);
        Assert.Contains("docs\\release-checklist.md", audit, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalDataOverrideRequiresOneExactAbsoluteArgumentPair()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "DevForge-ReleasePackageTests",
            "isolated");

        Assert.Equal(root, DesktopLocalDataRootResolver.Resolve(["--local-data-root", root]));
        Assert.Throws<ArgumentException>(() =>
            DesktopLocalDataRootResolver.Resolve(["--local-data-root", "relative"]));
        Assert.Throws<ArgumentException>(() =>
            DesktopLocalDataRootResolver.Resolve([
                "--local-data-root",
                Path.GetPathRoot(root) ?? "C:\\"]));
        Assert.Throws<ArgumentException>(() =>
            DesktopLocalDataRootResolver.Resolve(["--local-data-root", root, "extra"]));
    }

    private static string Value(XDocument document, string name) =>
        Assert.Single(document.Descendants(name)).Value;

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
