using System.ComponentModel;
using System.Runtime.InteropServices;
using DevForge.Application.Contracts;
using Microsoft.Win32.SafeHandles;

namespace DevForge.Infrastructure.FileSystem;

/// <summary>
/// Provisions a local directory chain while holding non-delete-shared handles to every
/// verified component. This prevents a same-user process from replacing an ancestor with
/// a reparse point between validation and creation of the next component.
/// </summary>
internal sealed class WindowsDirectoryProvisioningLease : IDisposable
{
    private const int ErrorAlreadyExists = 183;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const int FileAttributeTagInfoClass = 9;

    private readonly List<SafeFileHandle> _handles;

    private WindowsDirectoryProvisioningLease(List<SafeFileHandle> handles)
    {
        _handles = handles;
    }

    public static WindowsDirectoryProvisioningLease Acquire(WorkspaceRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var targetPath = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(root.RevealForFileSystem()));
        var driveRoot = Path.GetPathRoot(targetPath);
        if (string.IsNullOrEmpty(driveRoot))
        {
            throw new IOException();
        }

        var handles = new List<SafeFileHandle>();
        try
        {
            var currentPath = Path.TrimEndingDirectorySeparator(driveRoot);
            handles.Add(OpenVerifiedDirectory(currentPath));

            var relativePath = Path.GetRelativePath(driveRoot, targetPath);
            if (!relativePath.Equals(".", StringComparison.Ordinal))
            {
                foreach (var segment in relativePath.Split(Path.DirectorySeparatorChar))
                {
                    currentPath = Path.Combine(currentPath, segment);
                    CreateDirectoryIfMissing(currentPath);
                    handles.Add(OpenVerifiedDirectory(currentPath));
                }
            }

            return new WindowsDirectoryProvisioningLease(handles);
        }
        catch
        {
            DisposeHandles(handles);
            throw;
        }
    }

    public void Dispose()
    {
        DisposeHandles(_handles);
    }

    private static void CreateDirectoryIfMissing(string path)
    {
        if (CreateDirectory(path, IntPtr.Zero))
        {
            return;
        }

        var error = Marshal.GetLastPInvokeError();
        if (error != ErrorAlreadyExists)
        {
            throw new IOException("Directory provisioning failed.", new Win32Exception(error));
        }
    }

    private static SafeFileHandle OpenVerifiedDirectory(string path)
    {
        var handle = CreateFile(
            path,
            desiredAccess: 0,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            FileFlagOpenReparsePoint | FileFlagBackupSemantics,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new IOException("Directory verification failed.", new Win32Exception(error));
        }

        if (!GetFileInformationByHandleEx(
                handle,
                FileAttributeTagInfoClass,
                out var information,
                checked((uint)Marshal.SizeOf<FileAttributeTagInfo>())))
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new IOException("Directory verification failed.", new Win32Exception(error));
        }

        const FileAttributes prohibited = FileAttributes.ReparsePoint;
        if ((information.FileAttributes & FileAttributes.Directory) == 0
            || (information.FileAttributes & prohibited) != 0)
        {
            handle.Dispose();
            throw new WorkspaceContainmentException();
        }

        return handle;
    }

    private static void DisposeHandles(List<SafeFileHandle> handles)
    {
        for (var index = handles.Count - 1; index >= 0; index--)
        {
            handles[index].Dispose();
        }

        handles.Clear();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileAttributeTagInfo
    {
        public FileAttributes FileAttributes;
        public uint ReparseTag;
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
    private static extern bool CreateDirectory(string path, IntPtr securityAttributes);

    [DllImport(
        "kernel32.dll",
        EntryPoint = "CreateFileW",
        SetLastError = true,
        CharSet = CharSet.Unicode,
        ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle file,
        int fileInformationClass,
        out FileAttributeTagInfo fileInformation,
        uint bufferSize);
#pragma warning restore SYSLIB1054
}
