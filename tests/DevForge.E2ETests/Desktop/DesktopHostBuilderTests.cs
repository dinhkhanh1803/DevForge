using System.IO;
using DevForge.Application.Contracts;
using DevForge.Application.Contracts.Persistence;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Desktop.BlueprintCatalog;
using DevForge.Desktop.Bootstrap;
using DevForge.Desktop.CreateProject;
using DevForge.Desktop.Dashboard;
using DevForge.Desktop.EnvironmentDoctor;
using DevForge.Desktop.Execution;
using DevForge.Desktop.RunHistory;
using DevForge.Desktop.Settings;
using DevForge.Desktop.Shell;
using DevForge.Desktop.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace DevForge.E2ETests.Desktop;

public sealed class DesktopHostBuilderTests
{
    [Fact]
    public void ResolvingReadOnlyDesktopGraphDoesNotProvisionBlueprintSource()
    {
        var root = Path.Combine(Path.GetTempPath(), "DevForge-HostTests", Guid.NewGuid().ToString("N"));
        using var host = DesktopHostBuilder.Create(
            root,
            new FakeThemeResourceHost(),
            services => services.AddSingleton<ISystemThemeSource>(new FakeSystemThemeSource()));

        Assert.NotNull(host.Services.GetRequiredService<ShellViewModel>());
        Assert.False(Directory.Exists(Path.Combine(root, "blueprints")));
    }

    [Fact]
    public async Task ProductionGraphResolvesDesktopServicesWithoutServiceLocator()
    {
        var root = Path.Combine(Path.GetTempPath(), "DevForge-HostTests", Guid.NewGuid().ToString("N"));
        var themeHost = new FakeThemeResourceHost();
        using var host = DesktopHostBuilder.Create(
            root,
            themeHost,
            services => services.AddSingleton<ISystemThemeSource>(
                new FakeSystemThemeSource()));

        await host.Services.GetRequiredService<ILocalDataRootProvisioner>()
            .EnsureExistsAsync(
                host.Services.GetRequiredService<DatabaseLocation>(),
                CancellationToken.None);
        await host.Services.GetRequiredService<DesktopBlueprintSourceRegistry>()
            .InitializeAsync(CancellationToken.None);

        Assert.NotNull(host.Services.GetRequiredService<IDesktopStartupCoordinator>());
        Assert.NotNull(host.Services.GetRequiredService<DashboardViewModel>());
        Assert.NotNull(host.Services.GetRequiredService<SettingsViewModel>());
        Assert.NotNull(host.Services.GetRequiredService<EnvironmentDoctorViewModel>());
        Assert.NotNull(host.Services.GetRequiredService<ShellViewModel>());
        Assert.NotNull(host.Services.GetRequiredService<IBlueprintCatalog>());
        Assert.NotNull(host.Services.GetRequiredService<IProjectPlanner>());
        Assert.NotNull(host.Services.GetRequiredService<IGitService>());
        Assert.NotNull(host.Services.GetRequiredService<IGitHubService>());
        Assert.NotNull(host.Services.GetRequiredService<IPublicationGitService>());
        Assert.NotNull(host.Services.GetRequiredService<IPublicationGitHubService>());
        Assert.NotNull(host.Services.GetRequiredService<IPublicationLeaseProvider>());
        Assert.NotNull(host.Services.GetRequiredService<IProjectPublicationWorkspaceFactory>());
        Assert.NotNull(host.Services.GetRequiredService<IPublicationReceiptStore>());
        Assert.NotNull(host.Services.GetRequiredService<IPublicationNonceGenerator>());
        Assert.NotNull(host.Services.GetRequiredService<IProjectPublicationCoordinator>());
        Assert.NotNull(host.Services.GetRequiredService<IStagingWorkspaceManager>());
        Assert.NotNull(host.Services.GetRequiredService<IExecutionOrchestrator>());
        Assert.NotNull(host.Services.GetRequiredService<IRunRecoveryService>());
        Assert.NotNull(host.Services.GetRequiredService<IProjectCreationWorkflow>());
        Assert.NotNull(host.Services.GetRequiredService<ExecutionSessionCoordinator>());
        Assert.NotNull(host.Services.GetRequiredService<CreateProjectViewModel>());
        Assert.NotNull(host.Services.GetRequiredService<BlueprintCatalogViewModel>());
        Assert.NotNull(host.Services.GetRequiredService<RunHistoryViewModel>());
        var registries = host.Services.GetRequiredService<IExecutionHandlerRegistryProvider>();
        Assert.True(registries.Create(BlueprintTrust.BuiltIn).IsSuccessful);
        Assert.True(registries.Create(BlueprintTrust.TrustedLocal).IsSuccessful);
        Assert.True(Directory.Exists(Path.Combine(root, "blueprints", "local")));
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
