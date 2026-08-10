using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using DevForge.Application.Contracts;
using DevForge.Infrastructure;
using DevForge.Infrastructure.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace DevForge.IntegrationTests.Infrastructure.FileSystem;

public sealed partial class WindowsWorkspaceFileSystemTests
{
    [Fact]
    public async Task WorkspaceSupportsGuardedFileRoundTripAndEnumeration()
    {
        await using var fixture = await WorkspaceFixture.CreateAsync();
        var directory = Relative("nested");
        var file = Relative("nested\\project.txt");

        await fixture.Workspace.CreateDirectoryAsync(directory, CancellationToken.None);
        await using (var stream = await fixture.Workspace.OpenWriteAsync(
                         file,
                         overwrite: false,
                         CancellationToken.None))
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes("devforge"));
        }

        await using var readStream = await fixture.Workspace.OpenReadAsync(file, CancellationToken.None);
        using var reader = new StreamReader(readStream, Encoding.UTF8);
        var contents = await reader.ReadToEndAsync(CancellationToken.None);
        var files = await fixture.Workspace.EnumerateFilesAsync(
            directory,
            recursive: false,
            CancellationToken.None);

        Assert.Equal("devforge", contents);
        Assert.True(await fixture.Workspace.FileExistsAsync(file, CancellationToken.None));
        Assert.True(await fixture.Workspace.DirectoryExistsAsync(directory, CancellationToken.None));
        Assert.Equal(file.Value, Assert.Single(files).Value);
    }

    [Fact]
    public async Task OpenWriteWithoutOverwritePreservesExistingFile()
    {
        await using var fixture = await WorkspaceFixture.CreateAsync();
        var file = Relative("existing.txt");
        await fixture.WriteTextAsync(file, "original");

        var exception = await Assert.ThrowsAsync<InfrastructureOperationException>(() =>
            fixture.Workspace.OpenWriteAsync(file, overwrite: false, CancellationToken.None));

        Assert.Equal("DF-FS-002", exception.Code);
        Assert.Equal("original", await fixture.ReadTextAsync(file));
        Assert.DoesNotContain(fixture.RootPath, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MoveDirectoryDoesNotOverwriteExistingDestination()
    {
        await using var fixture = await WorkspaceFixture.CreateAsync();
        var source = Relative("staging");
        var destination = Relative("target");
        await fixture.Workspace.CreateDirectoryAsync(source, CancellationToken.None);
        await fixture.Workspace.CreateDirectoryAsync(destination, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InfrastructureOperationException>(() =>
            fixture.Workspace.MoveDirectoryAsync(
                source,
                destination,
                WorkspaceMoveIntent.AtomicNoOverwriteFinalize,
                CancellationToken.None));

        Assert.Equal("DF-FS-002", exception.Code);
        Assert.True(await fixture.Workspace.DirectoryExistsAsync(source, CancellationToken.None));
        Assert.True(await fixture.Workspace.DirectoryExistsAsync(destination, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAndMoveOperationsCompleteInsideWorkspace()
    {
        await using var fixture = await WorkspaceFixture.CreateAsync();
        var source = Relative("source");
        var destination = Relative("destination");
        var file = Relative("source\\delete.txt");
        await fixture.Workspace.CreateDirectoryAsync(source, CancellationToken.None);
        await fixture.WriteTextAsync(file, "delete me");

        await fixture.Workspace.DeleteFileAsync(file, CancellationToken.None);
        await fixture.Workspace.MoveDirectoryAsync(
            source,
            destination,
            WorkspaceMoveIntent.AtomicNoOverwriteFinalize,
            CancellationToken.None);

        Assert.False(await fixture.Workspace.FileExistsAsync(file, CancellationToken.None));
        Assert.False(await fixture.Workspace.DirectoryExistsAsync(source, CancellationToken.None));
        Assert.True(await fixture.Workspace.DirectoryExistsAsync(destination, CancellationToken.None));

        await fixture.Workspace.DeleteDirectoryAsync(
            destination,
            DirectoryCleanupIntent.RecursiveRunOwned,
            CancellationToken.None);
        Assert.False(await fixture.Workspace.DirectoryExistsAsync(destination, CancellationToken.None));
    }

    [Fact]
    public async Task OpenWorkspaceRejectsReparseRoot()
    {
        await using var fixture = await WorkspaceFixture.CreateAsync();
        var outsideRoot = fixture.CreateOutsideDirectory();
        fixture.CreateJunction("root-link", outsideRoot);
        var linkedRoot = WorkspaceRoot.Create(Path.Combine(fixture.RootPath, "root-link")).Value;

        var exception = await Assert.ThrowsAsync<InfrastructureOperationException>(() =>
            new WindowsFileSystem().OpenWorkspaceAsync(linkedRoot, CancellationToken.None));

        Assert.Equal("DF-FS-003", exception.Code);
    }

    [Fact]
    public async Task OpenWorkspaceRejectsMissingRootWithoutLeakingPath()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), "DevForge-M3-Missing-" + Guid.NewGuid().ToString("N"));
        var root = WorkspaceRoot.Create(missingPath).Value;

        var exception = await Assert.ThrowsAsync<InfrastructureOperationException>(() =>
            new WindowsFileSystem().OpenWorkspaceAsync(root, CancellationToken.None));

        Assert.Equal("DF-FS-001", exception.Code);
        Assert.DoesNotContain(missingPath, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReparseDirectoryCannotEscapeWorkspace()
    {
        await using var fixture = await WorkspaceFixture.CreateAsync();
        var outsideRoot = fixture.CreateOutsideDirectory();
        var sentinel = Path.Combine(outsideRoot, "sentinel.txt");
        await File.WriteAllTextAsync(sentinel, "outside");
        fixture.CreateJunction("link", outsideRoot);

        var exception = await Assert.ThrowsAsync<InfrastructureOperationException>(() =>
            fixture.Workspace.OpenReadAsync(Relative("link\\sentinel.txt"), CancellationToken.None));

        Assert.Equal("DF-FS-003", exception.Code);
        Assert.Equal("outside", await File.ReadAllTextAsync(sentinel));
        Assert.DoesNotContain(outsideRoot, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RecursiveEnumerationRejectsReparseDirectory()
    {
        await using var fixture = await WorkspaceFixture.CreateAsync();
        var outsideRoot = fixture.CreateOutsideDirectory();
        fixture.CreateJunction("linked", outsideRoot);

        var exception = await Assert.ThrowsAsync<InfrastructureOperationException>(() =>
            fixture.Workspace.EnumerateFilesAsync(
                Relative("linked"),
                recursive: true,
                CancellationToken.None));

        Assert.Equal("DF-FS-003", exception.Code);
    }

    [Fact]
    public async Task RecursiveRunOwnedCleanupRejectsReparseContent()
    {
        await using var fixture = await WorkspaceFixture.CreateAsync();
        var owned = Relative("owned");
        await fixture.Workspace.CreateDirectoryAsync(owned, CancellationToken.None);
        var outsideRoot = fixture.CreateOutsideDirectory();
        var sentinel = Path.Combine(outsideRoot, "keep.txt");
        await File.WriteAllTextAsync(sentinel, "keep");
        fixture.CreateJunction("owned\\linked", outsideRoot);

        var exception = await Assert.ThrowsAsync<InfrastructureOperationException>(() =>
            fixture.Workspace.DeleteDirectoryAsync(
                owned,
                DirectoryCleanupIntent.RecursiveRunOwned,
                CancellationToken.None));

        Assert.Equal("DF-FS-003", exception.Code);
        Assert.True(File.Exists(sentinel));
    }

    [Fact]
    public async Task PreCancelledOperationDoesNotMutateWorkspace()
    {
        await using var fixture = await WorkspaceFixture.CreateAsync();
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Workspace.CreateDirectoryAsync(Relative("cancelled"), source.Token));

        Assert.False(Directory.Exists(Path.Combine(fixture.RootPath, "cancelled")));
    }

    private static WorkspaceRelativePath Relative(string value)
    {
        return WorkspaceRelativePath.Create(value).Value;
    }

    private sealed class WorkspaceFixture : IAsyncDisposable
    {
        private readonly List<string> _cleanupRoots = [];
        private readonly List<string> _junctions = [];

        private WorkspaceFixture(string rootPath, IWorkspaceFileSystem workspace)
        {
            RootPath = rootPath;
            Workspace = workspace;
            _cleanupRoots.Add(rootPath);
        }

        public string RootPath { get; }

        public IWorkspaceFileSystem Workspace { get; }

        public static async Task<WorkspaceFixture> CreateAsync()
        {
            var rootPath = Path.Combine(Path.GetTempPath(), "DevForge-M3-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootPath);
            var root = WorkspaceRoot.Create(rootPath).Value;
            var workspace = await new WindowsFileSystem().OpenWorkspaceAsync(root, CancellationToken.None);
            return new WorkspaceFixture(rootPath, workspace);
        }

        public string CreateOutsideDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "DevForge-M3-Outside-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            _cleanupRoots.Add(path);
            return path;
        }

        public void CreateJunction(string relativePath, string targetPath)
        {
            var junctionPath = Path.GetFullPath(Path.Combine(RootPath, relativePath));
            var expectedPrefix = RootPath + Path.DirectorySeparatorChar;
            if (!junctionPath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Refusing to create a junction outside the test workspace.");
            }

            JunctionFixture.Create(junctionPath, targetPath);
            _junctions.Add(junctionPath);
        }

        public async Task WriteTextAsync(WorkspaceRelativePath path, string text)
        {
            await using var stream = await Workspace.OpenWriteAsync(path, overwrite: false, CancellationToken.None);
            await stream.WriteAsync(Encoding.UTF8.GetBytes(text));
        }

        public async Task<string> ReadTextAsync(WorkspaceRelativePath path)
        {
            await using var stream = await Workspace.OpenReadAsync(path, CancellationToken.None);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return await reader.ReadToEndAsync(CancellationToken.None);
        }

        public ValueTask DisposeAsync()
        {
            foreach (var junction in _junctions.OrderByDescending(path => path.Length))
            {
                if (Directory.Exists(junction))
                {
                    Directory.Delete(junction);
                }
            }

            foreach (var root in _cleanupRoots.OrderByDescending(path => path.Length))
            {
                var fullPath = Path.GetFullPath(root);
                if (!fullPath.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)
                    || !Path.GetFileName(fullPath).StartsWith("DevForge-M3-", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Refusing to clean an unexpected test directory.");
                }

                if (Directory.Exists(fullPath))
                {
                    Directory.Delete(fullPath, recursive: true);
                }
            }

            return ValueTask.CompletedTask;
        }
    }

    private static partial class JunctionFixture
    {
        private const uint GenericWrite = 0x40000000;
        private const uint FileShareRead = 0x00000001;
        private const uint FileShareWrite = 0x00000002;
        private const uint FileShareDelete = 0x00000004;
        private const uint OpenExisting = 3;
        private const uint FileFlagOpenReparsePoint = 0x00200000;
        private const uint FileFlagBackupSemantics = 0x02000000;
        private const uint FsctlSetReparsePoint = 0x000900A4;
        private const uint IoReparseTagMountPoint = 0xA0000003;

        public static void Create(string junctionPath, string targetPath)
        {
            Directory.CreateDirectory(junctionPath);
            using var handle = CreateFile(
                junctionPath,
                GenericWrite,
                FileShareRead | FileShareWrite | FileShareDelete,
                IntPtr.Zero,
                OpenExisting,
                FileFlagOpenReparsePoint | FileFlagBackupSemantics,
                IntPtr.Zero);
            if (handle.IsInvalid)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var printName = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetPath));
            var substituteName = @"\??\" + printName;
            var substituteBytes = Encoding.Unicode.GetBytes(substituteName);
            var printBytes = Encoding.Unicode.GetBytes(printName);
            var pathBytes = Encoding.Unicode.GetBytes(substituteName + '\0' + printName + '\0');
            var buffer = new byte[16 + pathBytes.Length];

            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0, 4), IoReparseTagMountPoint);
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
                    FsctlSetReparsePoint,
                    buffer,
                    checked((uint)buffer.Length),
                    IntPtr.Zero,
                    0,
                    out _,
                    IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
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
}
