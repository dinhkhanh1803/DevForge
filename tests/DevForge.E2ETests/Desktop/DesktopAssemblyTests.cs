namespace DevForge.E2ETests.Desktop;

public sealed class DesktopAssemblyTests
{
    [Fact]
    public void DesktopTargetsWindowsWpfAndExposesApplicationRoot()
    {
        Assert.True(typeof(DevForge.Desktop.App).IsSubclassOf(typeof(System.Windows.Application)));
        Assert.Equal("DevForge.Desktop", typeof(DevForge.Desktop.App).Assembly.GetName().Name);
    }
}
