using System.Diagnostics;

namespace DevForge.Infrastructure.Processes;

internal static class NodeProcessEnvironment
{
    public static void Apply(ProcessStartInfo startInfo, string trustedNodePath)
    {
        var system = System.Environment.SystemDirectory;
        var home = Path.Combine(startInfo.WorkingDirectory, ".devforge-node");
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SystemRoot"] = Path.GetDirectoryName(system)!,
            ["WINDIR"] = Path.GetDirectoryName(system)!,
            ["PATH"] = Path.GetDirectoryName(trustedNodePath) + Path.PathSeparator + system,
            ["USERPROFILE"] = home,
            ["HOME"] = home,
            ["APPDATA"] = Path.Combine(home, "config"),
            ["LOCALAPPDATA"] = Path.Combine(home, "local"),
            ["XDG_CONFIG_HOME"] = Path.Combine(home, "config"),
            ["XDG_CACHE_HOME"] = Path.Combine(home, "cache"),
            ["XDG_DATA_HOME"] = Path.Combine(home, "data"),
            ["TEMP"] = Path.GetTempPath(),
            ["TMP"] = Path.GetTempPath(),
            ["CI"] = "true",
            ["NEXT_TELEMETRY_DISABLED"] = "1",
            ["COREPACK_ENABLE_NETWORK"] = "0",
            ["COREPACK_ENABLE_DOWNLOAD_PROMPT"] = "0",
            ["PNPM_HOME"] = Path.Combine(home, "pnpm"),
            ["npm_config_userconfig"] = Path.Combine(home, "user.npmrc"),
            ["npm_config_globalconfig"] = Path.Combine(home, "global.npmrc"),
            ["npm_config_cache"] = Path.Combine(home, "cache"),
            ["npm_config_store_dir"] = Path.Combine(home, "store"),
            ["npm_config_registry"] = "https://registry.npmjs.org/",
            ["npm_config_ignore_scripts"] = "true",
            ["npm_config_ignore_pnpmfile"] = "true",
            ["npm_config_node_linker"] = "hoisted",
            ["npm_config_package_import_method"] = "copy",
            ["npm_config_shell_emulator"] = "true",
            ["npm_config_manage_package_manager_versions"] = "false",
            ["npm_config_update_notifier"] = "false",
            ["npm_config_verify_store_integrity"] = "true",
        };
        if (values.Values.Any(string.IsNullOrWhiteSpace)
            || startInfo.Environment.Keys.Any(key => values.ContainsKey(key)
                || key.StartsWith("NODE_", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("NPM_", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("PNPM_", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("COREPACK_", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InfrastructureOperationException("DF-PROC-001", "The declared Node runtime environment could not be prepared safely.");
        }
        foreach (var value in values) { startInfo.Environment.Add(value.Key, value.Value); }
    }
}
