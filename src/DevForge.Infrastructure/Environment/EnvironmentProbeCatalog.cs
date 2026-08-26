using System.Collections.Immutable;
using DevForge.Application.Contracts;

namespace DevForge.Infrastructure.Environment;

internal static class EnvironmentProbeCatalog
{
    public static ImmutableArray<EnvironmentProbe> All { get; } =
    [
        Create("dotnet", "dotnet", "--version"),
        Create("git", "git", "--version"),
        Create("gh", "gh", "--version"),
        Create("node", "node", "--version"),
        Create("python", "python", "--version"),
        Create("uv", "uv", "--version"),
    ];

    public static ImmutableArray<EnvironmentProbe> DotNetOnly { get; } = [All[0]];

    private static EnvironmentProbe Create(
        string name,
        string executableName,
        params string[] arguments)
    {
        return new EnvironmentProbe(
            name,
            ExecutableIdentity.Create(executableName).Value,
            [.. arguments]);
    }
}

internal sealed record EnvironmentProbe(
    string Name,
    ExecutableIdentity Executable,
    ImmutableArray<string> Arguments);
