using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using DevForge.Application.Contracts;
using DevForge.Domain.Execution;
using DevForge.Infrastructure.FileSystem;

namespace DevForge.Infrastructure.Execution;

// A disposable validation snapshot, never part of the finalized project tree.
internal sealed class NodeExecutionWorkspace
{
    internal const int MaximumFiles = 65_536;
    internal const int MaximumDirectories = 16_384;
    internal const int MaximumDepth = 48;
    internal const long MaximumToolingBytes = 2L * 1024 * 1024 * 1024;
    private static readonly string[] _outputRoots = ["node_modules", ".devforge-node", ".next", "dist"];
    private static readonly string[] _configurationFiles = [".npmrc", ".pnpmfile.cjs", "pnpm-workspace.yaml"];
    private static readonly WorkspaceRelativePath _snapshotPath = Relative("tooling/node/source.sha256");
    private readonly StagingWorkspace _staging;
    private readonly string _sourceDigest;
    private readonly bool _exportStaticDist;

    private NodeExecutionWorkspace(StagingWorkspace staging, IWorkspaceFileSystem project, string sourceDigest, bool exportStaticDist)
    {
        _staging = staging;
        Project = project;
        _sourceDigest = sourceDigest;
        _exportStaticDist = exportStaticDist;
    }

    public IWorkspaceFileSystem Project { get; }

    public static bool UsesPnpm(ExecutionPlan plan) => plan.Steps.Any(step =>
        step.Handler == "package-install" && step.Inputs.TryGetValue("packageManager", out var tool) && tool.StringValue == "pnpm"
        && step.Inputs.TryGetValue("workingDirectory", out var directory) && directory.StringValue == ".");

    public static async Task<NodeExecutionWorkspace> OpenAsync(StagingWorkspace staging, CancellationToken cancellationToken, bool exportStaticDist = false)
    {
        var container = staging.ContainerWorkspace ?? throw Invalid();
        var sourceTree = await EnumerateAsync(staging.PayloadWorkspace, false, cancellationToken).ConfigureAwait(false);
        if (exportStaticDist && await container.FileExistsAsync(_snapshotPath, cancellationToken).ConfigureAwait(false))
        {
            sourceTree = WithoutDist(sourceTree);
        }
        if (sourceTree.Files.Concat(sourceTree.Directories).Any(path => IsOutput(path.Value)
            || path.Value.Split('\\').Any(segment => segment.Equals(".git", StringComparison.OrdinalIgnoreCase)
                || segment.Equals(".devforge", StringComparison.OrdinalIgnoreCase)
                || segment.StartsWith(".env", StringComparison.OrdinalIgnoreCase) && segment != ".env.example")
            || _configurationFiles.Contains(System.IO.Path.GetFileName(path.Value), StringComparer.OrdinalIgnoreCase)))
        {
            throw Invalid();
        }
        var sourceDigest = await DigestAsync(staging.PayloadWorkspace, sourceTree.Files, cancellationToken).ConfigureAwait(false);
        await container.CreateDirectoryAsync(Relative("tooling/node/project"), cancellationToken).ConfigureAwait(false);
        var project = await new WindowsFileSystem().OpenWorkspaceAsync(WorkspaceRoot.Create(
            System.IO.Path.Combine(container.Root.RevealForFileSystem(), "tooling", "node", "project")).Value,
            cancellationToken).ConfigureAwait(false);
        var instance = new NodeExecutionWorkspace(staging, project, sourceDigest, exportStaticDist);
        if (await container.FileExistsAsync(_snapshotPath, cancellationToken).ConfigureAwait(false))
        {
            await using var recorded = await container.OpenReadAsync(_snapshotPath, cancellationToken).ConfigureAwait(false);
            if (recorded.Length != 71) { throw Invalid(); }
            var bytes = new byte[71];
            await recorded.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            if (Encoding.UTF8.GetString(bytes) != sourceDigest) { throw Invalid(); }
        }
        else
        {
            // Only an incomplete, byte-identical copy can be resumed before the marker exists.
            var existing = await EnumerateAsync(project, false, cancellationToken).ConfigureAwait(false);
            if (existing.Files.Any(file => !sourceTree.Files.Contains(file))
                || existing.Directories.Any(directory => !sourceTree.Directories.Contains(directory))) { throw Invalid(); }
            foreach (var directory in sourceTree.Directories)
            {
                await project.CreateDirectoryAsync(directory, cancellationToken).ConfigureAwait(false);
            }
            foreach (var path in sourceTree.Files)
            {
                await using var source = await staging.PayloadWorkspace.OpenReadAsync(path, cancellationToken).ConfigureAwait(false);
                if (source.Length > AtomicProjectFinalizer.MaximumFileBytes) { throw Invalid(); }
                var bytes = new byte[checked((int)source.Length)];
                await source.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
                if (!await project.FileExistsAsync(path, cancellationToken).ConfigureAwait(false))
                {
                    await ((IAtomicFileWorkspaceFileSystem)project).WriteFileAtomicallyAsync(path, bytes, false, cancellationToken).ConfigureAwait(false);
                }
            }
            await instance.VerifyAsync(cancellationToken).ConfigureAwait(false);
            await ((IAtomicFileWorkspaceFileSystem)container).WriteFileAtomicallyAsync(_snapshotPath,
                Encoding.UTF8.GetBytes(sourceDigest), false, cancellationToken).ConfigureAwait(false);
        }
        await instance.VerifyAsync(cancellationToken).ConfigureAwait(false);
        return instance;
    }

