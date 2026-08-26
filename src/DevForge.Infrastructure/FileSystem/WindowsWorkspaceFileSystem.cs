using System.Collections.Immutable;
using System.Runtime.InteropServices;
using DevForge.Application.Contracts;

namespace DevForge.Infrastructure.FileSystem;

internal sealed class WindowsWorkspaceFileSystem :
    IAtomicWorkspaceFileSystem,
    IAtomicFileWorkspaceFileSystem,
    IExclusiveLeaseWorkspaceFileSystem,
    IWorkspaceFileMetadataFileSystem,
    IBoundedWorkspaceEnumerator
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

    public Task<WorkspaceFileMetadata?> GetFileMetadataAsync(
        WorkspaceRelativePath path,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        return ExecuteAsync<WorkspaceFileMetadata?>(
            () =>
            {
                var fullPath = _guard.Resolve(path);
                if (!File.Exists(fullPath))
                {
                    return null;
                }

                _guard.VerifyExisting(fullPath);
                var information = new FileInfo(fullPath);
                information.Refresh();
                if (!information.Exists)
                {
                    return null;
                }

                _guard.VerifyExisting(fullPath);
                return new WorkspaceFileMetadata(
                    path,
                    information.Length,
                    new DateTimeOffset(information.LastWriteTimeUtc));
            },
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

    public Task<IWorkspaceExclusiveLease?> TryAcquireExclusiveLeaseAsync(
        WorkspaceRelativePath path,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var fullPath = _guard.Resolve(path);
            var parentPath = Path.GetDirectoryName(fullPath);
            if (parentPath is null || !Directory.Exists(parentPath))
            {
                throw new IOException();
            }

            _guard.VerifyExisting(parentPath);
            FileStream stream;
            try
            {
                stream = new FileStream(
                    fullPath,
                    new FileStreamOptions
                    {
                        Access = FileAccess.ReadWrite,
                        Mode = FileMode.OpenOrCreate,
                        Share = FileShare.None,
                        Options = FileOptions.Asynchronous
                            | FileOptions.WriteThrough
                            | FileOptions.DeleteOnClose,
                    });
            }
            catch (IOException exception) when (IsSharingViolation(exception))
            {
                return Task.FromResult<IWorkspaceExclusiveLease?>(null);
            }

            try
            {
                var verified = _guard.Resolve(path);
                if (!StringComparer.OrdinalIgnoreCase.Equals(fullPath, verified))
                {
                    throw new WorkspaceContainmentException();
                }

                _guard.VerifyExisting(fullPath);
                return Task.FromResult<IWorkspaceExclusiveLease?>(
                    new WorkspaceExclusiveLease(stream));
            }
            catch
            {
                stream.Dispose();
                throw;
            }
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
                "The guarded workspace lease could not be acquired.");
        }
    }

    private static bool IsSharingViolation(IOException exception)
    {
        var code = exception.HResult & 0xFFFF;
        return code is 32 or 33;
    }

    private sealed class WorkspaceExclusiveLease(FileStream stream) : IWorkspaceExclusiveLease
    {
        private FileStream? _stream = stream;

        public async ValueTask DisposeAsync()
        {
            var owned = Interlocked.Exchange(ref _stream, null);
            if (owned is not null)
            {
                await owned.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task WriteFileAtomicallyAsync(
        WorkspaceRelativePath path,
        ReadOnlyMemory<byte> content,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        cancellationToken.ThrowIfCancellationRequested();
        string? temporaryPath = null;
        WorkspaceRelativePath? temporaryRelativePath = null;
        var ownsTemporary = false;
        try
        {
            var destinationPath = _guard.Resolve(path);
            var parentPath = Path.GetDirectoryName(destinationPath);
            if (parentPath is null || !Directory.Exists(parentPath))
            {
                throw new IOException();
            }

            _guard.VerifyExisting(parentPath);
            var separator = path.Value.LastIndexOf('\\');
            var prefix = separator < 0 ? string.Empty : path.Value[..(separator + 1)];
            temporaryRelativePath = WorkspaceRelativePath.Create(
                $"{prefix}.devforge-{Guid.NewGuid():N}.tmp").Value;
            temporaryPath = _guard.Resolve(temporaryRelativePath);
            await using (var stream = new FileStream(
                temporaryPath,
                new FileStreamOptions
                {
                    Access = FileAccess.Write,
                    Mode = FileMode.CreateNew,
                    Share = FileShare.None,
                    Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
                }))
            {
                ownsTemporary = true;
                var verifiedTemporary = _guard.Resolve(temporaryRelativePath);
                if (!StringComparer.OrdinalIgnoreCase.Equals(temporaryPath, verifiedTemporary))
                {
                    throw new WorkspaceContainmentException();
                }

                _guard.VerifyExisting(temporaryPath);
                await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var verifiedDestination = _guard.Resolve(path);
            var finalTemporary = _guard.Resolve(temporaryRelativePath);
            if (!StringComparer.OrdinalIgnoreCase.Equals(destinationPath, verifiedDestination)
                || !StringComparer.OrdinalIgnoreCase.Equals(temporaryPath, finalTemporary)
                || !overwrite && (File.Exists(destinationPath) || Directory.Exists(destinationPath)))
            {
                throw new IOException();
            }

            File.Move(temporaryPath, destinationPath, overwrite);
            ownsTemporary = false;
        }
        catch (OperationCanceledException)
        {
            throw;
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
        finally
        {
            if (ownsTemporary && temporaryPath is not null && temporaryRelativePath is not null)
            {
                TryDeleteOwnedTemporary(temporaryRelativePath, temporaryPath);
            }
        }
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

    public Task<BoundedWorkspaceEnumeration> EnumerateTreeBoundedAsync(
        WorkspaceRelativePath? excludedRootDirectory,
        int maximumFiles,
        int maximumDirectories,
        int maximumDepth,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            () => EnumerateTreeBounded(
                excludedRootDirectory,
                maximumFiles,
                maximumDirectories,
                maximumDepth,
                cancellationToken),
            cancellationToken);
    }

    private BoundedWorkspaceEnumeration EnumerateTreeBounded(
        WorkspaceRelativePath? excludedRootDirectory,
        int maximumFiles,
        int maximumDirectories,
        int maximumDepth,
        CancellationToken cancellationToken)
    {
        _guard.VerifyExisting(_guard.RootPath);
        var files = ImmutableArray.CreateBuilder<WorkspaceRelativePath>();
        var directories = ImmutableArray.CreateBuilder<WorkspaceRelativePath>();
        var pending = new Stack<string>();
        pending.Push(_guard.RootPath);
        while (pending.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                cancellationToken.ThrowIfCancellationRequested();
                _guard.VerifyExisting(entry);
                var relative = _guard.ToRelative(entry);
                var depth = relative.Value.Count(character => character == '\\') + 1;
                if (depth > maximumDepth)
                {
                    return new BoundedWorkspaceEnumeration([], [], true);
                }

                if (Directory.Exists(entry))
                {
                    if (excludedRootDirectory is not null
                        && relative.Equals(excludedRootDirectory))
                    {
                        continue;
                    }

                    if (directories.Count >= maximumDirectories)
                    {
                        return new BoundedWorkspaceEnumeration([], [], true);
                    }

                    directories.Add(relative);
                    pending.Push(entry);
                }
                else
                {
                    if (files.Count >= maximumFiles)
                    {
                        return new BoundedWorkspaceEnumeration([], [], true);
                    }

                    files.Add(relative);
                }
            }
        }

        return new BoundedWorkspaceEnumeration(
            files.OrderBy(path => path.Value, StringComparer.Ordinal).ToImmutableArray(),
            directories.OrderBy(path => path.Value, StringComparer.Ordinal).ToImmutableArray(),
            false);
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

    private void TryDeleteOwnedTemporary(
        WorkspaceRelativePath temporaryRelativePath,
        string expectedFullPath)
    {
        try
        {
            var resolved = _guard.Resolve(temporaryRelativePath);
            if (StringComparer.OrdinalIgnoreCase.Equals(resolved, expectedFullPath)
                && File.Exists(resolved))
            {
                _guard.VerifyExisting(resolved);
                File.Delete(resolved);
            }
        }
        catch (Exception exception) when (exception is WorkspaceContainmentException
            || IsExpectedFileSystemFailure(exception))
        {
            // Failing closed may retain a run-owned temporary file; it never justifies deleting
            // through an ancestor whose containment can no longer be proven.
        }
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
