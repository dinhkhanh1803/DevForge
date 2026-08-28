using DevForge.Application.Contracts;
using DevForge.Infrastructure.FileSystem;

namespace DevForge.Infrastructure.Execution;

internal static class UvExecutionEnvironment
{
    private static readonly WorkspaceRelativePath _tooling = WorkspaceRelativePath.Create("tooling").Value;
    private static readonly string[] _directories = ["tooling", @"tooling\venv", @"tooling\mypy"];

    public static async Task PrepareAsync(StagingWorkspace staging, CancellationToken cancellationToken)
    {
        var container = staging.ContainerWorkspace ?? throw Invalid();
        foreach (var directory in _directories)
        {
            await container.CreateDirectoryAsync(WorkspaceRelativePath.Create(directory).Value, cancellationToken).ConfigureAwait(false);
        }
        var toolingRoot = WorkspaceRoot.Create(Path.Combine(container.Root.RevealForFileSystem(), _tooling.Value)).Value;
        var toolingWorkspace = await new WindowsFileSystem().OpenWorkspaceAsync(toolingRoot, cancellationToken).ConfigureAwait(false);
        if (toolingWorkspace is not IBoundedWorkspaceEnumerator bounded) { throw Invalid(); }
        var tree = await bounded.EnumerateTreeBoundedAsync(null, AtomicProjectFinalizer.MaximumFileCount,
            AtomicProjectFinalizer.MaximumDirectoryCount, AtomicProjectFinalizer.MaximumPathDepth, cancellationToken).ConfigureAwait(false);
        if (tree.LimitExceeded) { throw Invalid(); }
    }

    public static IEnumerable<KeyValuePair<string, ProcessEnvironmentValue?>> Create(StagingWorkspace staging)
    {
        var container = staging.ContainerWorkspace ?? throw Invalid();
        var root = container.Root.RevealForFileSystem();
        var values = new Dictionary<string, string>
        {
            ["UV_PROJECT_ENVIRONMENT"] = Path.Combine(root, "tooling", "venv"),
            ["MYPY_CACHE_DIR"] = Path.Combine(root, "tooling", "mypy"),
            ["RUFF_NO_CACHE"] = "true",
            ["PYTEST_ADDOPTS"] = "-p no:cacheprovider",
        };
        return values.Select(value => KeyValuePair.Create<string, ProcessEnvironmentValue?>(value.Key,
            ProcessEnvironmentValue.CreateSafe(value.Value).Value));
    }

    private static InfrastructureOperationException Invalid() => new("DF-PROC-002", "The owned Python tooling workspace is unavailable or exceeds its safety bounds.");
}