    public async Task<string> VerifyAsync(CancellationToken cancellationToken)
    {
        var source = await EnumerateAsync(_staging.PayloadWorkspace, false, cancellationToken).ConfigureAwait(false);
        var transferred = source.Files.Where(path => IsDist(path.Value)).ToImmutableArray();
        if (_exportStaticDist) { source = WithoutDist(source); }
        if (await DigestAsync(_staging.PayloadWorkspace, source.Files, cancellationToken).ConfigureAwait(false) != _sourceDigest) { throw Invalid(); }
        var tooling = await EnumerateAsync(Project, true, cancellationToken).ConfigureAwait(false);
        if (_exportStaticDist && !transferred.IsEmpty
            && await DigestAsync(_staging.PayloadWorkspace, transferred, cancellationToken).ConfigureAwait(false)
                != await DigestAsync(Project, transferred, cancellationToken).ConfigureAwait(false))
        {
            throw Invalid();
        }
        var sourceFiles = tooling.Files.Where(path => !IsOutput(path.Value)).ToImmutableArray();
        if (!sourceFiles.OrderBy(path => path.Value, StringComparer.Ordinal).SequenceEqual(source.Files.OrderBy(path => path.Value, StringComparer.Ordinal))
            || tooling.Directories.Any(path => !IsOutput(path.Value) && !source.Directories.Contains(path))
            || await DigestAsync(Project, sourceFiles, cancellationToken).ConfigureAwait(false) != _sourceDigest)
        {
            throw Invalid();
        }
        // Do not hash mutable package-manager caches into source identity. Hash actual build
        // artifacts and source into per-command evidence; dependencies remain bounded tooling.
        var evidence = tooling.Files.Where(path => !path.Value.StartsWith("node_modules\\", StringComparison.Ordinal)
            && !path.Value.StartsWith(".devforge-node\\", StringComparison.Ordinal)).ToImmutableArray();
        return await DigestAsync(Project, evidence, cancellationToken).ConfigureAwait(false);
    }

