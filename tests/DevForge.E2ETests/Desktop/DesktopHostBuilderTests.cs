using System.IO;
using DevForge.Desktop.Bootstrap;
using DevForge.Desktop.Dashboard;
using DevForge.Desktop.EnvironmentDoctor;
using DevForge.Desktop.Settings;
using DevForge.Desktop.Shell;
using DevForge.Desktop.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace DevForge.E2ETests.Desktop;

public sealed class DesktopHostBuilderTests
{
    [Fact]
    public void ProductionGraphResolvesDesktopServicesWithoutServiceLocator()
    {
        var root = Path.Combine(Path.GetTempPath(), "DevForge-HostTests", Guid.NewGuid().ToString("N"));
        var themeHost = new FakeThemeResourceHost();
        using var host = DesktopHostBuilder.Create(
            root,
            themeHost,
            services => services.AddSingleton<ISystemThemeSource>(
                new FakeSystemThemeSource()));

        Assert.NotNull(host.Services.GetRequiredService<IDesktopStartupCoordinator>());
        Assert.NotNull(host.Services.GetRequiredService<DashboardViewModel>());
        Assert.NotNull(host.Services.GetRequiredService<SettingsViewModel>());
        Assert.NotNull(host.Services.GetRequiredService<EnvironmentDoctorViewModel>());
        Assert.NotNull(host.Services.GetRequiredService<ShellViewModel>());
    }

    private sealed class FakeThemeResourceHost : IThemeResourceHost
    {
        public void Apply(EffectiveTheme theme)
        {
        }
    }

    private sealed class FakeSystemThemeSource : ISystemThemeSource
    {
        public EffectiveTheme Current => EffectiveTheme.Light;

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }
    }
}
