using System.IO;
using System.Xml.Linq;

namespace DevForge.E2ETests.Desktop;

public sealed class DesktopAccessibilityTests
{
    [Theory]
    [InlineData("src/DevForge.Desktop/Execution/ExecutionCenterView.xaml")]
    [InlineData("src/DevForge.Desktop/RunHistory/RunHistoryView.xaml")]
    [InlineData("src/DevForge.Desktop/Settings/SettingsView.xaml")]
    public void HardenedDesktopViewsNameEveryActionableButton(string relativePath)
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), relativePath));
        var unnamed = document.Descendants()
            .Where(element => element.Name.LocalName == "Button")
            .Where(element => element.Attributes().All(attribute =>
                attribute.Name.LocalName != "AutomationProperties.Name"))
            .ToArray();

        Assert.Empty(unnamed);
    }

    [Fact]
    public void DiagnosticsViewsKeepScalingAndReadOnlyStatusContracts()
    {
        var root = FindRepositoryRoot();
        var execution = File.ReadAllText(Path.Combine(
            root,
            "src/DevForge.Desktop/Execution/ExecutionCenterView.xaml"));
        var history = File.ReadAllText(Path.Combine(
            root,
            "src/DevForge.Desktop/RunHistory/RunHistoryView.xaml"));
        var settings = File.ReadAllText(Path.Combine(
            root,
            "src/DevForge.Desktop/Settings/SettingsView.xaml"));

        Assert.Contains("VirtualizingStackPanel.IsVirtualizing=\"True\"", execution, StringComparison.Ordinal);
        Assert.Contains("VirtualizingStackPanel.VirtualizationMode=\"Recycling\"", execution, StringComparison.Ordinal);
        Assert.Contains("Status.Label, Mode=OneWay", execution, StringComparison.Ordinal);
        Assert.Contains("SelectedStep, Mode=OneWay", execution, StringComparison.Ordinal);
        Assert.Contains("StatusLabel, Mode=OneWay", history, StringComparison.Ordinal);
        Assert.Contains("DiagnosticRetentionDays", settings, StringComparison.Ordinal);
        Assert.Contains("DiagnosticRetentionMaxBytes", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("<ScrollViewer Height=", execution, StringComparison.Ordinal);
        Assert.DoesNotContain("<ScrollViewer Height=", history, StringComparison.Ordinal);
        Assert.DoesNotContain("<ScrollViewer Height=", settings, StringComparison.Ordinal);
    }

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
