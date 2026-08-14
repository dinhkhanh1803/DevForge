using System.IO;
using System.Reflection;
using System.Windows;
using DevForge.Application.Contracts;
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

    [Fact]
    public void ViewModelsDoNotRetainWorkspaceHandlesWpfControlsOrEfTypes()
    {
        var desktopAssembly = typeof(DashboardViewModel).Assembly;
        var workspaceType = typeof(IWorkspaceFileSystem);
        var viewModels = desktopAssembly.GetTypes()
            .Where(type => type.Name.EndsWith("ViewModel", StringComparison.Ordinal))
            .ToArray();

        var violations = viewModels
            .SelectMany(type => type
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(field => (Owner: type, Member: field.Name, Type: field.FieldType))
                .Concat(type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Select(property => (Owner: type, Member: property.Name, Type: property.PropertyType)))
                .Concat(type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .SelectMany(constructor => constructor.GetParameters())
                    .Select(parameter => (Owner: type, Member: ".ctor", Type: parameter.ParameterType)))
                .Concat(type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Select(method => (Owner: type, Member: method.Name, Type: method.ReturnType)))
                .Concat(type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .SelectMany(method => method.GetParameters().Select(parameter =>
                        (Owner: type, Member: method.Name, Type: parameter.ParameterType))))
                .Concat(type.GetInterfaces()
                    .Select(item => (Owner: type, Member: "interface", Type: item)))
                .Concat(type.BaseType is null
                    ? []
                    : new[] { (Owner: type, Member: "base", Type: type.BaseType) }))
            .Where(item => ContainsForbiddenUiStateType(item.Type, workspaceType))
            .Select(item => $"{item.Owner.FullName}.{item.Member}: {item.Type.FullName}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Desktop ViewModels retain forbidden UI state:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");

        var root = FindRepositoryRoot();
        string[] forbiddenTokens =
        [
            "DevForge.Infrastructure",
            "IWorkspaceFileSystem",
            "Microsoft.EntityFrameworkCore",
            "System.Diagnostics",
            "System.IO",
            "Directory.",
            "File.",
            "Path.",
            "Process.",
            "System.Windows.Controls",
        ];
        var sourceViolations = Directory
            .EnumerateFiles(Path.Combine(root, "src", "DevForge.Desktop"), "*ViewModel.cs", SearchOption.AllDirectories)
            .SelectMany(path => forbiddenTokens
                .Where(token => File.ReadAllText(path).Contains(token, StringComparison.Ordinal))
                .Select(token => $"{Path.GetRelativePath(root, path)}: {token}"))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(
            sourceViolations.Length == 0,
            $"Desktop ViewModel source references forbidden types:{Environment.NewLine}{string.Join(Environment.NewLine, sourceViolations)}");
    }

    [Fact]
    public void EveryWpfViewHasOnlyParameterlessPublicConstruction()
    {
        var desktopAssembly = typeof(DashboardViewModel).Assembly;
        var viewTypes = desktopAssembly.GetTypes()
            .Where(type => !type.IsAbstract
                && typeof(FrameworkElement).IsAssignableFrom(type)
                && (type.Name.EndsWith("View", StringComparison.Ordinal)
                    || type.Name.EndsWith("Window", StringComparison.Ordinal)))
            .ToArray();

        Assert.NotEmpty(viewTypes);
        Assert.All(viewTypes, viewType =>
        {
            var constructors = viewType.GetConstructors(BindingFlags.Instance | BindingFlags.Public);
            Assert.Single(constructors);
            Assert.Empty(constructors[0].GetParameters());
        });
    }

    [Fact]
    public void ApplicationLayerDoesNotReferenceDesktopWpfOrInfrastructure()
    {
        var application = typeof(IProjectCreationWorkflow).Assembly;
        var forbidden = new[]
        {
            typeof(DashboardViewModel).Assembly.GetName().Name,
            typeof(DevForge.Infrastructure.FileSystem.WindowsFileSystem).Assembly.GetName().Name,
            typeof(FrameworkElement).Assembly.GetName().Name,
            "PresentationFramework",
            "PresentationCore",
            "WindowsBase",
        }.ToHashSet(StringComparer.Ordinal);

        var violations = application.GetReferencedAssemblies()
            .Where(reference => reference.Name is not null && forbidden.Contains(reference.Name))
            .Select(reference => reference.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(violations);
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

    private static bool ContainsForbiddenUiStateType(Type type, Type workspaceType)
    {
        if (workspaceType.IsAssignableFrom(type)
            || typeof(DependencyObject).IsAssignableFrom(type)
            || type.Namespace?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) == true
            || type.Namespace?.StartsWith("System.IO", StringComparison.Ordinal) == true
            || type == typeof(System.Diagnostics.Process)
            || type.Assembly.GetName().Name == "DevForge.Infrastructure")
        {
            return true;
        }

        return type.IsArray
            ? ContainsForbiddenUiStateType(type.GetElementType()!, workspaceType)
            : type.IsGenericType
                && type.GetGenericArguments().Any(argument =>
                    ContainsForbiddenUiStateType(argument, workspaceType));
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "DevForge.sln")))
        {
            current = current.Parent;
        }

        return current?.FullName
            ?? throw new InvalidOperationException("The repository root could not be located.");
    }
}
