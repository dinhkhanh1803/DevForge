using System.Collections.Immutable;
using DevForge.Application.Contracts;
using DevForge.Infrastructure.FileSystem;

namespace DevForge.Infrastructure.Processes;

internal sealed class TrustedExecutableResolver : ITrustedExecutableResolver
{
    private readonly string? _pathValue;
    private readonly string? _dotNetHostPath;

    public TrustedExecutableResolver()
        : this(
            System.Environment.GetEnvironmentVariable("PATH"),
            System.Environment.GetEnvironmentVariable("DOTNET_HOST_PATH"))
    {
    }

    internal TrustedExecutableResolver(string? pathValue, string? dotNetHostPath)
    {
        _pathValue = pathValue;
        _dotNetHostPath = dotNetHostPath;
    }

    public TrustedExecutableLaunch Resolve(ExecutableIdentity executable)
    {
        ArgumentNullException.ThrowIfNull(executable);

        foreach (var candidate in EnumerateCandidates(executable))
        {
            if (IsTrustedExecutableFile(candidate.ExecutablePath)
                && IsTrustedPrefix(candidate.PrefixArguments, executable))
            {
                return new TrustedExecutableLaunch(
                    Path.GetFullPath(candidate.ExecutablePath),
                    [.. candidate.PrefixArguments.Select(argument =>
                        Path.IsPathFullyQualified(argument) ? Path.GetFullPath(argument) : argument)]);
            }
        }

        throw new InfrastructureOperationException(
            "DF-PROC-001",
            "The trusted executable could not be resolved.");
    }

    private IEnumerable<TrustedExecutableLaunch> EnumerateCandidates(
        ExecutableIdentity executable)
    {
        if (executable.Tool == ExecutableTool.DotNet)
        {
            if (!string.IsNullOrWhiteSpace(_dotNetHostPath))
            {
                yield return new TrustedExecutableLaunch(_dotNetHostPath, []);
            }
        }

        foreach (var candidate in EnumeratePathFiles(executable.ExecutableName + ".exe"))
        {
            yield return new TrustedExecutableLaunch(candidate, []);
        }

        if (executable.Tool is not (ExecutableTool.Npm
            or ExecutableTool.Npx
            or ExecutableTool.Pnpm
            or ExecutableTool.Yarn))
        {
            yield break;
        }

        foreach (var nodePath in EnumeratePathFiles("node.exe"))
        {
            var nodeDirectory = Path.GetDirectoryName(nodePath);
            if (nodeDirectory is null)
            {
                continue;
            }

            if (executable.Tool is ExecutableTool.Npm or ExecutableTool.Npx)
            {
                var script = Path.Combine(
                    nodeDirectory,
                    "node_modules",
                    "npm",
                    "bin",
                    executable.Tool == ExecutableTool.Npm ? "npm-cli.js" : "npx-cli.js");
                yield return new TrustedExecutableLaunch(nodePath, [script]);
                continue;
            }

            if (executable.Tool == ExecutableTool.Pnpm)
            {
                foreach (var script in EnumeratePathFiles(Path.Combine("node_modules", "pnpm", "bin", "pnpm.cjs")))
                {
                    yield return new TrustedExecutableLaunch(nodePath, [script]);
                }
            }

            var corepack = Path.Combine(
                nodeDirectory,
                "node_modules",
                "corepack",
                "dist",
                "corepack.js");
            yield return new TrustedExecutableLaunch(
                nodePath,
                [corepack, executable.ExecutableName]);
        }
    }

    private IEnumerable<string> EnumeratePathFiles(string fileName)
    {
        if (string.IsNullOrWhiteSpace(_pathValue))
        {
            yield break;
        }

        foreach (var directory in _pathValue.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return Path.Combine(directory, fileName);
        }
    }

    private static bool IsTrustedExecutableFile(string candidate)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(candidate)
                || !Path.IsPathFullyQualified(candidate)
                || candidate.StartsWith(@"\\", StringComparison.Ordinal)
                || !File.Exists(candidate))
            {
                return false;
            }

            var attributes = File.GetAttributes(candidate);
            return (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;
        }
        catch (Exception exception) when (WindowsWorkspaceFileSystem.IsExpectedFileSystemFailure(exception))
        {
            return false;
        }
    }

    private static bool IsTrustedPrefix(
        ImmutableArray<string> prefixArguments,
        ExecutableIdentity executable)
    {
        return prefixArguments.IsEmpty
            || IsTrustedExecutableFile(prefixArguments[0])
                && prefixArguments.Skip(1).All(argument =>
                    StringComparer.Ordinal.Equals(argument, executable.ExecutableName));
    }
}
