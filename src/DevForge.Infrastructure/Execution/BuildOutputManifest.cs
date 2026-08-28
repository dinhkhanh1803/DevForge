using System.Buffers;
using System.Collections.Immutable;
using System.Text.Json;
using DevForge.Application.Contracts;
using DevForge.Domain.Execution;
using DevForge.Infrastructure.FileSystem;

namespace DevForge.Infrastructure.Execution;

// Membership is engine-authored and bound by the complete finalized-tree digest.
// This never interprets .gitignore or removes a file from integrity/secret checks.
internal static class BuildOutputManifest
{
    internal const int MaximumBytes = 1024 * 1024;
    internal static readonly WorkspaceRelativePath Path = ProjectEvidencePathPolicy.BuildOutputsPath;
    private static readonly ImmutableArray<string> _pythonProjects = ["pyproject.toml", "uv.lock"];
    private static readonly ImmutableArray<string> _pythonBuild = ["run", "--frozen", "--no-sync", "--no-config", "pyproject-build", "--no-isolation"];

    public static async Task<byte[]?> CreateAsync(RunCheckpoint checkpoint,
        IWorkspaceFileSystem workspace, CancellationToken cancellationToken)
    {
        var declared = checkpoint.Preview?.Artifacts.Select(item => item.Path.Replace('/', '\\'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var projects = declared.Where(IsProjectPath).Order(StringComparer.Ordinal).ToImmutableArray();
        var hasDotnetValidation = checkpoint.Plan.Validators.Any(validator => validator.Required
            && validator.Handler == "validate-command" && IsDotnetAtRoot(validator.Inputs));
        var python = _pythonProjects.All(declared.Contains) && checkpoint.Plan.Validators.Any(validator => validator.Required
            && validator.Handler == "validate-command"
            && validator.Inputs.TryGetValue("executable", out var tool) && tool.StringValue == "uv"
            && validator.Inputs.TryGetValue("workingDirectory", out var directory) && directory.StringValue == "."
            && validator.Inputs.TryGetValue("arguments", out var arguments) && arguments.Kind == PlanValueKind.Sequence
            && arguments.ArrayValue.All(item => item.Kind == PlanValueKind.Text)
            && arguments.ArrayValue.Select(item => item.StringValue).SequenceEqual(_pythonBuild, StringComparer.Ordinal));
        if (python)
        {
            if (!projects.IsEmpty) { throw Invalid(); }
            projects = _pythonProjects;
        }
        if (projects.IsEmpty || !hasDotnetValidation && !python)
        {
            if (await workspace.FileExistsAsync(Path, cancellationToken).ConfigureAwait(false)
                || await workspace.DirectoryExistsAsync(Path, cancellationToken).ConfigureAwait(false))
            {
                throw Invalid();
            }
            return null;
        }
        var files = await EnumerateAsync(workspace, cancellationToken).ConfigureAwait(false);
        var publish = checkpoint.Plan.Validators.Any(validator => validator.Required
            && validator.Handler == "validate-command" && IsDotnetAtRoot(validator.Inputs)
            && validator.Inputs.TryGetValue("arguments", out var args)
            && args.Kind == PlanValueKind.Sequence && args.ArrayValue.Length == 6
            && args.ArrayValue.All(item => item.Kind == PlanValueKind.Text)
            && projects.Any(project => args.ArrayValue.Select(item => item.StringValue).SequenceEqual(
                ["publish", project, "--configuration", "Release", "--no-restore", "--property:PublishProfile=WindowsSmoke"], StringComparer.Ordinal)));
        var outputs = files.Where(file => !declared.Contains(file.Value)
                && IsOutput(file.Value, projects, publish))
            .Select(file => file.Value).Order(StringComparer.Ordinal).ToImmutableArray();
        if (outputs.IsEmpty)
        {
            if (files.Contains(Path))
            {
                throw Invalid();
            }
            return null;
        }
        if (projects.IsEmpty || projects.Any(project => !files.Any(file => file.Value == project)))
        {
            throw Invalid();
        }
        return Serialize(projects, publish, outputs, python);
    }

    public static ImmutableArray<WorkspaceRelativePath> SourceFiles(
        ImmutableArray<WorkspaceRelativePath> allFiles, byte[]? bytes)
    {
        if (!allFiles.Contains(Path))
        {
            return allFiles;
        }
        try
        {
            if (bytes is null || bytes.Length == 0 || bytes.Length > MaximumBytes)
            {
                throw Invalid();
            }
            using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
            var root = json.RootElement;
            var schema = root.GetProperty("schema").GetString();
            var python = schema == "devforge-python-build-outputs-v1";
            if (!python && schema != "devforge-build-outputs-v1")
            {
                throw Invalid();
            }
            var projects = ReadPaths(root.GetProperty("projects"));
            var outputs = ReadPaths(root.GetProperty("outputs"));
            var publish = root.GetProperty("publish").GetBoolean();
            var available = allFiles.Select(file => file.Value).ToHashSet(StringComparer.Ordinal);
            if (projects.IsEmpty || outputs.IsEmpty
                || (python ? publish || !projects.SequenceEqual(_pythonProjects, StringComparer.Ordinal) : projects.Any(project => !IsProjectPath(project)))
                || projects.Any(project => !available.Contains(project))
                || outputs.Any(output => !IsOutput(output, projects, publish) || !available.Contains(output))
                || !bytes.AsSpan().SequenceEqual(Serialize(projects, publish, outputs, python)))
            {
                throw Invalid();
            }
            var excluded = outputs.ToHashSet(StringComparer.Ordinal);
            return [.. allFiles.Where(file => !excluded.Contains(file.Value))];
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException
            or KeyNotFoundException or ArgumentException or EndOfStreamException)
        {
            throw Invalid();
        }
    }

    private static bool IsDotnetAtRoot(ImmutableDictionary<string, PlanValue> inputs) =>
        inputs.TryGetValue("executable", out var executable) && executable.StringValue == "dotnet"
        && inputs.TryGetValue("workingDirectory", out var working) && working.StringValue == ".";

    private static bool IsProjectPath(string path) => path.EndsWith(".csproj", StringComparison.Ordinal)
        && !path.Split('\\').Any(segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("artifacts", StringComparison.OrdinalIgnoreCase)
            || segment.StartsWith('.'));

    private static bool IsOutput(string path, ImmutableArray<string> projects, bool publish)
    {
        if (projects.SequenceEqual(_pythonProjects, StringComparer.Ordinal))
        {
            return !publish && path.StartsWith("dist\\", StringComparison.Ordinal)
                && path.Count(character => character == '\\') == 1
                && (path.EndsWith(".whl", StringComparison.Ordinal) || path.EndsWith(".tar.gz", StringComparison.Ordinal));
        }
        if (publish && path.StartsWith("artifacts\\publish\\", StringComparison.Ordinal))
        {
            return true;
        }
        return projects.Any(project =>
        {
            var separator = project.LastIndexOf('\\');
            var parent = separator < 0 ? string.Empty : project[..(separator + 1)];
            return path.StartsWith(parent + "bin\\", StringComparison.Ordinal)
                || path.StartsWith(parent + "obj\\", StringComparison.Ordinal);
        });
    }

    private static ImmutableArray<string> ReadPaths(JsonElement array)
    {
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > AtomicProjectFinalizer.MaximumFileCount)
        {
            throw Invalid();
        }
        var paths = ImmutableArray.CreateBuilder<string>();
        var distinct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in array.EnumerateArray())
        {
            var text = item.GetString();
            var path = WorkspaceRelativePath.Create(text?.Replace('/', '\\'));
            if (!path.IsValid || text != path.Value.Value.Replace('\\', '/') || !distinct.Add(path.Value.Value))
            {
                throw Invalid();
            }
            paths.Add(path.Value.Value);
        }
        var result = paths.ToImmutable();
        if (!result.SequenceEqual(result.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw Invalid();
        }
        return result;
    }

    private static byte[] Serialize(ImmutableArray<string> projects, bool publish, ImmutableArray<string> outputs, bool python = false)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", python ? "devforge-python-build-outputs-v1" : "devforge-build-outputs-v1");
            writer.WriteStartArray("projects");
            foreach (var project in projects) { writer.WriteStringValue(project.Replace('\\', '/')); }
            writer.WriteEndArray();
            writer.WriteBoolean("publish", publish);
            writer.WriteStartArray("outputs");
            foreach (var output in outputs) { writer.WriteStringValue(output.Replace('\\', '/')); }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        if (buffer.WrittenCount >= MaximumBytes) { throw Invalid(); }
        return [.. buffer.WrittenSpan, (byte)'\n'];
    }

    private static async Task<ImmutableArray<WorkspaceRelativePath>> EnumerateAsync(
        IWorkspaceFileSystem workspace, CancellationToken cancellationToken)
    {
        if (workspace is IBoundedWorkspaceEnumerator bounded)
        {
            var tree = await bounded.EnumerateTreeBoundedAsync(null, AtomicProjectFinalizer.MaximumFileCount,
                AtomicProjectFinalizer.MaximumDirectoryCount, AtomicProjectFinalizer.MaximumPathDepth, cancellationToken).ConfigureAwait(false);
            if (tree.LimitExceeded) { throw Invalid(); }
            return tree.Files;
        }
        var files = await workspace.EnumerateAllFilesAsync(cancellationToken).ConfigureAwait(false);
        return files.Length <= AtomicProjectFinalizer.MaximumFileCount ? files : throw Invalid();
    }

    private static InfrastructureOperationException Invalid() => new("DF-GIT-004",
        "The engine-owned build-output membership is invalid.");
}
