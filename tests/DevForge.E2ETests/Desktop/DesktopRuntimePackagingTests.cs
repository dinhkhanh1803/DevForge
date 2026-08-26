using System.IO;
using System.Reflection;
using System.Windows.Resources;

namespace DevForge.E2ETests.Desktop;

public sealed class DesktopRuntimePackagingTests
{
    [Fact]
    public void BuiltInBlueprintPayloadDoesNotShadowDesktopApplicationResources()
    {
        var contentPaths = typeof(DevForge.Desktop.App).Assembly
            .GetCustomAttributes<AssemblyAssociatedContentFileAttribute>()
            .Select(attribute => attribute.RelativeContentFilePath)
            .ToArray();

        Assert.DoesNotContain("app.xaml", contentPaths, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("app.xaml.cs", contentPaths, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadOnlySettingsIndicatorsUseOneWayBindings()
    {
        var xaml = await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "DevForge.Desktop",
            "Settings",
            "SettingsView.xaml"));

        foreach (var property in new[]
                 {
                     "HasProjectRoot",
                     "HasIdeSelection",
                     "HasTeamProfile",
                     "HasLanguage",
                 })
        {
            Assert.Contains(
                $"IsChecked=\"{{Binding {property}, Mode=OneWay}}\"",
                xaml,
                StringComparison.Ordinal);
        }
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "DevForge.sln")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
