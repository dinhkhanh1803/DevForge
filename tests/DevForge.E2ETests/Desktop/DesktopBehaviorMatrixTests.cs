using DevForge.Application.Contracts.Persistence;
using DevForge.Desktop.Bootstrap;
using DevForge.Desktop.EnvironmentDoctor;
using DevForge.Desktop.Navigation;
using DevForge.Desktop.Theming;

namespace DevForge.E2ETests.Desktop;

public sealed class DesktopBehaviorMatrixTests
{
    [Fact]
    public void M6RouteMatrixHasExactEnabledBoundary()
    {
        var matrix = NavigationService.Descriptors.ToDictionary(item => item.Route, item => item.IsEnabled);

        Assert.True(matrix[DesktopRoute.Dashboard]);
        Assert.True(matrix[DesktopRoute.EnvironmentDoctor]);
        Assert.True(matrix[DesktopRoute.Settings]);
        Assert.False(matrix[DesktopRoute.CreateProject]);
        Assert.False(matrix[DesktopRoute.Projects]);
        Assert.False(matrix[DesktopRoute.BlueprintCatalog]);
    }

    [Fact]
    public void StatusMatrixAlwaysHasTextAndIconEvidence()
    {
        var scannedAt = DateTimeOffset.UnixEpoch;

        Assert.All(Enum.GetValues<EnvironmentToolStatus>(), status =>
        {
            var item = new EnvironmentHealthItem("tool", null, status, scannedAt);
            Assert.False(string.IsNullOrWhiteSpace(item.StatusLabel));
            Assert.False(string.IsNullOrWhiteSpace(item.StatusGlyph));
            Assert.False(string.IsNullOrWhiteSpace(item.CompatibilitySummary));
            Assert.False(string.IsNullOrWhiteSpace(item.Remediation));
        });
    }

    [Fact]
    public void ClosedDesktopEnumsHaveExplicitNonzeroValues()
    {
        Assert.Equal([1, 2, 3], Enum.GetValues<ThemePreference>().Select(value => (int)value));
        Assert.Equal([1, 2], Enum.GetValues<DesktopStartupMode>().Select(value => (int)value));
        Assert.Equal([1, 2, 3], Enum.GetValues<DesktopMigrationOutcome>().Select(value => (int)value));
    }
}
