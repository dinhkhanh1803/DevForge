using System.Collections.Immutable;
using System.Runtime.InteropServices;
using DevForge.Application.Contracts;

namespace DevForge.Infrastructure.FileSystem;

internal sealed class WindowsWorkspaceFileSystem : IAtomicWorkspaceFileSystem
{
    private const int ErrorFileExists = 80;
    private const int ErrorAlreadyExists = 183;
    private readonly WorkspacePathGuard _guard;

    public WindowsWorkspaceFileSystem(WorkspaceRoot root, WorkspacePathGuard guard)
    {
        Root = root;
        _guard = guard;
    }

    public WorkspaceRoot Root { get; }

    public Task<bool> FileExistsAsync(
        WorkspaceRelativePath path,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            () => File.Exists(_guard.Resolve(path)),
            cancellationToken);
    }

    public Task<bool> DirectoryExistsAsync(
        WorkspaceRelativePath path,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            () => Directory.Exists(_guard.Resolve(path)),
            cancellationToken);
    }

    public Task CreateDirectoryAsync(
        WorkspaceRelativePath path,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            () =>
            {
                var fullPath = _guard.Resolve(path);
                Directory.CreateDirectory(fullPath);
                _guard.VerifyExisting(fullPath);
            },
            cancellationToken);
    }

    public Task<bool> TryCreateDirectoryAsync(
        WorkspaceRelativePath path,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            () =>
            {
                var fullPath = _guard.Resolve(path);
                if (CreateDirectoryNative(fullPath, IntPtr.Zero))
                {
                    var verifiedPath = _guard.Resolve(path);
                    if (!StringComparer.OrdinalIgnoreCase.Equals(fullPath, verifiedPath))
                    {
                        throw new IOException();
                    }

                    _guard.VerifyExisting(fullPath);
                    return true;
                }

                var error = Marshal.GetLastWin32Error();
                if (error is ErrorFileExists or ErrorAlreadyExists)
                {
                    return false;
                }

                throw new IOException();
            },
            cancellationToken);
    }

    public Task<Stream> OpenReadAsync(
        WorkspaceRelativePath path,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync<Stream>(
            () =>
            {
                var fullPath = _guard.Resolve(path);
                _guard.VerifyExisting(fullPath);
                if (Directory.Exists(fullPath))
                {
                    throw new IOException();
                }

                return new FileStream(
                    fullPath,
                    new FileStreamOptions
                    {
                        Access = FileAccess.Read,
                        Mode = FileMode.Open,
                        Share = FileShare.Read,
                        Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                    });
            },
            cancellationToken);
    }

    public Task<Stream> OpenWriteAsync(
        WorkspaceRelativePath path,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync<Stream>(
            () =>
            {
                var fullPath = _guard.Resolve(path);
                var stream = new FileStream(
                    fullPath,
                    new FileStreamOptions
                    {
                        Access = FileAccess.Write,
                        Mode = overwrite ? FileMode.Create : FileMode.CreateNew,
                        Share = FileShare.None,
                        Options = FileOptions.Asynchronous,
                    });
                try
                {
                    _guard.VerifyExisting(fullPath);
                    return stream;
                }
                catch
                {
                    stream.Dispose();
                    throw;
                }
            },
            cancellationToken);
    }

    public Task DeleteFileAsync(
        WorkspaceRelativePath path,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            () =>
            {
                var fullPath = _guard.Resolve(path);
                _guard.VerifyExisting(fullPath);
                if (Directory.Exists(fullPath))
                {
                    throw new IOException();
                }

                File.Delete(fullPath);
            },
            cancellationToken);
    }

    public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateFilesAsync(
        WorkspaceRelativePath directory,
        bool recursive,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            () => EnumerateFiles(_guard.Resolve(directory), recursive, cancellationToken),
            cancellationToken);
    }

    public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateAllFilesAsync(
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            () => EnumerateFiles(_guard.RootPath, recursive: true, cancellationToken),
            cancellationToken);
    }

    public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateRootDirectoriesAsync(
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            () => EnumerateDirectories(_guard.RootPath, cancellationToken),
            cancellationToken);
    }

    public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateDirectoriesAsync(
        WorkspaceRelativePath directory,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            () => EnumerateDirectories(_guard.Resolve(directory), cancellationToken),
            cancellationToken);
    }

    public Task DeleteDirectoryAsync(
        WorkspaceRelativePath path,
        DirectoryCleanupIntent intent,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            () =>
            {
                if (intent != DirectoryCleanupIntent.RecursiveRunOwned)
                {
                    throw new IOException();
                }

                var fullPath = _guard.Resolve(path);
                if (!Directory.Exists(fullPath))
                {
                    throw new DirectoryNotFoundException();
                }

                _guard.VerifyTreeHasNoReparsePoints(fullPath, cancellationToken);
                Directory.Delete(fullPath, recursive: true);
            },
            cancellationToken);
    }

    public Task MoveDirectoryAsync(
        WorkspaceRelativePath source,
        WorkspaceRelativePath destination,
        WorkspaceMoveIntent intent,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            () =>
            {
                if (intent != WorkspaceMoveIntent.AtomicNoOverwriteFinalize)
                {
                    throw new IOException();
                }

                var sourcePath = _guard.Resolve(source);
                var destinationPath = _guard.Resolve(destination);
                if (!Directory.Exists(sourcePath)
                    || Directory.Exists(destinationPath)
                    || File.Exists(destinationPath))
                {
                    throw new IOException();
                }

                _guard.VerifyTreeHasNoReparsePoints(sourcePath, cancellationToken);
                Directory.Move(sourcePath, destinationPath);
                _guard.VerifyExisting(destinationPath);
            },
            cancellationToken);
    }

    internal static bool IsExpectedFileSystemFailure(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException
            or NotSupportedException
            or ArgumentException;
    }

    private ImmutableArray<WorkspaceRelativePath> EnumerateFiles(
        string root,
        bool recursive,
        CancellationToken cancellationToken)
    {
        _guard.VerifyExisting(root);
        if (!Directory.Exists(root))
        {
            throw new IOException();
        }

        var files = ImmutableArray.CreateBuilder<WorkspaceRelativePath>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                cancellationToken.ThrowIfCancellationRequested();
                _guard.VerifyExisting(entry);
                if (Directory.Exists(entry))
                {
                    if (recursive)
                    {
                        pending.Push(entry);
                    }

                    continue;
                }

                files.Add(_guard.ToRelative(entry));
            }
        }

        return files
            .OrderBy(path => path.Value, StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();
    }

    private ImmutableArray<WorkspaceRelativePath> EnumerateDirectories(
        string directory,
        CancellationToken cancellationToken)
    {
        _guard.VerifyExisting(directory);
        if (!Directory.Exists(directory))
        {
            throw new IOException();
        }

        var directories = ImmutableArray.CreateBuilder<WorkspaceRelativePath>();
        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            _guard.VerifyExisting(child);
            directories.Add(_guard.ToRelative(child));
        }

        return directories
            .OrderBy(path => path.Value, StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();
    }

    private static Task ExecuteAsync(Action action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            action();
            return Task.CompletedTask;
        }
        catch (WorkspaceContainmentException)
        {
            throw new InfrastructureOperationException(
                "DF-FS-003",
                "Workspace containment could not be proven.");
        }
        catch (Exception exception) when (IsExpectedFileSystemFailure(exception))
        {
            throw new InfrastructureOperationException(
                "DF-FS-002",
                "The guarded workspace operation could not be completed.");
        }
    }

    private static Task<T> ExecuteAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return Task.FromResult(action());
        }
        catch (WorkspaceContainmentException)
        {
            throw new InfrastructureOperationException(
                "DF-FS-003",
                "Workspace containment could not be proven.");
        }
        catch (Exception exception) when (IsExpectedFileSystemFailure(exception))
        {
            throw new InfrastructureOperationException(
                "DF-FS-002",
                "The guarded workspace operation could not be completed.");
        }
    }

    // DllImport is intentionally isolated here so Infrastructure does not enable unsafe code assembly-wide.
#pragma warning disable SYSLIB1054
    [DllImport(
        "kernel32.dll",
        EntryPoint = "CreateDirectoryW",
        SetLastError = true,
        CharSet = CharSet.Unicode,
        ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateDirectoryNative(
        string path,
        IntPtr securityAttributes);
#pragma warning restore SYSLIB1054
}
