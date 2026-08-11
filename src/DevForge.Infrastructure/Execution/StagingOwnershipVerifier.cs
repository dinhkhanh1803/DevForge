using DevForge.Application.Contracts;

namespace DevForge.Infrastructure.Execution;

internal static class StagingOwnershipVerifier
{
    private const int BufferSize = 4096;

    public static async Task<bool> VerifyForFinalizationAsync(
        RunCheckpoint checkpoint,
        StagingWorkspace staging,
        IWorkspaceFileSystem targetParentWorkspace,
        CancellationToken cancellationToken)
    {
        if (!targetParentWorkspace.Root.Equals(checkpoint.Target.ParentRoot)
            || !checkpoint.Staging.Equals(staging.Descriptor)
            || !IsExactDescriptorForRun(checkpoint.Staging, checkpoint.Run.Id))
        {
            return false;
        }

        try
        {
            var marker = StagingOwnershipMarkerCodec.Decode(await ReadMarkerAsync(
                targetParentWorkspace,
                checkpoint.Staging.MarkerFile,
                cancellationToken).ConfigureAwait(false));
            if (!Matches(marker, checkpoint))
            {
                return false;
            }

            var files = await staging.PayloadWorkspace.EnumerateAllFilesAsync(
                cancellationToken).ConfigureAwait(false);
            if (files.Length > AtomicProjectFinalizer.MaximumFileCount
                || files.Any(path => Depth(path) > AtomicProjectFinalizer.MaximumPathDepth))
            {
                return false;
            }

            var roots = await staging.PayloadWorkspace.EnumerateRootDirectoriesAsync(
                cancellationToken).ConfigureAwait(false);
            var queue = new Queue<WorkspaceRelativePath>(roots);
            var directoryCount = roots.Length;
            while (queue.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var directory = queue.Dequeue();
                if (directoryCount > AtomicProjectFinalizer.MaximumDirectoryCount
                    || Depth(directory) > AtomicProjectFinalizer.MaximumPathDepth)
                {
                    return false;
                }

                var children = await staging.PayloadWorkspace.EnumerateDirectoriesAsync(
                    directory,
                    cancellationToken).ConfigureAwait(false);
                if (directoryCount > AtomicProjectFinalizer.MaximumDirectoryCount - children.Length)
                {
                    return false;
                }

                foreach (var child in children)
                {
                    directoryCount++;
                    queue.Enqueue(child);
                }
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException
            or InfrastructureOperationException
            or UnauthorizedAccessException
            or System.Security.SecurityException
            or NotSupportedException
            or ArgumentException
            or InvalidStagingMarkerException)
        {
            return false;
        }
    }

    private static int Depth(WorkspaceRelativePath path) =>
        path.Value.Count(character => character == '\\') + 1;

    private static async Task<byte[]> ReadMarkerAsync(
        IWorkspaceFileSystem workspace,
        WorkspaceRelativePath markerPath,
        CancellationToken cancellationToken)
    {
        await using var stream = await workspace.OpenReadAsync(
            markerPath,
            cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[BufferSize];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return buffer.ToArray();
            }

            if (buffer.Length + read > StagingOwnershipMarkerCodec.MaximumMarkerBytes)
            {
                throw new InvalidStagingMarkerException();
            }

            buffer.Write(chunk, 0, read);
        }
    }

    private static bool Matches(StagingOwnershipMarker marker, RunCheckpoint checkpoint) =>
        StringComparer.Ordinal.Equals(marker.MarkerId, checkpoint.Staging.MarkerId)
        && StringComparer.Ordinal.Equals(marker.RunId, checkpoint.Run.Id)
        && StringComparer.Ordinal.Equals(marker.PlanHash, checkpoint.PlanHash)
        && marker.Blueprint.Equals(checkpoint.Blueprint)
        && StringComparer.Ordinal.Equals(
            marker.BlueprintChecksum,
            checkpoint.BlueprintFingerprint.AggregateChecksum);

    private static bool IsExactDescriptorForRun(StagingDescriptor descriptor, string runId)
    {
        var container = WorkspaceRelativePath.Create($".devforge-staging\\{runId}");
        var payload = WorkspaceRelativePath.Create($".devforge-staging\\{runId}\\payload");
        var marker = WorkspaceRelativePath.Create($".devforge-staging\\{runId}\\ownership.json");
        return container.IsValid
            && payload.IsValid
            && marker.IsValid
            && descriptor.ContainerDirectory.Equals(container.Value)
            && descriptor.PayloadDirectory.Equals(payload.Value)
            && descriptor.MarkerFile.Equals(marker.Value)
            && StringComparer.Ordinal.Equals(descriptor.MarkerId, runId);
    }
}
