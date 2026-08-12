namespace DevForge.E2ETests.Desktop;

public sealed class DesktopAssemblyTests
{
    [Fact]
    public void DesktopTargetsWindowsWpfAndExposesApplicationRoot()
    {
        Assert.True(typeof(DevForge.Desktop.App).IsSubclassOf(typeof(System.Windows.Application)));
        Assert.Equal("DevForge.Desktop", typeof(DevForge.Desktop.App).Assembly.GetName().Name);
    }

    [Fact]
    public void ApplicationOwnsExplicitStartupAndShutdownLifecycle()
    {
        var flags = System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic;

        Assert.Equal(typeof(DevForge.Desktop.App), typeof(DevForge.Desktop.App).GetMethod("OnStartup", flags)?.DeclaringType);
        Assert.Equal(typeof(DevForge.Desktop.App), typeof(DevForge.Desktop.App).GetMethod("OnExit", flags)?.DeclaringType);
    }
}
