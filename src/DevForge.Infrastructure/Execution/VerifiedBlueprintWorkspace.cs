using System.Collections.Immutable;
using DevForge.Application.Contracts;

namespace DevForge.Infrastructure.Execution;

internal sealed class VerifiedBlueprintWorkspace : IWorkspaceFileSystem
{
    private readonly ImmutableDictionary<string, ImmutableArray<byte>> _files;

    private VerifiedBlueprintWorkspace(
        WorkspaceRoot root,
        ImmutableDictionary<string, ImmutableArray<byte>> files)
    {
        Root = root;
        _files = files;
    }

    public WorkspaceRoot Root { get; }

    public static VerifiedBlueprintWorkspace Create(
        string aggregateChecksum,
        ImmutableDictionary<string, ImmutableArray<byte>> verifiedFiles,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(aggregateChecksum);
        ArgumentNullException.ThrowIfNull(verifiedFiles);
        cancellationToken.ThrowIfCancellationRequested();
        var builder = ImmutableDictionary.CreateBuilder<string, ImmutableArray<byte>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var file in verifiedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = WorkspaceRelativePath.Create(file.Key.Replace('/', '\\'));
            if (!path.IsValid
                || file.Value.IsDefault
                || !builder.TryAdd(path.Value.Value, [.. file.Value]))
            {
                throw new InvalidDataException("The verified blueprint byte snapshot is inconsistent.");
            }
        }

        var digest = aggregateChecksum.StartsWith("sha256:", StringComparison.Ordinal)
            ? aggregateChecksum["sha256:".Length..]
            : throw new InvalidDataException("The verified blueprint checksum is inconsistent.");
        var root = WorkspaceRoot.Create($"C:\\DevForge\\VerifiedBlueprint\\{digest}");
        if (!root.IsValid)
        {
            throw new InvalidDataException("The verified blueprint workspace identity is inconsistent.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new VerifiedBlueprintWorkspace(root.Value, builder.ToImmutable());
    }

    public Task<bool> FileExistsAsync(
        WorkspaceRelativePath path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_files.ContainsKey(path.Value));
    }

    public Task<bool> DirectoryExistsAsync(
        WorkspaceRelativePath path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var prefix = path.Value + '\\';
        return Task.FromResult(_files.Keys.Any(item =>
            item.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
    }

    public Task<Stream> OpenReadAsync(
        WorkspaceRelativePath path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _files.TryGetValue(path.Value, out var content)
            ? Task.FromResult<Stream>(new MemoryStream(content.ToArray(), writable: false))
            : Task.FromException<Stream>(ReadFailure());
    }

    public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateAllFilesAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_files.Keys
            .OrderBy(item => item, StringComparer.Ordinal)
            .Select(ToPath)
            .ToImmutableArray());
    }

    public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateRootDirectoriesAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_files.Keys
            .Where(item => item.Contains('\\'))
            .Select(item => item[..item.IndexOf('\\')])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.Ordinal)
            .Select(ToPath)
            .ToImmutableArray());
    }

    public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateFilesAsync(
        WorkspaceRelativePath directory,
        bool recursive,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var prefix = directory.Value + '\\';
        var files = _files.Keys
            .Where(item => item.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Where(item => recursive || !item[prefix.Length..].Contains('\\'))
            .OrderBy(item => item, StringComparer.Ordinal)
            .Select(ToPath)
            .ToImmutableArray();
        return Task.FromResult(files);
    }

    public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateDirectoriesAsync(
        WorkspaceRelativePath directory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var prefix = directory.Value + '\\';
        var directories = _files.Keys
            .Where(item => item.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(item => item[prefix.Length..])
            .Where(item => item.Contains('\\'))
            .Select(item => prefix + item[..item.IndexOf('\\')])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.Ordinal)
            .Select(ToPath)
            .ToImmutableArray();
        return Task.FromResult(directories);
    }

    public Task CreateDirectoryAsync(
        WorkspaceRelativePath path,
        CancellationToken cancellationToken) => ReadOnly(cancellationToken);

    public Task<Stream> OpenWriteAsync(
        WorkspaceRelativePath path,
        bool overwrite,
        CancellationToken cancellationToken) => ReadOnly<Stream>(cancellationToken);

    public Task DeleteFileAsync(
        WorkspaceRelativePath path,
        CancellationToken cancellationToken) => ReadOnly(cancellationToken);

    public Task DeleteDirectoryAsync(
        WorkspaceRelativePath path,
        DirectoryCleanupIntent intent,
        CancellationToken cancellationToken) => ReadOnly(cancellationToken);

    public Task MoveDirectoryAsync(
        WorkspaceRelativePath source,
        WorkspaceRelativePath destination,
        WorkspaceMoveIntent intent,
        CancellationToken cancellationToken) => ReadOnly(cancellationToken);

    private static WorkspaceRelativePath ToPath(string value)
    {
        var result = WorkspaceRelativePath.Create(value);
        return result.IsValid
            ? result.Value
            : throw new InvalidDataException("The verified blueprint path snapshot is inconsistent.");
    }

    private static Task ReadOnly(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromException(ReadFailure());
    }

    private static Task<T> ReadOnly<T>(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromException<T>(ReadFailure());
    }

    private static InfrastructureOperationException ReadFailure() => new(
        "DF-FS-002",
        "The verified blueprint workspace is read-only.");
}
