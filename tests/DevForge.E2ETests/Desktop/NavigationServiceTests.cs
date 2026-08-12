using DevForge.Desktop.Navigation;

namespace DevForge.E2ETests.Desktop;

public sealed class NavigationServiceTests
{
    [Fact]
    public void StartsAtDashboard()
    {
        var sut = new NavigationService();

        Assert.Equal(DesktopRoute.Dashboard, sut.CurrentRoute);
    }

    [Fact]
    public void NavigatesToEnabledM6Destination()
    {
        var sut = new NavigationService();

        var navigated = sut.TryNavigate(DesktopRoute.EnvironmentDoctor);

        Assert.True(navigated);
        Assert.Equal(DesktopRoute.EnvironmentDoctor, sut.CurrentRoute);
    }

    [Fact]
    public void RejectsDisabledM7Destination()
    {
        var sut = new NavigationService();

        var navigated = sut.TryNavigate(DesktopRoute.CreateProject);

        Assert.False(navigated);
        Assert.Equal(DesktopRoute.Dashboard, sut.CurrentRoute);
    }

    [Fact]
    public void DescriptorsExposeOnlyThreeEnabledM6Routes()
    {
        Assert.Equal(
            [DesktopRoute.Dashboard, DesktopRoute.EnvironmentDoctor, DesktopRoute.Settings],
            NavigationService.Descriptors.Where(item => item.IsEnabled).Select(item => item.Route));
    }
}
