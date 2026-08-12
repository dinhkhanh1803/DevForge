using System.Reflection;
using DevForge.Application.Contracts;

namespace DevForge.UnitTests.Application;

public sealed class ApplicationContractArchitectureTests
{
    private static readonly Type[] _requiredPorts =
    [
        typeof(IProjectPlanner),
        typeof(IExecutionOrchestrator),
        typeof(IProcessRunner),
        typeof(IFileSystem),
        typeof(ITemplateRenderer),
        typeof(IBlueprintCatalog),
        typeof(IEnvironmentDoctor),
        typeof(IRunJournalStore),
        typeof(IGitService),
        typeof(IGitHubService),
        typeof(ISecretScanner),
        typeof(IIdeLauncher),
        typeof(IProjectLocationProbe),
    ];

    [Fact]
    public void RequiredApplicationPortsArePublicInterfaces()
    {
        Assert.All(_requiredPorts, type =>
        {
            Assert.True(type.IsPublic, $"{type.Name} must be public.");
            Assert.True(type.IsInterface, $"{type.Name} must be an interface.");
        });
    }

    [Fact]
    public void EveryPortAsyncOperationRequiresCancellationToken()
    {
        var methods = _requiredPorts.SelectMany(type => type.GetMethods()).ToArray();

        Assert.NotEmpty(methods);
        Assert.All(methods, method =>
        {
            Assert.EndsWith("Async", method.Name, StringComparison.Ordinal);
            Assert.True(
                typeof(Task).IsAssignableFrom(method.ReturnType),
                $"{method.DeclaringType?.Name}.{method.Name} must return Task or Task<T>.");
            Assert.Equal(typeof(CancellationToken), method.GetParameters()[^1].ParameterType);
        });
    }

    [Fact]
    public void ExportedApplicationContractsDoNotExposeCredentialOrShellShapedProperties()
    {
        string[] forbiddenFragments =
        [
            "password",
            "token",
            "connectionstring",
            "commandline",
            "shellcommand",
        ];
        var exportedProperties = typeof(IProjectPlanner).Assembly
            .GetExportedTypes()
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .ToArray();

        Assert.DoesNotContain(
            exportedProperties,
            property => forbiddenFragments.Any(
                fragment => Normalize(property.Name).Contains(fragment, StringComparison.Ordinal)));
    }

    [Fact]
    public void GitHubPortDoesNotCarryAuthenticationMaterial()
    {
        var applicationAssembly = typeof(IGitHubService).Assembly;
        var graph = new Queue<Type>([typeof(IGitHubService)]);
        var visited = new HashSet<Type>();

        while (graph.TryDequeue(out var type))
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (!visited.Add(type))
            {
                continue;
            }

            if (type.IsArray)
            {
                graph.Enqueue(type.GetElementType()!);
            }

            foreach (var argument in type.GetGenericArguments())
            {
                graph.Enqueue(argument);
            }

            if (type.Assembly != applicationAssembly)
            {
                continue;
            }

            Assert.DoesNotContain(
                ["password", "token", "connectionstring", "credential"],
                fragment => Normalize(type.Name).Contains(fragment, StringComparison.Ordinal));

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.DoesNotContain(
                    ["password", "token", "connectionstring", "credential"],
                    fragment => Normalize(property.Name).Contains(fragment, StringComparison.Ordinal));
                graph.Enqueue(property.PropertyType);
            }

            foreach (var method in type.GetMethods())
            {
                graph.Enqueue(method.ReturnType);
                foreach (var parameter in method.GetParameters())
                {
                    graph.Enqueue(parameter.ParameterType);
                }
            }
        }
    }

    private static string Normalize(string value)
    {
        return new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }
}
