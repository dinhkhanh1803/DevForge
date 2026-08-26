using System.IO;
using System.Text.RegularExpressions;

namespace DevForge.E2ETests.Desktop;

public sealed partial class DesktopPresentationContractTests
{
    private static readonly string[] RequiredSemanticPaletteKeys =
    [
        "Brush.AppBackground",
        "Brush.Navigation",
        "Brush.Surface",
        "Brush.SurfaceRaised",
        "Brush.SurfaceHover",
        "Brush.Border",
        "Brush.BorderStrong",
        "Brush.TextPrimary",
        "Brush.TextSecondary",
        "Brush.TextTertiary",
        "Brush.Accent",
        "Brush.AccentHover",
        "Brush.AccentSubtle",
        "Brush.Info",
        "Brush.InfoSurface",
        "Brush.Success",
        "Brush.SuccessSurface",
        "Brush.Warning",
        "Brush.WarningSurface",
        "Brush.Error",
        "Brush.ErrorSurface",
        "Brush.Focus",
        "Brush.DisabledSurface",
        "Brush.DisabledText",
    ];

    [Fact]
    public void LightAndDarkThemesExposeTheSameSemanticPalette()
    {
        var light = ResourceKeys("src/DevForge.Desktop/Resources/Colors.Light.xaml");
        var dark = ResourceKeys("src/DevForge.Desktop/Resources/Colors.Dark.xaml");

        Assert.Equal(
            light.Order(StringComparer.Ordinal),
            dark.Order(StringComparer.Ordinal));
        Assert.All(RequiredSemanticPaletteKeys, key =>
        {
            Assert.Contains(key, light);
            Assert.Contains(key, dark);
        });
    }

    [Fact]
    public void ApplicationMergesTheCompletePresentationSystemInDependencyOrder()
    {
        var xaml = Read("src/DevForge.Desktop/App.xaml");
        string[] resources =
        [
            "Resources/Tokens.xaml",
            "Resources/Typography.xaml",
            "Resources/Icons.xaml",
            "Resources/Controls.xaml",
            "Resources/Components.xaml",
        ];

        var previous = -1;
        foreach (var resource in resources)
        {
            var index = xaml.IndexOf(resource, StringComparison.Ordinal);
            Assert.True(index > previous, $"{resource} is missing or out of order.");
            previous = index;
        }
    }

    [Fact]
    public void TokensExposeTheAcceptedSpacingGeometryAndControlScale()
    {
        var keys = ResourceKeys("src/DevForge.Desktop/Resources/Tokens.xaml");

        Assert.All(
            [
                "Space.1",
                "Space.2",
                "Space.3",
                "Space.4",
                "Space.5",
                "Space.6",
                "Space.8",
                "Space.10",
                "Space.12",
                "Control.Height.Compact",
                "Control.Height.Standard",
                "Control.Height.Prominent",
                "Page.Padding",
                "Page.Padding.Compact",
                "Card.Padding",
                "Card.CornerRadius",
                "Surface.CornerRadius",
                "Motion.Fast",
                "Motion.Standard",
            ],
            key => Assert.Contains(key, keys));
    }

    [Fact]
    public void ControlsAndComponentsExposeTheAcceptedInteractionPrimitives()
    {
        var controls = ResourceKeys("src/DevForge.Desktop/Resources/Controls.xaml");
        Assert.All(
            [
                "Button.Template",
                "Button.Primary",
                "Button.Secondary",
                "Button.Ghost",
                "Button.Danger",
                "Button.Icon",
                "NavigationButton",
            ],
            key => Assert.Contains(key, controls));

        var components = ResourceKeys("src/DevForge.Desktop/Resources/Components.xaml");
        Assert.All(
            [
                "Card",
                "Card.Raised",
                "StatusBadge",
                "Callout.Info",
                "Callout.Warning",
                "Callout.Error",
                "Callout.Success",
                "ActionBar",
                "ConsolePanel",
                "EmptyState",
                "EmptyStateIcon",
                "FormCard",
                "WorkflowStepper",
                "TimelineList",
                "ToastCard",
            ],
            key => Assert.Contains(key, components));
    }

    [Fact]
    public void ShellUsesAdaptiveNavigationSafeModeAndSemanticToasts()
    {
        var xaml = Read("src/DevForge.Desktop/MainWindow.xaml");

        Assert.Contains("EqualityMultiConverter", xaml, StringComparison.Ordinal);
        Assert.Contains("DoubleLessThanConverter", xaml, StringComparison.Ordinal);
        Assert.Contains("NavigationButton", xaml, StringComparison.Ordinal);
        Assert.Contains("Safe mode", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NotificationSeverity.Warning", xaml, StringComparison.Ordinal);
        Assert.Contains("NotificationSeverity.Error", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.HelpText", xaml, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        "src/DevForge.Desktop/Dashboard/DashboardView.xaml",
        "Create Project",
        "EmptyState",
        "RefreshCommand")]
    [InlineData(
        "src/DevForge.Desktop/BlueprintCatalog/BlueprintCatalogView.xaml",
        "TrustLabel",
        "EmptyState",
        "CreateCommand")]
    [InlineData(
        "src/DevForge.Desktop/RunHistory/RunHistoryView.xaml",
        "StatusBadge",
        "EmptyState",
        "RetryPublishCommand")]
    public void DataPagesExposeHierarchyEmptyStateAndBackedActions(
        string path,
        string hierarchyMarker,
        string emptyStateMarker,
        string commandMarker)
    {
        var xaml = Read(path);

        Assert.Contains(hierarchyMarker, xaml, StringComparison.Ordinal);
        Assert.Contains(emptyStateMarker, xaml, StringComparison.Ordinal);
        Assert.Contains(commandMarker, xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("#FFD13438", xaml, StringComparison.OrdinalIgnoreCase);
    }

    private static HashSet<string> ResourceKeys(string relativePath) =>
        KeyExpression()
            .Matches(Read(relativePath))
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

    private static string Read(string relativePath) =>
        File.ReadAllText(
            Path.Combine(
                FindRepositoryRoot(),
                relativePath.Replace('/', Path.DirectorySeparatorChar)));

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

    [GeneratedRegex("x:Key=\"([^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex KeyExpression();
}
