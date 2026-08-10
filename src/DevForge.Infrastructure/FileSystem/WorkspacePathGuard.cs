using DevForge.Application.Contracts;

namespace DevForge.Infrastructure.FileSystem;

internal sealed class WorkspacePathGuard
{
    private readonly string _rootPath;
    private readonly string _rootPrefix;

    private WorkspacePathGuard(string rootPath)
    {
        _rootPath = rootPath;
        _rootPrefix = rootPath + Path.DirectorySeparatorChar;
    }

    public string RootPath => _rootPath;

    public static WorkspacePathGuard Open(WorkspaceRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var rootPath = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(root.RevealForFileSystem()));
        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException();
        }

        RejectReparsePoint(rootPath);
        return new WorkspacePathGuard(rootPath);
    }

    public string Resolve(WorkspaceRelativePath path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var relativePath = path.RevealForFileSystem();
        var candidate = Path.GetFullPath(Path.Combine(_rootPath, relativePath));
        if (!candidate.StartsWith(_rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new WorkspaceContainmentException();
        }

        var current = _rootPath;
        foreach (var segment in relativePath.Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            if (!File.Exists(current) && !Directory.Exists(current))
            {
                break;
            }

            RejectReparsePoint(current);
        }

        return candidate;
    }

    public void VerifyExisting(string fullPath)
    {
        if (!IsContained(fullPath) || (!File.Exists(fullPath) && !Directory.Exists(fullPath)))
        {
            throw new WorkspaceContainmentException();
        }

        RejectReparsePoint(fullPath);
    }

    public WorkspaceRelativePath ToRelative(string fullPath)
    {
        if (!IsContained(fullPath))
        {
            throw new WorkspaceContainmentException();
        }

        var relative = Path.GetRelativePath(_rootPath, fullPath);
        var result = WorkspaceRelativePath.Create(relative);
        if (!result.IsValid)
        {
            throw new WorkspaceContainmentException();
        }

        return result.Value;
    }

    public void VerifyTreeHasNoReparsePoints(string directory, CancellationToken cancellationToken)
    {
        VerifyExisting(directory);
        var pending = new Stack<string>();
        pending.Push(directory);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                cancellationToken.ThrowIfCancellationRequested();
                VerifyExisting(entry);
                if (Directory.Exists(entry))
                {
                    pending.Push(entry);
                }
            }
        }
    }

    private bool IsContained(string fullPath)
    {
        var canonical = Path.GetFullPath(fullPath);
        return canonical.StartsWith(_rootPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static void RejectReparsePoint(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new WorkspaceContainmentException();
        }
    }
}

internal sealed class WorkspaceContainmentException : Exception;
