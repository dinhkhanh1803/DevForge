using System.IO;
using DevForge.Application.Contracts;
using DevForge.Application.Contracts.Persistence;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Blueprints.BuiltIn;
using DevForge.Desktop.BlueprintCatalog;
using DevForge.Desktop.Bootstrap;
using DevForge.Desktop.CreateProject;
using DevForge.Desktop.Dashboard;
using DevForge.Desktop.Diagnostics;
using DevForge.Desktop.EnvironmentDoctor;
using DevForge.Desktop.Execution;
using DevForge.Desktop.RunHistory;
using DevForge.Desktop.Settings;
using DevForge.Desktop.Shell;
using DevForge.Desktop.Theming;
using DevForge.Infrastructure;
using DevForge.Infrastructure.FileSystem;
using Microsoft.Extensions.DependencyInjection;

namespace DevForge.E2ETests.Desktop;

public sealed class DesktopHostBuilderTests
{
    [Fact]
    public void BuildOutputContainsTheCanonicalBuiltInCatalogRoot()
    {
        Assert.True(Directory.Exists(Path.Combine(
            AppContext.BaseDirectory,
            BuiltInBlueprintCatalog.OutputDirectory)));
        Assert.True(File.Exists(Path.Combine(
            AppContext.BaseDirectory,
            BuiltInBlueprintCatalog.OutputDirectory,
            "README.md")));
    }

    [Fact]
    public async Task MissingBuiltInCatalogFailsBeforeProvisioningLocalBlueprintStorage()
    {
        var localDataRoot = Path.Combine(
            Path.GetTempPath(),
            "DevForge-HostTests",
            Guid.NewGuid().ToString("N"));
        var missingApplicationRoot = Path.Combine(
            Path.GetTempPath(),
            "DevForge-MissingBuiltInTests",
            Guid.NewGuid().ToString("N"));
        var database = DatabaseLocation.Create(localDataRoot, "devforge.db").Value;
        var registry = new DesktopBlueprintSourceRegistry(
            database,
            new WindowsFileSystem(),
            BuiltInBlueprintPackageLocation.Create(missingApplicationRoot));

        var exception = await Assert.ThrowsAsync<InfrastructureOperationException>(
            () => registry.InitializeAsync(CancellationToken.None));

        Assert.Equal("DF-FS-001", exception.Code);
        Assert.False(Directory.Exists(Path.Combine(localDataRoot, "blueprints")));
    }

    [Fact]
    public void BuiltInCatalogLocationRejectsRelativeApplicationRoot()
    {
        Assert.Throws<ArgumentException>(
            () => BuiltInBlueprintPackageLocation.Create("relative-application-root"));
    }

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
        var sourceRegistry = host.Services.GetRequiredService<DesktopBlueprintSourceRegistry>();
        await sourceRegistry.InitializeAsync(CancellationToken.None);

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
        Assert.NotNull(host.Services.GetRequiredService<IDiagnosticSink>());
        Assert.NotNull(host.Services.GetRequiredService<IDiagnosticRetentionService>());
        Assert.NotNull(host.Services.GetRequiredService<ISupportBundleWriter>());
        Assert.NotNull(host.Services.GetRequiredService<ISupportBundleCleanupService>());
        Assert.NotNull(host.Services.GetRequiredService<ISupportBundleCoordinator>());
        Assert.NotNull(host.Services.GetRequiredService<DesktopDiagnosticsCoordinator>());
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
        Assert.Collection(
            sourceRegistry,
            source =>
            {
                Assert.Equal("built-in", source.Id);
                Assert.Equal(BlueprintSourceProvenance.BuiltIn, source.Provenance);
            },
            source =>
            {
                Assert.Equal("trusted-local", source.Id);
                Assert.Equal(BlueprintSourceProvenance.Local, source.Provenance);
            });
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
