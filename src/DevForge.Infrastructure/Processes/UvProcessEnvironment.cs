using System.Diagnostics;

namespace DevForge.Infrastructure.Processes;

internal static class UvProcessEnvironment
{
    public static void Apply(ProcessStartInfo startInfo, string? trustedPythonPath)
    {
        var system = System.Environment.SystemDirectory;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SystemRoot"] = Path.GetDirectoryName(system)!,
            ["WINDIR"] = Path.GetDirectoryName(system)!,
            ["PATH"] = trustedPythonPath is null ? system : Path.GetDirectoryName(trustedPythonPath) + Path.PathSeparator + system,
            ["USERPROFILE"] = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
            ["APPDATA"] = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
            ["LOCALAPPDATA"] = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            ["TEMP"] = Path.GetTempPath(),
            ["TMP"] = Path.GetTempPath(),
            ["UV_PYTHON_DOWNLOADS"] = "never",
            ["UV_LINK_MODE"] = "copy",
            ["UV_NO_CACHE"] = "true",
            ["UV_NO_CONFIG"] = "true",
            ["PYTHONDONTWRITEBYTECODE"] = "1",
            ["PYTHONUTF8"] = "1",
        };
        if (trustedPythonPath is not null) { values.Add("UV_PYTHON", trustedPythonPath); }
        if (startInfo.Environment.Keys.Any(values.ContainsKey) || values.Values.Any(string.IsNullOrWhiteSpace))
        {
            throw new InfrastructureOperationException("DF-PROC-001", "The declared Python runtime environment could not be prepared safely.");
        }
        foreach (var value in values) { startInfo.Environment.Add(value.Key, value.Value); }
    }
}
