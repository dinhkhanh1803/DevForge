using DevForge.Application.Contracts;
using DevForge.Infrastructure.FileSystem;

namespace DevForge.Infrastructure.Processes;

internal sealed class TrustedExecutableResolver : ITrustedExecutableResolver
{
    public string Resolve(ExecutableIdentity executable)
    {
        ArgumentNullException.ThrowIfNull(executable);

        foreach (var candidate in EnumerateCandidates(executable))
        {
            if (IsTrustedExecutableFile(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        throw new InfrastructureOperationException(
            "DF-PROC-001",
            "The trusted executable could not be resolved.");
    }

    private static IEnumerable<string> EnumerateCandidates(ExecutableIdentity executable)
    {
        if (executable.Tool == ExecutableTool.DotNet)
        {
            var configuredHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            if (!string.IsNullOrWhiteSpace(configuredHost))
            {
                yield return configuredHost;
            }
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            yield break;
        }

        var executableFileName = executable.ExecutableName + ".exe";
        foreach (var directory in pathValue.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return Path.Combine(directory, executableFileName);
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
}
