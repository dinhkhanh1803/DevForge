using System.Diagnostics;

namespace DevForge.Infrastructure.Processes;

internal static class DotNetProcessEnvironment
{
    public static void Apply(ProcessStartInfo startInfo, string trustedExecutablePath)
    {
        var sdkRoot = Path.GetDirectoryName(trustedExecutablePath)!;
        var systemDirectory = System.Environment.SystemDirectory;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DOTNET_ROOT"] = sdkRoot,
            ["DOTNET_HOST_PATH"] = trustedExecutablePath,
            ["PATH"] = sdkRoot + Path.PathSeparator + systemDirectory,
            ["SystemRoot"] = Path.GetDirectoryName(systemDirectory)!,
            ["WINDIR"] = Path.GetDirectoryName(systemDirectory)!,
            ["ProgramFiles"] = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles),
            ["ProgramFiles(x86)"] = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFilesX86),
            ["USERPROFILE"] = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
            ["APPDATA"] = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
            ["LOCALAPPDATA"] = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            ["TEMP"] = Path.GetTempPath(),
            ["TMP"] = Path.GetTempPath(),
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0",
            ["MSBUILDDISABLENODEREUSE"] = "1",
            ["UseSharedCompilation"] = "false",
        };
        if (startInfo.Environment.Keys.Any(values.ContainsKey)
            || values.Values.Any(string.IsNullOrWhiteSpace))
        {
            throw new InfrastructureOperationException("DF-PROC-001",
                "The declared .NET runtime environment could not be prepared safely.");
        }

        foreach (var value in values)
        {
            startInfo.Environment.Add(value.Key, value.Value);
        }
    }
}
