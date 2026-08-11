using DevForge.Application.Contracts;
using DevForge.Domain.Diagnostics;
using DevForge.Domain.Privacy;
using DevForge.Domain.Runs;
using DevForge.Domain.Validation;

namespace DevForge.Infrastructure.Execution;

public sealed class OwnedStagingWorkspaceManager : IStagingWorkspaceManager
{
    private const string StagingRootName = ".devforge-staging";
    private const string PayloadName = "payload";
    private const string MarkerName = "ownership.json";
    private const string GlobalLeaseName = "execution.lock";
    private readonly IFileSystem _fileSystem;

    public OwnedStagingWorkspaceManager(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public async Task<ExecutionOperationResult<IStagingWorkspaceLease>> CreateAsync(
        ExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (await ExistsAsync(
                    request.TargetParentWorkspace,
                    request.TargetDirectory,
                    cancellationToken)
                .ConfigureAwait(false))
            {
                return Failure<IStagingWorkspaceLease>(
                    "DF-FINAL-001",
                    "The project target already exists.",
                    "No target content was changed.",
                    retryable: false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            return Failure<IStagingWorkspaceLease>(
                "DF-FINAL-001",
                "The project target could not be verified as absent.",
                "No target or staging content was changed.",
                retryable: false);
        }

        var descriptor = CreateDescriptor(request.Run.Id);
        if (!descriptor.IsValid)
        {
            return InvalidOwnership<IStagingWorkspaceLease>();
        }

        if (request.TargetParentWorkspace is not IAtomicWorkspaceFileSystem atomicWorkspace)
        {
            return InvalidOwnership<IStagingWorkspaceLease>();
        }

        Stream? leaseStream = null;
        var containerCreated = false;
        try
        {
            await request.TargetParentWorkspace.CreateDirectoryAsync(
                Relative(StagingRootName),
                cancellationToken).ConfigureAwait(false);
            leaseStream = await AcquireGlobalLeaseAsync(
                request.TargetParentWorkspace,
                cancellationToken).ConfigureAwait(false);
            containerCreated = await atomicWorkspace.TryCreateDirectoryAsync(
                descriptor.Value.ContainerDirectory,
                cancellationToken).ConfigureAwait(false);
            if (!containerCreated)
            {
                await DisposeStreamAsync(leaseStream).ConfigureAwait(false);
                leaseStream = null;
                return InvalidOwnership<IStagingWorkspaceLease>();
            }

            await request.TargetParentWorkspace.CreateDirectoryAsync(
                descriptor.Value.PayloadDirectory,
                cancellationToken).ConfigureAwait(false);

            var marker = new StagingOwnershipMarker(
                descriptor.Value.MarkerId,
                request.Run.Id,
                request.PlannedProject.Plan.Id,
                request.PlannedProject.Preview.Blueprint,
                request.PlannedProject.BlueprintFingerprint.AggregateChecksum);
            await WriteMarkerAsync(
                request.TargetParentWorkspace,
                descriptor.Value.MarkerFile,
                marker,
                cancellationToken).ConfigureAwait(false);
            var payloadWorkspace = await OpenChildWorkspaceAsync(
                request.TargetParentWorkspace,
                descriptor.Value.PayloadDirectory,
                cancellationToken).ConfigureAwait(false);
            var workspace = StagingWorkspace.Create(descriptor.Value, payloadWorkspace);
            if (!workspace.IsValid)
            {
                throw new InvalidStagingMarkerException();
            }

            var lease = new OwnedStagingWorkspaceLease(workspace.Value, leaseStream);
            leaseStream = null;
            return ExecutionOperationResult.Success<IStagingWorkspaceLease>(lease);
        }
        catch (OperationCanceledException)
        {
            if (containerCreated)
            {
                await TryRemovePartialContainerAsync(
                    request.TargetParentWorkspace,
                    descriptor.Value).ConfigureAwait(false);
            }

            await DisposeStreamAsync(leaseStream).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            if (containerCreated)
            {
                await TryRemovePartialContainerAsync(
                    request.TargetParentWorkspace,
                    descriptor.Value).ConfigureAwait(false);
            }

            await DisposeStreamAsync(leaseStream).ConfigureAwait(false);
            return InvalidOwnership<IStagingWorkspaceLease>();
        }
    }

    public async Task<ExecutionOperationResult<IStagingWorkspaceLease>> ValidateOwnershipAsync(
        RunCheckpoint checkpoint,
        IWorkspaceFileSystem targetParentWorkspace,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(targetParentWorkspace);
        cancellationToken.ThrowIfCancellationRequested();
        if (!targetParentWorkspace.Root.Equals(checkpoint.Target.ParentRoot)
            || !IsExactDescriptorForRun(checkpoint.Staging, checkpoint.Run.Id))
        {
            return InvalidOwnership<IStagingWorkspaceLease>();
        }

        try
        {
            if (await ExistsAsync(
                    targetParentWorkspace,
                    checkpoint.Target.TargetDirectory,
                    cancellationToken)
                .ConfigureAwait(false))
            {
                return TargetNotAbsent<IStagingWorkspaceLease>();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            return TargetNotAbsent<IStagingWorkspaceLease>();
        }

        Stream? leaseStream = null;
        try
        {
            leaseStream = await AcquireGlobalLeaseAsync(
                targetParentWorkspace,
                cancellationToken).ConfigureAwait(false);
            var previousDescriptor = CreateSiblingDescriptor(checkpoint, ".previous");
            if (!await targetParentWorkspace.DirectoryExistsAsync(
                    checkpoint.Staging.ContainerDirectory,
                    cancellationToken).ConfigureAwait(false)
                && await targetParentWorkspace.DirectoryExistsAsync(
                    previousDescriptor.ContainerDirectory,
                    cancellationToken).ConfigureAwait(false))
            {
                if (!await IsValidOwnedDescriptorAsync(
                        targetParentWorkspace,
                        previousDescriptor,
                        checkpoint,
                        cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidStagingMarkerException();
                }

                await targetParentWorkspace.MoveDirectoryAsync(
                    previousDescriptor.ContainerDirectory,
                    checkpoint.Staging.ContainerDirectory,
                    WorkspaceMoveIntent.AtomicNoOverwriteFinalize,
                    CancellationToken.None).ConfigureAwait(false);
            }

            var markerBytes = await ReadMarkerAsync(
                targetParentWorkspace,
                checkpoint.Staging.MarkerFile,
                cancellationToken).ConfigureAwait(false);
            var marker = StagingOwnershipMarkerCodec.Decode(markerBytes);
            if (!Matches(marker, checkpoint))
            {
                throw new InvalidStagingMarkerException();
            }

            var payloadWorkspace = await OpenChildWorkspaceAsync(
                targetParentWorkspace,
                checkpoint.Staging.PayloadDirectory,
                cancellationToken).ConfigureAwait(false);
            _ = await payloadWorkspace.EnumerateAllFilesAsync(cancellationToken).ConfigureAwait(false);
            var workspace = StagingWorkspace.Create(checkpoint.Staging, payloadWorkspace);
            if (!workspace.IsValid)
            {
                throw new InvalidStagingMarkerException();
            }

            var lease = new OwnedStagingWorkspaceLease(workspace.Value, leaseStream);
            leaseStream = null;
            return ExecutionOperationResult.Success<IStagingWorkspaceLease>(lease);
        }
        catch (OperationCanceledException)
        {
            await DisposeStreamAsync(leaseStream).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            await DisposeStreamAsync(leaseStream).ConfigureAwait(false);
            return InvalidOwnership<IStagingWorkspaceLease>();
        }
    }

    public async Task<ExecutionOperationResult<StagingCleanupReceipt>> CleanupAsync(
        RunCheckpoint checkpoint,
        IWorkspaceFileSystem targetParentWorkspace,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(targetParentWorkspace);
        cancellationToken.ThrowIfCancellationRequested();
        if (!checkpoint.Run.AllowsStagingCleanup
            || checkpoint.FinalizationState == FinalizationState.Succeeded)
        {
            return Failure<StagingCleanupReceipt>(
                "DF-FINAL-001",
                "The staging workspace is not eligible for cleanup.",
                "Finalized or active workspaces are never cleanup candidates.",
                retryable: false);
        }

        var validated = await ValidateOwnershipAsync(
            checkpoint,
            targetParentWorkspace,
            cancellationToken).ConfigureAwait(false);
        if (!validated.IsSuccessful)
        {
            return ExecutionOperationResult.Failure<StagingCleanupReceipt>(validated.Error!);
        }

        await using var lease = validated.Value;
        try
        {
            foreach (var sibling in new[]
                     {
                         CreateSiblingDescriptor(checkpoint, ".replay"),
                         CreateSiblingDescriptor(checkpoint, ".previous"),
                     })
            {
                if (!await targetParentWorkspace.DirectoryExistsAsync(
                        sibling.ContainerDirectory,
                        cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                if (!await IsValidOwnedDescriptorAsync(
                        targetParentWorkspace,
                        sibling,
                        checkpoint,
                        cancellationToken).ConfigureAwait(false))
                {
                    return InvalidOwnership<StagingCleanupReceipt>();
                }

                await targetParentWorkspace.DeleteDirectoryAsync(
                    sibling.ContainerDirectory,
                    DirectoryCleanupIntent.RecursiveRunOwned,
                    cancellationToken).ConfigureAwait(false);
            }

            await targetParentWorkspace.DeleteDirectoryAsync(
                checkpoint.Staging.ContainerDirectory,
                DirectoryCleanupIntent.RecursiveRunOwned,
                cancellationToken).ConfigureAwait(false);
            var receipt = StagingCleanupReceipt.Create(
                checkpoint.Run.Id,
                checkpoint.Staging.MarkerId);
            return receipt.IsValid
                ? ExecutionOperationResult.Success(receipt.Value)
                : InvalidOwnership<StagingCleanupReceipt>();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            return Failure<StagingCleanupReceipt>(
                "DF-FINAL-001",
                "The staging workspace could not be cleaned.",
                "Ownership was verified but guarded cleanup did not complete.",
                retryable: true);
        }
    }

    public async Task<ExecutionOperationResult<StagingCleanupReceipt>> CleanupFinalizedAsync(
        RunCheckpoint checkpoint,
        IWorkspaceFileSystem targetParentWorkspace,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(targetParentWorkspace);
        cancellationToken.ThrowIfCancellationRequested();
        if (checkpoint.Run.Status != RunStatus.LocalReady
            || checkpoint.FinalizationState != FinalizationState.Succeeded
            || checkpoint.ReportState != ReportPersistenceState.Succeeded
            || !targetParentWorkspace.Root.Equals(checkpoint.Target.ParentRoot)
            || !IsExactDescriptorForRun(checkpoint.Staging, checkpoint.Run.Id))
        {
            return InvalidOwnership<StagingCleanupReceipt>();
        }

        Stream? leaseStream = null;
        try
        {
            leaseStream = await AcquireGlobalLeaseAsync(
                targetParentWorkspace,
                cancellationToken).ConfigureAwait(false);
            if (!await ExistsAsync(
                    targetParentWorkspace,
                    checkpoint.Target.TargetDirectory,
                    cancellationToken).ConfigureAwait(false)
                || await targetParentWorkspace.DirectoryExistsAsync(
                    checkpoint.Staging.PayloadDirectory,
                    cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidStagingMarkerException();
            }

            var marker = StagingOwnershipMarkerCodec.Decode(await ReadMarkerAsync(
                targetParentWorkspace,
                checkpoint.Staging.MarkerFile,
                cancellationToken).ConfigureAwait(false));
            if (!Matches(marker, checkpoint))
            {
                throw new InvalidStagingMarkerException();
            }

            var container = await OpenChildWorkspaceAsync(
                targetParentWorkspace,
                checkpoint.Staging.ContainerDirectory,
                cancellationToken).ConfigureAwait(false);
            var files = await container.EnumerateAllFilesAsync(cancellationToken).ConfigureAwait(false);
            var directories = await container.EnumerateRootDirectoriesAsync(
                cancellationToken).ConfigureAwait(false);
            if (files.Length != 1
                || !StringComparer.Ordinal.Equals(files[0].Value, MarkerName)
                || !directories.IsEmpty)
            {
                throw new InvalidStagingMarkerException();
            }

            var ownedSiblings = new List<StagingDescriptor>();
            foreach (var sibling in new[]
                     {
                         CreateSiblingDescriptor(checkpoint, ".replay"),
                         CreateSiblingDescriptor(checkpoint, ".previous"),
                     })
            {
                if (!await targetParentWorkspace.DirectoryExistsAsync(
                        sibling.ContainerDirectory,
                        cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                if (!await IsValidOwnedDescriptorAsync(
                        targetParentWorkspace,
                        sibling,
                        checkpoint,
                        cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidStagingMarkerException();
                }

                ownedSiblings.Add(sibling);
            }

            foreach (var sibling in ownedSiblings)
            {
                await targetParentWorkspace.DeleteDirectoryAsync(
                    sibling.ContainerDirectory,
                    DirectoryCleanupIntent.RecursiveRunOwned,
                    cancellationToken).ConfigureAwait(false);
            }

            await targetParentWorkspace.DeleteDirectoryAsync(
                checkpoint.Staging.ContainerDirectory,
                DirectoryCleanupIntent.RecursiveRunOwned,
                cancellationToken).ConfigureAwait(false);
            return ExecutionOperationResult.Success(
                StagingCleanupReceipt.Create(
                    checkpoint.Run.Id,
                    checkpoint.Staging.MarkerId).Value);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            return InvalidOwnership<StagingCleanupReceipt>();
        }
        finally
        {
            await DisposeStreamAsync(leaseStream).ConfigureAwait(false);
        }
    }

    public async Task<ExecutionOperationResult<IStagingWorkspaceLease>> RecreateForReplayAsync(
        RunCheckpoint checkpoint,
        ExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!RequestMatchesCheckpoint(request, checkpoint)
            || request.TargetParentWorkspace is not IAtomicWorkspaceFileSystem atomicWorkspace)
        {
            return InvalidOwnership<IStagingWorkspaceLease>();
        }

        var validated = await ValidateOwnershipAsync(
            checkpoint,
            request.TargetParentWorkspace,
            cancellationToken).ConfigureAwait(false);
        if (!validated.IsSuccessful)
        {
            return ExecutionOperationResult.Failure<IStagingWorkspaceLease>(validated.Error!);
        }

        await using var validatedLease = validated.Value;
        if (validatedLease is not OwnedStagingWorkspaceLease ownedLease)
        {
            return InvalidOwnership<IStagingWorkspaceLease>();
        }

        Stream? leaseStream = ownedLease.TakeLeaseStream();
        var replacementContainer = WorkspaceRelativePath.Create(
            checkpoint.Staging.ContainerDirectory.Value + ".replay").Value;
        var replacementPayload = WorkspaceRelativePath.Create(
            replacementContainer.Value + "\\" + PayloadName).Value;
        var replacementMarker = WorkspaceRelativePath.Create(
            replacementContainer.Value + "\\" + MarkerName).Value;
        var previousContainer = WorkspaceRelativePath.Create(
            checkpoint.Staging.ContainerDirectory.Value + ".previous").Value;
        var replacementCreated = false;
        var previousMoved = false;
        try
        {
            if (await ExistsAsync(
                    request.TargetParentWorkspace,
                    request.TargetDirectory,
                    cancellationToken).ConfigureAwait(false))
            {
                await DisposeStreamAsync(leaseStream).ConfigureAwait(false);
                leaseStream = null;
                return TargetNotAbsent<IStagingWorkspaceLease>();
            }

            foreach (var sibling in new[]
                     {
                         CreateSiblingDescriptor(checkpoint, ".replay"),
                         CreateSiblingDescriptor(checkpoint, ".previous"),
                     })
            {
                if (!await request.TargetParentWorkspace.DirectoryExistsAsync(
                        sibling.ContainerDirectory,
                        cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                if (!await IsValidOwnedDescriptorAsync(
                        request.TargetParentWorkspace,
                        sibling,
                        checkpoint,
                        cancellationToken).ConfigureAwait(false))
                {
                    await DisposeStreamAsync(leaseStream).ConfigureAwait(false);
                    leaseStream = null;
                    return InvalidOwnership<IStagingWorkspaceLease>();
                }

                await request.TargetParentWorkspace.DeleteDirectoryAsync(
                    sibling.ContainerDirectory,
                    DirectoryCleanupIntent.RecursiveRunOwned,
                    cancellationToken).ConfigureAwait(false);
            }

            if (await ExistsAsync(
                    request.TargetParentWorkspace,
                    replacementContainer,
                    cancellationToken).ConfigureAwait(false)
                || await ExistsAsync(
                    request.TargetParentWorkspace,
                    previousContainer,
                    cancellationToken).ConfigureAwait(false))
            {
                await DisposeStreamAsync(leaseStream).ConfigureAwait(false);
                leaseStream = null;
                return InvalidOwnership<IStagingWorkspaceLease>();
            }

            replacementCreated = await atomicWorkspace.TryCreateDirectoryAsync(
                replacementContainer,
                cancellationToken).ConfigureAwait(false);
            if (!replacementCreated)
            {
                await DisposeStreamAsync(leaseStream).ConfigureAwait(false);
                leaseStream = null;
                return InvalidOwnership<IStagingWorkspaceLease>();
            }

            await request.TargetParentWorkspace.CreateDirectoryAsync(
                replacementPayload,
                cancellationToken).ConfigureAwait(false);
            var marker = new StagingOwnershipMarker(
                checkpoint.Staging.MarkerId,
                checkpoint.Run.Id,
                checkpoint.PlanHash,
                checkpoint.Blueprint,
                checkpoint.BlueprintFingerprint.AggregateChecksum);
            await WriteMarkerAsync(
                request.TargetParentWorkspace,
                replacementMarker,
                marker,
                cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            await request.TargetParentWorkspace.MoveDirectoryAsync(
                checkpoint.Staging.ContainerDirectory,
                previousContainer,
                WorkspaceMoveIntent.AtomicNoOverwriteFinalize,
                CancellationToken.None).ConfigureAwait(false);
            previousMoved = true;
            await request.TargetParentWorkspace.MoveDirectoryAsync(
                replacementContainer,
                checkpoint.Staging.ContainerDirectory,
                WorkspaceMoveIntent.AtomicNoOverwriteFinalize,
                CancellationToken.None).ConfigureAwait(false);
            replacementCreated = false;

            var payloadWorkspace = await OpenChildWorkspaceAsync(
                request.TargetParentWorkspace,
                checkpoint.Staging.PayloadDirectory,
                CancellationToken.None).ConfigureAwait(false);
            var workspace = StagingWorkspace.Create(checkpoint.Staging, payloadWorkspace);
            if (!workspace.IsValid)
            {
                throw new InvalidStagingMarkerException();
            }

            await TryRemoveDirectoryAsync(
                request.TargetParentWorkspace,
                previousContainer).ConfigureAwait(false);
            previousMoved = false;

            var replayLease = new OwnedStagingWorkspaceLease(workspace.Value, leaseStream);
            leaseStream = null;
            return ExecutionOperationResult.Success<IStagingWorkspaceLease>(replayLease);
        }
        catch (OperationCanceledException)
        {
            if (replacementCreated)
            {
                await TryRemoveDirectoryAsync(
                    request.TargetParentWorkspace,
                    replacementContainer).ConfigureAwait(false);
            }

            await DisposeStreamAsync(leaseStream).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            if (previousMoved)
            {
                await TryRollbackReplayAsync(
                    request.TargetParentWorkspace,
                    checkpoint.Staging.ContainerDirectory,
                    replacementContainer,
                    previousContainer,
                    CancellationToken.None).ConfigureAwait(false);
            }

            if (replacementCreated)
            {
                await TryRemoveDirectoryAsync(
                    request.TargetParentWorkspace,
                    replacementContainer).ConfigureAwait(false);
            }

            await DisposeStreamAsync(leaseStream).ConfigureAwait(false);
            return InvalidOwnership<IStagingWorkspaceLease>();
        }
    }

    private static ValidationResult<StagingDescriptor> CreateDescriptor(string runId)
    {
        var container = WorkspaceRelativePath.Create($"{StagingRootName}\\{runId}");
        if (!container.IsValid)
        {
            return StagingDescriptor.Create(null, null, null, runId);
        }

        var payload = WorkspaceRelativePath.Create($"{container.Value.Value}\\{PayloadName}");
        var marker = WorkspaceRelativePath.Create($"{container.Value.Value}\\{MarkerName}");
        return StagingDescriptor.Create(
            container.Value,
            payload.IsValid ? payload.Value : null,
            marker.IsValid ? marker.Value : null,
            runId);
    }

    private static StagingDescriptor CreateSiblingDescriptor(
        RunCheckpoint checkpoint,
        string suffix)
    {
        var container = WorkspaceRelativePath.Create(
            checkpoint.Staging.ContainerDirectory.Value + suffix).Value;
        return StagingDescriptor.Create(
            container,
            WorkspaceRelativePath.Create(container.Value + "\\" + PayloadName).Value,
            WorkspaceRelativePath.Create(container.Value + "\\" + MarkerName).Value,
            checkpoint.Staging.MarkerId).Value;
    }

    private async Task<bool> IsValidOwnedDescriptorAsync(
        IWorkspaceFileSystem workspace,
        StagingDescriptor descriptor,
        RunCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        try
        {
            var markerBytes = await ReadMarkerAsync(
                workspace,
                descriptor.MarkerFile,
                cancellationToken).ConfigureAwait(false);
            if (!Matches(StagingOwnershipMarkerCodec.Decode(markerBytes), checkpoint))
            {
                return false;
            }

            var payload = await OpenChildWorkspaceAsync(
                workspace,
                descriptor.PayloadDirectory,
                cancellationToken).ConfigureAwait(false);
            _ = await payload.EnumerateAllFilesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            return false;
        }
    }

    private async Task<IWorkspaceFileSystem> OpenChildWorkspaceAsync(
        IWorkspaceFileSystem parent,
        WorkspaceRelativePath child,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(Path.Combine(
            parent.Root.RevealForFileSystem(),
            child.RevealForFileSystem()));
        var root = WorkspaceRoot.Create(fullPath);
        if (!root.IsValid)
        {
            throw new InvalidStagingMarkerException();
        }

        return await _fileSystem.OpenWorkspaceAsync(root.Value, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Stream> AcquireGlobalLeaseAsync(
        IWorkspaceFileSystem workspace,
        CancellationToken cancellationToken)
    {
        var leasePath = Relative($"{StagingRootName}\\{GlobalLeaseName}");
        return await workspace.OpenWriteAsync(
            leasePath,
            overwrite: true,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteMarkerAsync(
        IWorkspaceFileSystem workspace,
        WorkspaceRelativePath markerPath,
        StagingOwnershipMarker marker,
        CancellationToken cancellationToken)
    {
        var bytes = StagingOwnershipMarkerCodec.Encode(marker);
        await using var stream = await workspace.OpenWriteAsync(
            markerPath,
            overwrite: false,
            cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadMarkerAsync(
        IWorkspaceFileSystem workspace,
        WorkspaceRelativePath markerPath,
        CancellationToken cancellationToken)
    {
        await using var stream = await workspace.OpenReadAsync(
            markerPath,
            cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
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

    private static async Task<bool> ExistsAsync(
        IWorkspaceFileSystem workspace,
        WorkspaceRelativePath path,
        CancellationToken cancellationToken)
    {
        return await workspace.DirectoryExistsAsync(path, cancellationToken).ConfigureAwait(false)
            || await workspace.FileExistsAsync(path, cancellationToken).ConfigureAwait(false);
    }

    private static bool Matches(StagingOwnershipMarker marker, RunCheckpoint checkpoint)
    {
        return StringComparer.Ordinal.Equals(marker.MarkerId, checkpoint.Staging.MarkerId)
            && StringComparer.Ordinal.Equals(marker.RunId, checkpoint.Run.Id)
            && StringComparer.Ordinal.Equals(marker.PlanHash, checkpoint.PlanHash)
            && marker.Blueprint.Equals(checkpoint.Blueprint)
            && StringComparer.Ordinal.Equals(
                marker.BlueprintChecksum,
                checkpoint.BlueprintFingerprint.AggregateChecksum);
    }

    private static bool RequestMatchesCheckpoint(
        ExecutionRequest request,
        RunCheckpoint checkpoint)
    {
        var requestedFingerprint = request.PlannedProject.BlueprintFingerprint;
        var persistedFingerprint = checkpoint.BlueprintFingerprint;
        return StringComparer.Ordinal.Equals(request.Run.Id, checkpoint.Run.Id)
            && StringComparer.Ordinal.Equals(request.PlannedProject.Plan.Id, checkpoint.PlanHash)
            && request.PlannedProject.Preview.Blueprint.Equals(checkpoint.Blueprint)
            && request.TargetParentWorkspace.Root.Equals(checkpoint.Target.ParentRoot)
            && request.TargetDirectory.Equals(checkpoint.Target.TargetDirectory)
            && request.RunArtifactWorkspace.Root.Equals(checkpoint.RunArtifacts.Root)
            && StringComparer.Ordinal.Equals(
                requestedFingerprint.SourceId,
                persistedFingerprint.SourceId)
            && requestedFingerprint.PackageDirectory.Equals(persistedFingerprint.PackageDirectory)
            && requestedFingerprint.Trust == persistedFingerprint.Trust
            && StringComparer.Ordinal.Equals(
                requestedFingerprint.AggregateChecksum,
                persistedFingerprint.AggregateChecksum);
    }

    private static bool IsExactDescriptorForRun(StagingDescriptor descriptor, string runId)
    {
        var expected = CreateDescriptor(runId);
        return expected.IsValid
            && descriptor.ContainerDirectory.Equals(expected.Value.ContainerDirectory)
            && descriptor.PayloadDirectory.Equals(expected.Value.PayloadDirectory)
            && descriptor.MarkerFile.Equals(expected.Value.MarkerFile)
            && StringComparer.Ordinal.Equals(descriptor.MarkerId, expected.Value.MarkerId);
    }

    private static async Task TryRemovePartialContainerAsync(
        IWorkspaceFileSystem workspace,
        StagingDescriptor descriptor)
    {
        try
        {
            if (await workspace.DirectoryExistsAsync(
                    descriptor.ContainerDirectory,
                    CancellationToken.None).ConfigureAwait(false))
            {
                await workspace.DeleteDirectoryAsync(
                    descriptor.ContainerDirectory,
                    DirectoryCleanupIntent.RecursiveRunOwned,
                    CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
        }
    }

    private static async Task TryRemoveDirectoryAsync(
        IWorkspaceFileSystem workspace,
        WorkspaceRelativePath path)
    {
        try
        {
            if (await workspace.DirectoryExistsAsync(
                    path,
                    CancellationToken.None).ConfigureAwait(false))
            {
                await workspace.DeleteDirectoryAsync(
                    path,
                    DirectoryCleanupIntent.RecursiveRunOwned,
                    CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
        }
    }

    private static async Task TryRollbackReplayAsync(
        IWorkspaceFileSystem workspace,
        WorkspaceRelativePath canonical,
        WorkspaceRelativePath replacement,
        WorkspaceRelativePath previous,
        CancellationToken cancellationToken)
    {
        try
        {
            if (await workspace.DirectoryExistsAsync(
                    canonical,
                    cancellationToken).ConfigureAwait(false))
            {
                await workspace.MoveDirectoryAsync(
                    canonical,
                    replacement,
                    WorkspaceMoveIntent.AtomicNoOverwriteFinalize,
                    cancellationToken).ConfigureAwait(false);
            }

            await workspace.MoveDirectoryAsync(
                previous,
                canonical,
                WorkspaceMoveIntent.AtomicNoOverwriteFinalize,
                cancellationToken).ConfigureAwait(false);
            await TryRemoveDirectoryAsync(workspace, replacement).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
        }
    }

    private static ExecutionOperationResult<T> TargetNotAbsent<T>()
        where T : class
    {
        return Failure<T>(
            "DF-FINAL-001",
            "The project target could not be verified as absent.",
            "Ownership cannot be resumed while the target exists or cannot be contained.",
            retryable: false);
    }

    private static async ValueTask DisposeStreamAsync(Stream? stream)
    {
        if (stream is not null)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static ExecutionOperationResult<T> InvalidOwnership<T>()
        where T : class
    {
        return Failure<T>(
            "DF-EXEC-003",
            "Staging ownership could not be verified.",
            "The marker, checkpoint, workspace, or active lease did not match.",
            retryable: true);
    }

    private static ExecutionOperationResult<T> Failure<T>(
        string code,
        string summary,
        string technicalDetail,
        bool retryable)
        where T : class
    {
        var detail = RedactedText.FromTrustedRedaction(technicalDetail);
        if (!detail.IsValid)
        {
            throw new InvalidOperationException("A static staging diagnostic was not privacy safe.");
        }

        var error = DevForgeError.Create(
            code,
            summary,
            detail.Value,
            "staging",
            null,
            retryable,
            ["Verify the run checkpoint and staging ownership marker."],
            []);
        return ExecutionOperationResult.Failure<T>(error.Value);
    }

    private static bool IsExpectedFailure(Exception exception)
    {
        return exception is InfrastructureOperationException
            or InvalidStagingMarkerException
            or IOException
            or UnauthorizedAccessException;
    }

    private static WorkspaceRelativePath Relative(string value)
    {
        var result = WorkspaceRelativePath.Create(value);
        return result.IsValid ? result.Value : throw new InvalidStagingMarkerException();
    }
}

internal sealed class OwnedStagingWorkspaceLease : IStagingWorkspaceLease
{
    private Stream? _leaseStream;

    public OwnedStagingWorkspaceLease(StagingWorkspace workspace, Stream leaseStream)
    {
        Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _leaseStream = leaseStream ?? throw new ArgumentNullException(nameof(leaseStream));
    }

    public StagingWorkspace Workspace { get; }

    internal Stream TakeLeaseStream()
    {
        return Interlocked.Exchange(ref _leaseStream, null)
            ?? throw new InvalidOperationException("The staging lease has already been released.");
    }

    public async ValueTask DisposeAsync()
    {
        var stream = Interlocked.Exchange(ref _leaseStream, null);
        if (stream is not null)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
    }
}
