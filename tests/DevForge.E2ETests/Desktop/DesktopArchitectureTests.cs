using System.Reflection;
using DevForge.Desktop.Dashboard;

namespace DevForge.E2ETests.Desktop;

public sealed class DesktopArchitectureTests
{
    [Fact]
    public void CoreLayersDoNotReferenceDesktop()
    {
        var desktopName = typeof(DashboardViewModel).Assembly.GetName().Name;
        Assembly[] coreAssemblies =
        [
            typeof(DevForge.Domain.Runs.ProjectRun).Assembly,
            typeof(DevForge.Application.Contracts.IProjectPlanner).Assembly,
            typeof(DevForge.Infrastructure.FileSystem.WindowsFileSystem).Assembly,
        ];

        Assert.All(coreAssemblies, assembly =>
            Assert.DoesNotContain(
                assembly.GetReferencedAssemblies(),
                reference => reference.Name == desktopName));
    }

    [Fact]
    public void ViewModelsDoNotHoldInfrastructureOrIoTypes()
    {
        var desktopAssembly = typeof(DashboardViewModel).Assembly;
        var infrastructureAssembly = typeof(DevForge.Infrastructure.FileSystem.WindowsFileSystem).Assembly;
        var viewModels = desktopAssembly.GetTypes()
            .Where(type => type.Name.EndsWith("ViewModel", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(viewModels);
        Assert.All(viewModels, viewModel =>
        {
            var members = viewModel
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(field => field.FieldType)
                .Concat(viewModel.GetProperties().Select(property => property.PropertyType));
            Assert.DoesNotContain(members, type => type.Assembly == infrastructureAssembly);
            Assert.DoesNotContain(members, type => type.Namespace?.StartsWith("System.IO", StringComparison.Ordinal) == true);
            Assert.DoesNotContain(members, type => type == typeof(System.Diagnostics.Process));
        });
    }

    [Theory]
    [InlineData(typeof(DevForge.Desktop.MainWindow))]
    [InlineData(typeof(DashboardView))]
    [InlineData(typeof(DevForge.Desktop.Settings.SettingsView))]
    [InlineData(typeof(DevForge.Desktop.EnvironmentDoctor.EnvironmentDoctorView))]
    public void CodeBehindHasOnlyParameterlessConstruction(Type viewType)
    {
        var constructors = viewType.GetConstructors(BindingFlags.Instance | BindingFlags.Public);

        Assert.Single(constructors);
        Assert.Empty(constructors[0].GetParameters());
    }
}
