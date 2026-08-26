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
    public void NamedTypographyStylesOwnAThemeAwareForeground()
    {
        var xaml = Read("src/DevForge.Desktop/Resources/Typography.xaml");
        Assert.Contains("Segoe MDL2 Assets", xaml, StringComparison.Ordinal);
        foreach (var key in new[]
                 {
                     "Text.Display",
                     "Text.PageTitle",
                     "Text.SectionTitle",
                     "Text.CardTitle",
                     "Text.Label",
                     "Text.Mono",
                 })
        {
            var start = xaml.IndexOf($"x:Key=\"{key}\"", StringComparison.Ordinal);
            var end = xaml.IndexOf("</Style>", start, StringComparison.Ordinal);
            Assert.True(start >= 0 && end > start, $"{key} style is missing.");
            Assert.Contains("Brush.TextPrimary", xaml[start..end], StringComparison.Ordinal);
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
                "ComboBox.Template",
                "ComboBoxItem.Template",
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

    [Fact]
    public void DoctorAndSettingsUseSemanticStateAndStickyActions()
    {
        var doctor = Read("src/DevForge.Desktop/EnvironmentDoctor/EnvironmentDoctorView.xaml");
        Assert.Contains("Callout.Warning", doctor, StringComparison.Ordinal);
        Assert.Contains("Scan failed; cached results shown", doctor, StringComparison.Ordinal);
        Assert.Contains("StatusBadge", doctor, StringComparison.Ordinal);
        Assert.Contains("CopyDiagnosticsCommand", doctor, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Environment actions\"", doctor, StringComparison.Ordinal);

        var settings = Read("src/DevForge.Desktop/Settings/SettingsView.xaml");
        Assert.Contains("Getting started", settings, StringComparison.Ordinal);
        Assert.Contains("Project defaults", settings, StringComparison.Ordinal);
        Assert.Contains("Appearance", settings, StringComparison.Ordinal);
        Assert.Contains("ActionBar", settings, StringComparison.Ordinal);
        Assert.Contains("ValidationMessages", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("#FFD13438", settings, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateProjectPresentsAllFourWorkflowStagesAndReviewEvidence()
    {
        var xaml = Read("src/DevForge.Desktop/CreateProject/CreateProjectView.xaml");

        Assert.Contains("Configure", xaml, StringComparison.Ordinal);
        Assert.Contains("Review", xaml, StringComparison.Ordinal);
        Assert.Contains("Execute", xaml, StringComparison.Ordinal);
        Assert.Contains("Complete", xaml, StringComparison.Ordinal);
        Assert.Contains("WorkflowStepper", xaml, StringComparison.Ordinal);
        Assert.Contains("FormCard", xaml, StringComparison.Ordinal);
        Assert.Contains("ActionBar", xaml, StringComparison.Ordinal);
        Assert.Contains("Text.Mono", xaml, StringComparison.Ordinal);
        Assert.Contains("CreateAndValidateCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("BackToConfigureCommand", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("#FFD13438", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExecutionAndCompletionSeparateProgressRecoveryAndPublicationState()
    {
        var execution = Read("src/DevForge.Desktop/Execution/ExecutionCenterView.xaml");
        Assert.Contains("TimelineList", execution, StringComparison.Ordinal);
        Assert.Contains("ConsolePanel", execution, StringComparison.Ordinal);
        Assert.Contains("CancelCommand", execution, StringComparison.Ordinal);
        Assert.Contains("ResumeCommand", execution, StringComparison.Ordinal);
        Assert.Contains("CleanupCommand", execution, StringComparison.Ordinal);

        var completion = Read("src/DevForge.Desktop/Execution/LocalReadyView.xaml");
        Assert.Contains("validated local project remains safe", completion, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Callout.Success", completion, StringComparison.Ordinal);
        Assert.Contains("Callout.Warning", completion, StringComparison.Ordinal);
        Assert.Contains(
            "Style=\"{StaticResource StatusBadge.Success}\" VerticalAlignment=\"Top\"",
            completion,
            StringComparison.Ordinal);
        Assert.Contains("RetryPublishCommand", completion, StringComparison.Ordinal);
        Assert.Contains("OpenIdeCommand", completion, StringComparison.Ordinal);
        Assert.DoesNotContain("#FFD13438", completion, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RedesignedViewsRetainAutomationAndDoNotInventBehavior()
    {
        var paths = new[]
        {
            "src/DevForge.Desktop/MainWindow.xaml",
            "src/DevForge.Desktop/Dashboard/DashboardView.xaml",
            "src/DevForge.Desktop/CreateProject/CreateProjectView.xaml",
            "src/DevForge.Desktop/RunHistory/RunHistoryView.xaml",
            "src/DevForge.Desktop/BlueprintCatalog/BlueprintCatalogView.xaml",
            "src/DevForge.Desktop/EnvironmentDoctor/EnvironmentDoctorView.xaml",
            "src/DevForge.Desktop/Settings/SettingsView.xaml",
            "src/DevForge.Desktop/Execution/ExecutionCenterView.xaml",
            "src/DevForge.Desktop/Execution/LocalReadyView.xaml",
        };

        Assert.All(paths, path => Assert.Contains(
            "AutomationProperties.Name", Read(path), StringComparison.Ordinal));
        var all = string.Join(Environment.NewLine, paths.Select(Read));
        Assert.DoesNotContain("Click=", all, StringComparison.Ordinal);
        Assert.DoesNotContain("File.", all, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.", all, StringComparison.Ordinal);
        Assert.DoesNotContain("#FFD13438", all, StringComparison.OrdinalIgnoreCase);
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
