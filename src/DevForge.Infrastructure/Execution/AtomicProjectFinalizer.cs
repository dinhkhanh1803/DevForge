using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using DevForge.Application.Contracts;
using DevForge.Domain.Diagnostics;
using DevForge.Domain.Privacy;

namespace DevForge.Infrastructure.Execution;

public sealed class AtomicProjectFinalizer : IProjectFinalizer
{
    private const int CopyBufferSize = 81920;
    public const int MaximumFileCount = 4096;
    public const int MaximumDirectoryCount = 2048;
    public const int MaximumPathDepth = 32;
    public const long MaximumFileBytes = 64L * 1024 * 1024;
    public const long MaximumAggregateBytes = 512L * 1024 * 1024;

    public async Task<ExecutionOperationResult<FinalizationReceipt>> FinalizeAsync(
        RunCheckpoint checkpoint,
        StagingWorkspace staging,
        IWorkspaceFileSystem targetParentWorkspace,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(staging);
        ArgumentNullException.ThrowIfNull(targetParentWorkspace);
        cancellationToken.ThrowIfCancellationRequested();
        if (checkpoint.FinalizationState != FinalizationState.IntentPersisted
            || !checkpoint.Staging.Equals(staging.Descriptor)
            || !checkpoint.Target.ParentRoot.Equals(targetParentWorkspace.Root))
        {
            return Failure();
        }

        try
        {
            if (await targetParentWorkspace.DirectoryExistsAsync(
                    checkpoint.Target.TargetDirectory,
                    cancellationToken).ConfigureAwait(false)
                || await targetParentWorkspace.FileExistsAsync(
                    checkpoint.Target.TargetDirectory,
                    cancellationToken).ConfigureAwait(false))
            {
                return Failure();
            }

            if (!await StagingOwnershipVerifier.VerifyForFinalizationAsync(
                    checkpoint,
                    staging,
                    targetParentWorkspace,
                    cancellationToken).ConfigureAwait(false))
            {
                return Failure();
            }

            var treeDigest = await ComputeTreeDigestAsync(
                staging.PayloadWorkspace,
                cancellationToken).ConfigureAwait(false);
            var sourceExists = await targetParentWorkspace.DirectoryExistsAsync(
                checkpoint.Staging.PayloadDirectory,
                cancellationToken).ConfigureAwait(false);
            if (sourceExists)
            {
                var expectedPayloadRoot = WorkspaceRoot.Create(Path.GetFullPath(Path.Combine(
                    targetParentWorkspace.Root.RevealForFileSystem(),
                    checkpoint.Staging.PayloadDirectory.RevealForFileSystem())));
                if (!expectedPayloadRoot.IsValid
                    || !staging.PayloadWorkspace.Root.Equals(expectedPayloadRoot.Value))
                {
                    return Failure();
                }

                await targetParentWorkspace.MoveDirectoryAsync(
                    checkpoint.Staging.PayloadDirectory,
                    checkpoint.Target.TargetDirectory,
                    WorkspaceMoveIntent.AtomicNoOverwriteFinalize,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var copied = await CopyVerifyAndPublishAsync(
                    checkpoint,
                    staging.PayloadWorkspace,
                    targetParentWorkspace,
                    treeDigest,
                    cancellationToken).ConfigureAwait(false);
                if (!copied)
                {
                    return Failure();
                }
            }
            var receipt = FinalizationReceipt.Create(checkpoint.Target, treeDigest);
            return receipt.IsValid
                ? ExecutionOperationResult.Success(receipt.Value)
                : Failure();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            return Failure();
        }
    }

    private static async Task<bool> CopyVerifyAndPublishAsync(
        RunCheckpoint checkpoint,
        IWorkspaceFileSystem source,
        IWorkspaceFileSystem targetParent,
        string sourceDigest,
        CancellationToken cancellationToken)
    {
        if (targetParent is not IAtomicWorkspaceFileSystem atomicTarget)
        {
            return false;
        }

        var temporary = checkpoint.Target.CrossVolumeTemporaryDirectory
            ?? WorkspaceRelativePath.Create($".devforge-finalize-{checkpoint.Run.Id}").Value;
        if (await targetParent.DirectoryExistsAsync(temporary, cancellationToken).ConfigureAwait(false)
            || await targetParent.FileExistsAsync(temporary, cancellationToken).ConfigureAwait(false)
            || !await atomicTarget.TryCreateDirectoryAsync(
                temporary,
                cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var temporaryCreated = true;
        try
        {
            var sourceDirectories = await EnumerateDirectoriesRecursivelyAsync(
                source,
                root: null,
                cancellationToken).ConfigureAwait(false);
            EnsureDirectoriesBounded(sourceDirectories);
            foreach (var directory in sourceDirectories)
            {
                await targetParent.CreateDirectoryAsync(
                    Prefix(temporary, directory),
                    cancellationToken).ConfigureAwait(false);
            }

            var sourceFiles = await source.EnumerateAllFilesAsync(cancellationToken).ConfigureAwait(false);
            EnsureFilesBounded(sourceFiles);
            long copiedBytes = 0;
            foreach (var file in sourceFiles.OrderBy(path => path.Value, StringComparer.Ordinal))
            {
                var destination = Prefix(temporary, file);
                await EnsureParentAsync(targetParent, destination, cancellationToken).ConfigureAwait(false);
                await using var input = await source.OpenReadAsync(file, cancellationToken).ConfigureAwait(false);
                copiedBytes = AddBoundedLength(copiedBytes, input.Length);
                await using var output = await targetParent.OpenWriteAsync(
                    destination,
                    overwrite: false,
                    cancellationToken).ConfigureAwait(false);
                await input.CopyToAsync(output, CopyBufferSize, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            var targetDirectories = await EnumerateDirectoriesRecursivelyAsync(
                targetParent,
                temporary,
                cancellationToken).ConfigureAwait(false);
            var targetFiles = await targetParent.EnumerateFilesAsync(
                temporary,
                recursive: true,
                cancellationToken).ConfigureAwait(false);
            EnsureDirectoriesBounded(targetDirectories);
            EnsureFilesBounded(targetFiles);
            var prefixLength = temporary.Value.Length + 1;
            var normalizedTargetDirectories = targetDirectories
                .Select(path => WorkspaceRelativePath.Create(path.Value[prefixLength..]).Value)
                .ToArray();
            var normalizedTargetFiles = targetFiles
                .Select(path => WorkspaceRelativePath.Create(path.Value[prefixLength..]).Value)
                .ToArray();
            if (!sourceDirectories.SequenceEqual(
                    normalizedTargetDirectories,
                    WorkspacePathComparer.Instance)
                || !sourceFiles.OrderBy(path => path.Value, StringComparer.Ordinal).SequenceEqual(
                    normalizedTargetFiles.OrderBy(path => path.Value, StringComparer.Ordinal),
                    WorkspacePathComparer.Instance))
            {
                return false;
            }

            var copiedDigest = await ComputeTreeDigestAsync(
                targetParent,
                temporary,
                cancellationToken).ConfigureAwait(false);
            if (!StringComparer.Ordinal.Equals(sourceDigest, copiedDigest))
            {
                return false;
            }

            await targetParent.MoveDirectoryAsync(
                temporary,
                checkpoint.Target.TargetDirectory,
                WorkspaceMoveIntent.AtomicNoOverwriteFinalize,
                cancellationToken).ConfigureAwait(false);
            temporaryCreated = false;
            return true;
        }
        finally
        {
            if (temporaryCreated)
            {
                await TryDeleteTemporaryAsync(targetParent, temporary).ConfigureAwait(false);
            }
        }
    }

    private static async Task<string> ComputeTreeDigestAsync(
        IWorkspaceFileSystem workspace,
        CancellationToken cancellationToken) =>
        await ComputeTreeDigestAsync(workspace, prefix: null, cancellationToken).ConfigureAwait(false);

    private static async Task<string> ComputeTreeDigestAsync(
        IWorkspaceFileSystem workspace,
        WorkspaceRelativePath? prefix,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var files = prefix is null
            ? await workspace.EnumerateAllFilesAsync(cancellationToken).ConfigureAwait(false)
            : await workspace.EnumerateFilesAsync(
                prefix,
                recursive: true,
                cancellationToken).ConfigureAwait(false);
        var prefixLength = prefix is null ? 0 : prefix.Value.Length + 1;
        EnsureFilesBounded(files);
        long aggregateBytes = 0;
        foreach (var file in files.OrderBy(path => path.Value, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var logicalPath = prefix is null ? file.Value : file.Value[prefixLength..];
            var pathBytes = Encoding.UTF8.GetBytes(logicalPath.Replace('\\', '/'));
            hash.AppendData(pathBytes);
            hash.AppendData([0]);
            await using var input = await workspace.OpenReadAsync(
                file,
                cancellationToken).ConfigureAwait(false);
            aggregateBytes = AddBoundedLength(aggregateBytes, input.Length);
            var lengthBytes = new byte[sizeof(long)];
            BinaryPrimitives.WriteInt64BigEndian(lengthBytes, input.Length);
            hash.AppendData(lengthBytes);
            var buffer = new byte[CopyBufferSize];
            while (true)
            {
                var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                hash.AppendData(buffer, 0, read);
            }
        }

        return $"sha256:{Convert.ToHexStringLower(hash.GetHashAndReset())}";
    }

    private static async Task<WorkspaceRelativePath[]> EnumerateDirectoriesRecursivelyAsync(
        IWorkspaceFileSystem workspace,
        WorkspaceRelativePath? root,
        CancellationToken cancellationToken)
    {
        var queue = new Queue<WorkspaceRelativePath>(root is null
            ? await workspace.EnumerateRootDirectoriesAsync(cancellationToken).ConfigureAwait(false)
            : await workspace.EnumerateDirectoriesAsync(root, cancellationToken).ConfigureAwait(false));
        var directories = new List<WorkspaceRelativePath>();
        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = queue.Dequeue();
            if (directories.Count >= MaximumDirectoryCount
                || PathDepth(directory) > MaximumPathDepth)
            {
                throw new FinalizationPayloadBoundsException();
            }

            directories.Add(directory);
            var children = await workspace.EnumerateDirectoriesAsync(
                directory,
                cancellationToken).ConfigureAwait(false);
            if (directories.Count + queue.Count
                > MaximumDirectoryCount - children.Length)
            {
                throw new FinalizationPayloadBoundsException();
            }

            foreach (var child in children)
            {
                queue.Enqueue(child);
            }
        }

        return [.. directories.OrderBy(path => path.Value, StringComparer.Ordinal)];
    }

    private static void EnsureFilesBounded(IReadOnlyCollection<WorkspaceRelativePath> files)
    {
        if (files.Count > MaximumFileCount
            || files.Any(path => PathDepth(path) > MaximumPathDepth))
        {
            throw new FinalizationPayloadBoundsException();
        }
    }

    private static void EnsureDirectoriesBounded(WorkspaceRelativePath[] directories)
    {
        if (directories.Length > MaximumDirectoryCount
            || directories.Any(path => PathDepth(path) > MaximumPathDepth))
        {
            throw new FinalizationPayloadBoundsException();
        }
    }

    private static int PathDepth(WorkspaceRelativePath path) =>
        path.Value.Count(character => character == '\\') + 1;

    private static long AddBoundedLength(long aggregateBytes, long fileBytes)
    {
        if (fileBytes < 0
            || fileBytes > MaximumFileBytes
            || aggregateBytes > MaximumAggregateBytes - fileBytes)
        {
            throw new FinalizationPayloadBoundsException();
        }

        return aggregateBytes + fileBytes;
    }

    private static WorkspaceRelativePath Prefix(
        WorkspaceRelativePath prefix,
        WorkspaceRelativePath path) =>
        WorkspaceRelativePath.Create($"{prefix.Value}\\{path.Value}").Value;

    private static async Task EnsureParentAsync(
        IWorkspaceFileSystem workspace,
        WorkspaceRelativePath path,
        CancellationToken cancellationToken)
    {
        var separator = path.Value.LastIndexOf('\\');
        if (separator > 0)
        {
            await workspace.CreateDirectoryAsync(
                WorkspaceRelativePath.Create(path.Value[..separator]).Value,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task TryDeleteTemporaryAsync(
        IWorkspaceFileSystem workspace,
        WorkspaceRelativePath temporary)
    {
        try
        {
            if (await workspace.DirectoryExistsAsync(
                    temporary,
                    CancellationToken.None).ConfigureAwait(false))
            {
                await workspace.DeleteDirectoryAsync(
                    temporary,
                    DirectoryCleanupIntent.RecursiveRunOwned,
                    CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
        }
    }

    private sealed class WorkspacePathComparer : IEqualityComparer<WorkspaceRelativePath>
    {
        public static WorkspacePathComparer Instance { get; } = new();

        public bool Equals(WorkspaceRelativePath? x, WorkspaceRelativePath? y) =>
            x is not null
            && y is not null
            && StringComparer.Ordinal.Equals(x.Value, y.Value);

        public int GetHashCode(WorkspaceRelativePath obj) =>
            StringComparer.Ordinal.GetHashCode(obj.Value);
    }

    private static bool IsExpectedFailure(Exception exception) =>
        exception is IOException
            or InfrastructureOperationException
            or FinalizationPayloadBoundsException
            or UnauthorizedAccessException
            or System.Security.SecurityException
            or NotSupportedException
            or ArgumentException;

    private static ExecutionOperationResult<FinalizationReceipt> Failure()
    {
        var detail = RedactedText.FromTrustedRedaction(
            "The guarded finalization boundary could not verify and publish the staged project without overwrite.").Value;
        var error = DevForgeError.Create(
            "DF-FINAL-001",
            "The generated project could not be finalized safely.",
            detail,
            "finalization",
            null,
            false,
            ["Verify that the target is absent and retry from the retained run checkpoint."],
            []).Value;
        return ExecutionOperationResult.Failure<FinalizationReceipt>(error);
    }
}

internal sealed class FinalizationPayloadBoundsException : Exception;