    public async Task ExportStaticDistAsync(CancellationToken cancellationToken)
    {
        if (!_exportStaticDist) { return; }
        var tree = await EnumerateAsync(Project, true, cancellationToken).ConfigureAwait(false);
        if (!tree.Files.Contains(Relative("dist/index.html"))) { throw Invalid(); }
        foreach (var directory in tree.Directories.Where(path => IsDist(path.Value)))
        {
            await _staging.PayloadWorkspace.CreateDirectoryAsync(directory, cancellationToken).ConfigureAwait(false);
        }
        foreach (var path in tree.Files.Where(path => IsDist(path.Value)))
        {
            await using var source = await Project.OpenReadAsync(path, cancellationToken).ConfigureAwait(false);
            if (source.Length > AtomicProjectFinalizer.MaximumFileBytes) { throw Invalid(); }
            var bytes = new byte[checked((int)source.Length)];
            await source.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            if (await _staging.PayloadWorkspace.FileExistsAsync(path, cancellationToken).ConfigureAwait(false))
            {
                await using var existing = await _staging.PayloadWorkspace.OpenReadAsync(path, cancellationToken).ConfigureAwait(false);
                if (existing.Length != bytes.Length || !(await SHA256.HashDataAsync(existing, cancellationToken).ConfigureAwait(false)).AsSpan()
                    .SequenceEqual(SHA256.HashData(bytes))) { throw Invalid(); }
            }
            else
            {
                await ((IAtomicFileWorkspaceFileSystem)_staging.PayloadWorkspace).WriteFileAtomicallyAsync(path, bytes, false, cancellationToken).ConfigureAwait(false);
            }
        }
        await VerifyAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool IsDist(string path) => path == "dist" || path.StartsWith("dist\\", StringComparison.Ordinal);
    private static BoundedWorkspaceEnumeration WithoutDist(BoundedWorkspaceEnumeration tree) => new(
        [.. tree.Files.Where(path => !IsDist(path.Value))], [.. tree.Directories.Where(path => !IsDist(path.Value))], tree.LimitExceeded);

    private static bool IsOutput(string path) => _outputRoots.Any(root => path.Equals(root, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase));

    private static async Task<BoundedWorkspaceEnumeration> EnumerateAsync(IWorkspaceFileSystem workspace, bool tooling, CancellationToken cancellationToken)
    {
        if (workspace is not IBoundedWorkspaceEnumerator bounded || workspace is not IWorkspaceFileMetadataFileSystem metadata) { throw Invalid(); }
        var tree = await bounded.EnumerateTreeBoundedAsync(null,
            tooling ? MaximumFiles : AtomicProjectFinalizer.MaximumFileCount,
            tooling ? MaximumDirectories : AtomicProjectFinalizer.MaximumDirectoryCount,
            tooling ? MaximumDepth : AtomicProjectFinalizer.MaximumPathDepth, cancellationToken).ConfigureAwait(false);
        if (tree.LimitExceeded) { throw Invalid(); }
        long total = 0;
        foreach (var path in tree.Files)
        {
            var file = await metadata.GetFileMetadataAsync(path, cancellationToken).ConfigureAwait(false) ?? throw Invalid();
            total = checked(total + file.Length);
            if (file.Length > (tooling ? MaximumToolingBytes : AtomicProjectFinalizer.MaximumFileBytes)
                || total > (tooling ? MaximumToolingBytes : AtomicProjectFinalizer.MaximumAggregateBytes)) { throw Invalid(); }
        }
        return tree;
    }

    private static async Task<string> DigestAsync(IWorkspaceFileSystem workspace, ImmutableArray<WorkspaceRelativePath> files, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in files.OrderBy(path => path.Value, StringComparer.Ordinal))
        {
            var name = Encoding.UTF8.GetBytes(path.Value.Replace('\\', '/'));
            var length = new byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(length, name.Length);
            hash.AppendData(length);
            hash.AppendData(name);
            await using var stream = await workspace.OpenReadAsync(path, cancellationToken).ConfigureAwait(false);
            BinaryPrimitives.WriteInt64LittleEndian(length, stream.Length);
            hash.AppendData(length);
            hash.AppendData(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        }
        return "sha256:" + Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static WorkspaceRelativePath Relative(string path) => WorkspaceRelativePath.Create(path.Replace('/', '\\')).Value;
    private static InfrastructureOperationException Invalid() => new("DF-PROC-002", "The Node source snapshot or bounded tooling workspace failed integrity verification.");
}
