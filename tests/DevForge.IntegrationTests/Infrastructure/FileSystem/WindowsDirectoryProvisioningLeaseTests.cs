using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using DevForge.Application.Contracts;
using DevForge.Infrastructure.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace DevForge.IntegrationTests.Infrastructure.FileSystem;

public sealed partial class WindowsDirectoryProvisioningLeaseTests : IDisposable
{
    private readonly string _container = Path.Combine(
        Path.GetTempPath(),
        "DevForge-M10-Provision-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void HoldsVerifiedAncestorsAgainstReplacementUntilDisposed()
    {
        var target = Path.Combine(_container, "local", "data");
        var root = WorkspaceRoot.Create(target).Value;

        using (WindowsDirectoryProvisioningLease.Acquire(root))
        {
            Assert.True(Directory.Exists(target));
            Assert.ThrowsAny<IOException>(() => Directory.Move(
                Path.Combine(_container, "local"),
                Path.Combine(_container, "replaced")));
        }

        Directory.Move(
            Path.Combine(_container, "local"),
            Path.Combine(_container, "replaced"));
        Assert.True(Directory.Exists(Path.Combine(_container, "replaced", "data")));
    }

    [Fact]
    public void HoldsVerifiedDirectoryAgainstInPlaceReparseConversionUntilDisposed()
    {
        var target = Path.Combine(_container, "local");
        var outside = Path.Combine(_container, "outside");
        Directory.CreateDirectory(outside);
        var root = WorkspaceRoot.Create(target).Value;

        using (WindowsDirectoryProvisioningLease.Acquire(root))
        {
            Assert.False(TryConvertToJunction(target, outside));
        }

        Assert.True(TryConvertToJunction(target, outside));
        Directory.Delete(target);
    }

    public void Dispose()
    {
        var possibleJunction = Path.Combine(_container, "local");
        if (Directory.Exists(possibleJunction)
            && (File.GetAttributes(possibleJunction) & FileAttributes.ReparsePoint) != 0)
        {
            Directory.Delete(possibleJunction);
        }

        if (Directory.Exists(_container))
        {
            Directory.Delete(_container, recursive: true);
        }
    }

    private static bool TryConvertToJunction(string junctionPath, string targetPath)
    {
        const uint genericWrite = 0x40000000;
        const uint fileShareRead = 0x00000001;
        const uint fileShareWrite = 0x00000002;
        const uint fileShareDelete = 0x00000004;
        const uint openExisting = 3;
        const uint fileFlagOpenReparsePoint = 0x00200000;
        const uint fileFlagBackupSemantics = 0x02000000;
        const uint fsctlSetReparsePoint = 0x000900A4;
        const uint ioReparseTagMountPoint = 0xA0000003;

        Directory.CreateDirectory(junctionPath);
        using var handle = CreateFile(
            junctionPath,
            genericWrite,
            fileShareRead | fileShareWrite | fileShareDelete,
            IntPtr.Zero,
            openExisting,
            fileFlagOpenReparsePoint | fileFlagBackupSemantics,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            const int errorSharingViolation = 32;
            var error = Marshal.GetLastPInvokeError();
            return error == errorSharingViolation
                ? false
                : throw new Win32Exception(error);
        }

        var printName = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetPath));
        var substituteName = @"\??\" + printName;
        var substituteBytes = Encoding.Unicode.GetBytes(substituteName);
        var printBytes = Encoding.Unicode.GetBytes(printName);
        var pathBytes = Encoding.Unicode.GetBytes(substituteName + '\0' + printName + '\0');
        var buffer = new byte[16 + pathBytes.Length];

        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0, 4), ioReparseTagMountPoint);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4, 2), checked((ushort)(8 + pathBytes.Length)));
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(8, 2), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(10, 2), checked((ushort)substituteBytes.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(
            buffer.AsSpan(12, 2),
            checked((ushort)(substituteBytes.Length + sizeof(char))));
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(14, 2), checked((ushort)printBytes.Length));
        pathBytes.CopyTo(buffer, 16);

        if (!DeviceIoControl(
                handle,
                fsctlSetReparsePoint,
                buffer,
                checked((uint)buffer.Length),
                IntPtr.Zero,
                0,
                out _,
                IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        return true;
    }

    [LibraryImport(
        "kernel32.dll",
        EntryPoint = "CreateFileW",
        SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(
        SafeFileHandle device,
        uint controlCode,
        byte[] inputBuffer,
        uint inputBufferSize,
        IntPtr outputBuffer,
        uint outputBufferSize,
        out uint bytesReturned,
        IntPtr overlapped);
}
