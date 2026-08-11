using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DevForge.Application.Contracts;
using DevForge.Infrastructure.Blueprints;
using DevForge.Infrastructure.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace DevForge.IntegrationTests.Infrastructure.Blueprints;

public sealed partial class BlueprintChecksumVerifierTests
{
    [Fact]
    public async Task VerifyAcceptsCompleteChecksumsAndUsesStableOrdinalAggregateInput()
    {
        await using var fixture = await ChecksumFixture.CreateAsync();
        var first = await fixture.CreatePackageAsync("first", reverseChecksumOrder: false);
        var second = await fixture.CreatePackageAsync("second", reverseChecksumOrder: true);

        var firstResult = await BlueprintChecksumVerifier.VerifyAsync(
            fixture.Workspace,
            first,
            CancellationToken.None);
        var secondResult = await BlueprintChecksumVerifier.VerifyAsync(
            fixture.Workspace,
            second,
            CancellationToken.None);

        Assert.True(firstResult.IsValid);
        Assert.True(secondResult.IsValid);
        Assert.Equal(firstResult.AggregateChecksum, secondResult.AggregateChecksum);
        Assert.StartsWith("sha256:", firstResult.AggregateChecksum, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ChecksumFailure.MissingEntry)]
    [InlineData(ChecksumFailure.ExtraEntry)]
    [InlineData(ChecksumFailure.DuplicateEntry)]
    [InlineData(ChecksumFailure.SelfDeclared)]
    [InlineData(ChecksumFailure.HashMismatch)]
    public async Task VerifyRejectsIncompleteAmbiguousOrMismatchedDeclarations(ChecksumFailure failure)
    {
        await using var fixture = await ChecksumFixture.CreateAsync();
        var package = await fixture.CreatePackageAsync("package", failure: failure);

        var result = await BlueprintChecksumVerifier.VerifyAsync(
            fixture.Workspace,
            package,
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal("DF-BP-002", Assert.Single(result.Issues).Code);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("/rooted")]
    [InlineData("C:/device")]
    [InlineData("\\\\server/share")]
    [InlineData("templates\\backslash.txt")]
    [InlineData("checksums.json")]
    public async Task VerifyRejectsUnsafeOrSelfReferentialDeclaredPaths(string path)
    {
        await using var fixture = await ChecksumFixture.CreateAsync();
        var package = await fixture.CreatePackageAsync("package", declaredPathOverride: path);

        var result = await BlueprintChecksumVerifier.VerifyAsync(
            fixture.Workspace,
            package,
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal("DF-BP-002", Assert.Single(result.Issues).Code);
    }

    [Fact]
    public async Task VerifyRejectsPackageFileCountBound()
    {
        await using var fixture = await ChecksumFixture.CreateAsync();
        var package = Relative("many-files");
        await fixture.Workspace.CreateDirectoryAsync(package, CancellationToken.None);
        for (var index = 0; index <= BlueprintChecksumVerifier.MaximumFiles; index++)
        {
            await fixture.WriteAsync(
                Relative($"many-files\\files\\{index:D4}.txt"),
                []);
        }

        await fixture.WriteChecksumsAsync(package, new Dictionary<string, string>());

        var result = await BlueprintChecksumVerifier.VerifyAsync(
            fixture.Workspace,
            package,
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal("DF-BP-004", Assert.Single(result.Issues).Code);
    }

    [Fact]
    public async Task VerifyRejectsTotalDeclaredContentBoundBeforeHashComparison()
    {
        await using var fixture = await ChecksumFixture.CreateAsync();
        var package = Relative("large-package");
        await fixture.Workspace.CreateDirectoryAsync(package, CancellationToken.None);
        await using (var stream = await fixture.Workspace.OpenWriteAsync(
                         Relative("large-package\\payload.bin"),
                         overwrite: false,
                         CancellationToken.None))
        {
            stream.SetLength(BlueprintChecksumVerifier.MaximumDeclaredBytes + 1L);
        }

        await fixture.WriteChecksumsAsync(
            package,
            new Dictionary<string, string>
            {
                ["payload.bin"] = new string('0', 64),
            });

        var result = await BlueprintChecksumVerifier.VerifyAsync(
            fixture.Workspace,
            package,
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal("DF-BP-004", Assert.Single(result.Issues).Code);
    }

    [Fact]
    public async Task VerifyRejectsJunctionThatEscapesTheGuardedWorkspace()
    {
        await using var fixture = await ChecksumFixture.CreateAsync();
        var package = await fixture.CreatePackageAsync("package");
        var outsideRoot = Path.Combine(
            Path.GetTempPath(),
            "DevForge-M4-Checksum-Outside-" + Guid.NewGuid().ToString("N"));
        var linkPath = Path.Combine(fixture.RootPath, "package", "templates");
        Directory.CreateDirectory(outsideRoot);
        await File.WriteAllTextAsync(Path.Combine(outsideRoot, "app.txt"), "template");
        Directory.Delete(linkPath, recursive: true);
        JunctionFixture.Create(linkPath, outsideRoot);

        try
        {
            var result = await BlueprintChecksumVerifier.VerifyAsync(
                fixture.Workspace,
                package,
                CancellationToken.None);

            Assert.False(result.IsValid);
            Assert.Equal("DF-BP-002", Assert.Single(result.Issues).Code);
        }
        finally
        {
            if (Directory.Exists(linkPath))
            {
                Directory.Delete(linkPath);
            }

            if (Directory.Exists(outsideRoot))
            {
                Directory.Delete(outsideRoot, recursive: true);
            }
        }
    }

    public enum ChecksumFailure
    {
        MissingEntry,
        ExtraEntry,
        DuplicateEntry,
        SelfDeclared,
        HashMismatch,
    }

    private static WorkspaceRelativePath Relative(string value)
    {
        return WorkspaceRelativePath.Create(value).Value;
    }

    private sealed class ChecksumFixture : IAsyncDisposable
    {
        private static readonly Dictionary<string, byte[]> _validFiles = new(StringComparer.Ordinal)
        {
            ["manifest.yaml"] = Encoding.UTF8.GetBytes("id: example"),
            ["inputs.schema.json"] = Encoding.UTF8.GetBytes("{}"),
            ["rules.yaml"] = Encoding.UTF8.GetBytes("[]"),
            ["templates/app.txt"] = Encoding.UTF8.GetBytes("template"),
        };

        private ChecksumFixture(string rootPath, IWorkspaceFileSystem workspace)
        {
            RootPath = rootPath;
            Workspace = workspace;
        }

        public string RootPath { get; }

        public IWorkspaceFileSystem Workspace { get; }

        public static async Task<ChecksumFixture> CreateAsync()
        {
            var rootPath = Path.Combine(
                Path.GetTempPath(),
                "DevForge-M4-Checksum-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootPath);
            var workspace = await new WindowsFileSystem().OpenWorkspaceAsync(
                WorkspaceRoot.Create(rootPath).Value,
                CancellationToken.None);
            return new ChecksumFixture(rootPath, workspace);
        }

        public async Task<WorkspaceRelativePath> CreatePackageAsync(
            string name,
            bool reverseChecksumOrder = false,
            ChecksumFailure? failure = null,
            string? declaredPathOverride = null)
        {
            var package = Relative(name);
            await Workspace.CreateDirectoryAsync(package, CancellationToken.None);
            foreach (var file in _validFiles)
            {
                await WriteAsync(
                    Relative($"{name}\\{file.Key.Replace('/', '\\')}"),
                    file.Value);
            }

            var checksums = _validFiles.ToDictionary(
                item => item.Key,
                item => Convert.ToHexString(SHA256.HashData(item.Value)).ToLowerInvariant(),
                StringComparer.Ordinal);
            switch (failure)
            {
                case ChecksumFailure.MissingEntry:
                    checksums.Remove("templates/app.txt");
                    break;
                case ChecksumFailure.ExtraEntry:
                    checksums["ghost.txt"] = new string('0', 64);
                    break;
                case ChecksumFailure.SelfDeclared:
                    checksums["checksums.json"] = new string('0', 64);
                    break;
                case ChecksumFailure.HashMismatch:
                    checksums["manifest.yaml"] = new string('0', 64);
                    break;
            }

            if (declaredPathOverride is not null)
            {
                checksums.Remove("templates/app.txt");
                checksums[declaredPathOverride] = new string('0', 64);
            }

            if (failure == ChecksumFailure.DuplicateEntry)
            {
                var entries = checksums.Select(item =>
                    $"{JsonSerializer.Serialize(item.Key)}:{JsonSerializer.Serialize(item.Value)}");
                var duplicate = JsonSerializer.Serialize("manifest.yaml")
                    + ":"
                    + JsonSerializer.Serialize(checksums["manifest.yaml"]);
                await WriteAsync(
                    Relative($"{name}\\checksums.json"),
                    Encoding.UTF8.GetBytes("{" + string.Join(',', entries) + "," + duplicate + "}"));
            }
            else
            {
                var ordered = reverseChecksumOrder
                    ? checksums.Reverse()
                    : checksums;
                await WriteChecksumsAsync(package, ordered.ToDictionary(
                    item => item.Key,
                    item => item.Value,
                    StringComparer.Ordinal));
            }

            return package;
        }

        public async Task WriteChecksumsAsync(
            WorkspaceRelativePath package,
            IReadOnlyDictionary<string, string> checksums)
        {
            await WriteAsync(
                Relative($"{package.Value}\\checksums.json"),
                JsonSerializer.SerializeToUtf8Bytes(checksums));
        }

        public async Task WriteAsync(WorkspaceRelativePath path, byte[] content)
        {
            var segments = path.Value.Split('\\');
            if (segments.Length > 1)
            {
                var directory = Relative(string.Join('\\', segments[..^1]));
                if (!await Workspace.DirectoryExistsAsync(directory, CancellationToken.None))
                {
                    await Workspace.CreateDirectoryAsync(directory, CancellationToken.None);
                }
            }

            await using var stream = await Workspace.OpenWriteAsync(
                path,
                overwrite: false,
                CancellationToken.None);
            await stream.WriteAsync(content);
        }

        public ValueTask DisposeAsync()
        {
            var fullPath = Path.GetFullPath(RootPath);
            if (!fullPath.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(fullPath).StartsWith("DevForge-M4-Checksum-", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Refusing to clean an unexpected checksum fixture path.");
            }

            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, recursive: true);
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

        internal static void Create(string junctionPath, string targetPath)
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
