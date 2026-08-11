using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using DevForge.Application.Contracts;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Execution;
using DevForge.Domain.Projects;
using DevForge.Domain.Runs;
using DevForge.Infrastructure.Execution;
using DevForge.Infrastructure.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace DevForge.IntegrationTests.Infrastructure.Execution;

public sealed partial class OwnedStagingWorkspaceManagerTests
{
    [Fact]
    public async Task CreatesOwnedPayloadAndCanonicalPrivacySafeMarker()
    {
        await using var fixture = await StagingFixture.CreateAsync();

        var result = await fixture.Manager.CreateAsync(fixture.Request, CancellationToken.None);

        Assert.True(result.IsSuccessful);
        await using var lease = result.Value;
        Assert.Equal(".devforge-staging\\run-1", lease.Workspace.Descriptor.ContainerDirectory.Value);
        Assert.Equal(
            ".devforge-staging\\run-1\\payload",
            lease.Workspace.Descriptor.PayloadDirectory.Value);
        await lease.Workspace.PayloadWorkspace.CreateDirectoryAsync(
            Relative("src"),
            CancellationToken.None);
        Assert.True(await fixture.TargetParent.DirectoryExistsAsync(
            Relative(".devforge-staging\\run-1\\payload\\src"),
            CancellationToken.None));

        var marker = await fixture.ReadMarkerAsync(lease.Workspace.Descriptor.MarkerFile);
        Assert.Contains(fixture.Request.PlannedProject.Plan.Id, marker, StringComparison.Ordinal);
        Assert.Contains("desktop.csharp-wpf-tool", marker, StringComparison.Ordinal);
        Assert.DoesNotContain("built-in", marker, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "desktop.csharp-wpf-tool\\1.0.0",
            marker,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(fixture.TargetParentPath, marker, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(fixture.RunArtifactPath, marker, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RecreateForReplayReplacesOnlyTheOwnedRunContainer()
    {
        await using var fixture = await StagingFixture.CreateAsync();
        var created = await fixture.Manager.CreateAsync(fixture.Request, CancellationToken.None);
        Assert.True(created.IsSuccessful);
        var descriptor = created.Value.Workspace.Descriptor;
        await created.Value.Workspace.PayloadWorkspace.CreateDirectoryAsync(
            Relative("stale"),
            CancellationToken.None);
        await created.Value.DisposeAsync();
        var checkpoint = fixture.CreateCheckpoint(descriptor);

        var replay = await fixture.Manager.RecreateForReplayAsync(
            checkpoint,
            fixture.Request,
            CancellationToken.None);

        Assert.True(replay.IsSuccessful);
        var replayLease = replay.Value;
        Assert.Equal(descriptor, replayLease.Workspace.Descriptor);
        Assert.False(await replayLease.Workspace.PayloadWorkspace.DirectoryExistsAsync(
            Relative("stale"),
            CancellationToken.None));
        await replayLease.DisposeAsync();
        var ownership = await fixture.Manager.ValidateOwnershipAsync(
            checkpoint,
            fixture.TargetParent,
            CancellationToken.None);
        Assert.True(ownership.IsSuccessful);
        await ownership.Value.DisposeAsync();
    }

    [Fact]
    public async Task CancellationWhilePreparingReplayPreservesTheOriginalOwnedContainer()
    {
        await using var fixture = await StagingFixture.CreateAsync();
        var created = await fixture.Manager.CreateAsync(fixture.Request, CancellationToken.None);
        Assert.True(created.IsSuccessful);
        var descriptor = created.Value.Workspace.Descriptor;
        await created.Value.Workspace.PayloadWorkspace.CreateDirectoryAsync(
            Relative("preserved"),
            CancellationToken.None);
        await created.Value.DisposeAsync();
        var checkpoint = fixture.CreateCheckpoint(descriptor);
        using var cancellation = new CancellationTokenSource();
        var intercepted = new InterceptingWorkspace(
            fixture.TargetParent,
            markerWriteCancellation: cancellation);
        var replayRequest = fixture.CreateRequestForRun("run-1", intercepted);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Manager.RecreateForReplayAsync(
                checkpoint,
                replayRequest,
                cancellation.Token));

        Assert.True(await fixture.TargetParent.DirectoryExistsAsync(
            Relative(".devforge-staging\\run-1\\payload\\preserved"),
            CancellationToken.None));
        Assert.False(await fixture.TargetParent.DirectoryExistsAsync(
            Relative(".devforge-staging\\run-1.replay"),
            CancellationToken.None));
        var ownership = await fixture.Manager.ValidateOwnershipAsync(
            checkpoint,
            fixture.TargetParent,
            CancellationToken.None);
        Assert.True(ownership.IsSuccessful);
        await ownership.Value.DisposeAsync();
    }

    [Fact]
    public async Task OwnershipValidationRecoversAnInterruptedReplayRenameWindow()
    {
        await using var fixture = await StagingFixture.CreateAsync();
        var created = await fixture.Manager.CreateAsync(fixture.Request, CancellationToken.None);
        Assert.True(created.IsSuccessful);
        var checkpoint = fixture.CreateCheckpoint(created.Value.Workspace.Descriptor);
        await created.Value.DisposeAsync();
        await fixture.TargetParent.MoveDirectoryAsync(
            Relative(".devforge-staging\\run-1"),
            Relative(".devforge-staging\\run-1.previous"),
            WorkspaceMoveIntent.AtomicNoOverwriteFinalize,
            CancellationToken.None);

        var ownership = await fixture.Manager.ValidateOwnershipAsync(
            checkpoint,
            fixture.TargetParent,
            CancellationToken.None);

        Assert.True(ownership.IsSuccessful);
        await ownership.Value.DisposeAsync();
        Assert.True(await fixture.TargetParent.DirectoryExistsAsync(
            Relative(".devforge-staging\\run-1\\payload"),
            CancellationToken.None));
        Assert.False(await fixture.TargetParent.DirectoryExistsAsync(
            Relative(".devforge-staging\\run-1.previous"),
            CancellationToken.None));
    }

    [Fact]
    public async Task RefusesPreExistingTargetBeforeCreatingStaging()
    {
        await using var fixture = await StagingFixture.CreateAsync();
        await fixture.TargetParent.CreateDirectoryAsync(Relative("project"), CancellationToken.None);

        var result = await fixture.Manager.CreateAsync(fixture.Request, CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Equal("DF-FINAL-001", result.Error?.Code);
        Assert.False(await fixture.TargetParent.DirectoryExistsAsync(
            Relative(".devforge-staging\\run-1"),
            CancellationToken.None));
    }

    [Fact]
    public async Task PreExistingTargetJunctionFailsClosedWithoutTouchingOutsideContent()
    {
        await using var fixture = await StagingFixture.CreateAsync();
        fixture.CreateTargetJunctionToOutside();

        var result = await fixture.Manager.CreateAsync(fixture.Request, CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Equal("DF-FINAL-001", result.Error?.Code);
        Assert.Equal("outside remains unchanged", await File.ReadAllTextAsync(fixture.OutsideSentinelPath));
        Assert.False(await fixture.TargetParent.DirectoryExistsAsync(
            Relative(".devforge-staging"),
            CancellationToken.None));
    }

    [Fact]
    public async Task ConcurrentCreateCannotAcquireSecondLease()
    {
        await using var fixture = await StagingFixture.CreateAsync();
        var first = await fixture.Manager.CreateAsync(fixture.Request, CancellationToken.None);
        Assert.True(first.IsSuccessful);
        await using var firstLease = first.Value;

        var second = await fixture.Manager.CreateAsync(fixture.Request, CancellationToken.None);

        Assert.False(second.IsSuccessful);
        Assert.Equal("DF-EXEC-003", second.Error?.Code);
    }

    [Fact]
    public async Task ActiveLeaseBlocksDifferentRunBeforeItsContainerIsCreated()
    {
        await using var fixture = await StagingFixture.CreateAsync();
        var first = await fixture.Manager.CreateAsync(fixture.Request, CancellationToken.None);
        Assert.True(first.IsSuccessful);
        await using var firstLease = first.Value;

        var second = await fixture.Manager.CreateAsync(
            fixture.CreateRequestForRun("run-2"),
            CancellationToken.None);
        if (second.IsSuccessful)
        {
            await second.Value.DisposeAsync();
        }

        Assert.False(second.IsSuccessful);
        Assert.Equal("DF-EXEC-003", second.Error?.Code);
        Assert.False(await fixture.TargetParent.DirectoryExistsAsync(
            Relative(".devforge-staging\\run-2"),
            CancellationToken.None));
    }

    [Fact]
    public async Task RefusesPreExistingTargetFileBeforeCreatingStaging()
    {
        await using var fixture = await StagingFixture.CreateAsync();
        await using (var stream = await fixture.TargetParent.OpenWriteAsync(
            fixture.Request.TargetDirectory,
            overwrite: false,
            CancellationToken.None))
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes("existing"), CancellationToken.None);
        }

        var result = await fixture.Manager.CreateAsync(fixture.Request, CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Equal("DF-FINAL-001", result.Error?.Code);
        Assert.False(await fixture.TargetParent.DirectoryExistsAsync(
            Relative(".devforge-staging"),
            CancellationToken.None));
    }

    [Fact]
    public async Task LosingAtomicContainerCreationNeverDeletesUnownedContent()
    {
        await using var fixture = await StagingFixture.CreateAsync();
        var sentinelPath = Relative(".devforge-staging\\run-1\\intruder.txt");
        var racingWorkspace = new InterceptingWorkspace(
            fixture.TargetParent,
            atomicCreate: async (path, cancellationToken) =>
            {
                await fixture.TargetParent.CreateDirectoryAsync(path, cancellationToken);
                await using var stream = await fixture.TargetParent.OpenWriteAsync(
                    sentinelPath,
                    overwrite: false,
                    cancellationToken);
                await stream.WriteAsync(Encoding.UTF8.GetBytes("not owned"), cancellationToken);
                return false;
            });

        var result = await fixture.Manager.CreateAsync(
            fixture.CreateRequestForRun("run-1", racingWorkspace),
            CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Equal("DF-EXEC-003", result.Error?.Code);
        Assert.Equal("not owned", await fixture.ReadTextAsync(sentinelPath));
        Assert.False(await fixture.TargetParent.FileExistsAsync(
            Relative(".devforge-staging\\run-1\\ownership.json"),
            CancellationToken.None));
    }

    [Fact]
    public async Task ExactMarkerCanBeReopenedAndSpoofedMarkerFailsClosed()
    {
        await using var fixture = await StagingFixture.CreateAsync();
        var created = await fixture.Manager.CreateAsync(fixture.Request, CancellationToken.None);
        Assert.True(created.IsSuccessful);
        var descriptor = created.Value.Workspace.Descriptor;
        await created.Value.DisposeAsync();
        var checkpoint = fixture.CreateCheckpoint(descriptor);

        var valid = await fixture.Manager.ValidateOwnershipAsync(
            checkpoint,
            fixture.TargetParent,
            CancellationToken.None);
        Assert.True(valid.IsSuccessful);
        await valid.Value.DisposeAsync();

        var marker = await fixture.ReadMarkerAsync(descriptor.MarkerFile);
        await fixture.ReplaceMarkerAsync(
            descriptor.MarkerFile,
            marker.Replace("run-1", "run-2", StringComparison.Ordinal));
        var spoofed = await fixture.Manager.ValidateOwnershipAsync(
            checkpoint,
            fixture.TargetParent,
            CancellationToken.None);

        Assert.False(spoofed.IsSuccessful);
        Assert.Equal("DF-EXEC-003", spoofed.Error?.Code);
    }

    [Fact]
    public async Task CanonicalMarkerCopiedToAnotherContainerDoesNotTransferOwnership()
    {
        await using var fixture = await StagingFixture.CreateAsync();
        var created = await fixture.Manager.CreateAsync(fixture.Request, CancellationToken.None);
        Assert.True(created.IsSuccessful);
        var marker = await fixture.ReadMarkerAsync(created.Value.Workspace.Descriptor.MarkerFile);
        await created.Value.DisposeAsync();
        var spoofedDescriptor = StagingDescriptor.Create(
            Relative(".devforge-staging\\other-run"),
            Relative(".devforge-staging\\other-run\\payload"),
            Relative(".devforge-staging\\other-run\\ownership.json"),
            "run-1").Value;
        await fixture.TargetParent.CreateDirectoryAsync(
            spoofedDescriptor.PayloadDirectory,
            CancellationToken.None);
        await fixture.WriteMarkerAsync(spoofedDescriptor.MarkerFile, marker);

        var result = await fixture.Manager.ValidateOwnershipAsync(
            fixture.CreateCheckpoint(spoofedDescriptor),
            fixture.TargetParent,
            CancellationToken.None);
        if (result.IsSuccessful)
        {
            await result.Value.DisposeAsync();
        }

        Assert.False(result.IsSuccessful);
        Assert.Equal("DF-EXEC-003", result.Error?.Code);
    }

    [Fact]
    public async Task MalformedMarkerFailsClosedWithoutLeakingContent()
    {
        const string malformed = "Bearer abcdefghijk";
        await using var fixture = await StagingFixture.CreateAsync();
        var created = await fixture.Manager.CreateAsync(fixture.Request, CancellationToken.None);
        Assert.True(created.IsSuccessful);
        var descriptor = created.Value.Workspace.Descriptor;
        await created.Value.DisposeAsync();
        await fixture.ReplaceMarkerAsync(descriptor.MarkerFile, malformed);

        var result = await fixture.Manager.ValidateOwnershipAsync(
            fixture.CreateCheckpoint(descriptor),
            fixture.TargetParent,
            CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Equal("DF-EXEC-003", result.Error?.Code);
        Assert.DoesNotContain(malformed, result.Error?.TechnicalDetail.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SemanticallyValidButNonCanonicalMarkerFailsClosed()
    {
        await using var fixture = await StagingFixture.CreateAsync();
        var created = await fixture.Manager.CreateAsync(fixture.Request, CancellationToken.None);
        Assert.True(created.IsSuccessful);
        var descriptor = created.Value.Workspace.Descriptor;
        await created.Value.DisposeAsync();
        var marker = await fixture.ReadMarkerAsync(descriptor.MarkerFile);
        await fixture.ReplaceMarkerAsync(
            descriptor.MarkerFile,
            marker.Replace("{", "{ ", StringComparison.Ordinal));

        var result = await fixture.Manager.ValidateOwnershipAsync(
            fixture.CreateCheckpoint(descriptor),
            fixture.TargetParent,
            CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Equal("DF-EXEC-003", result.Error?.Code);
    }

    [Fact]
    public async Task PayloadJunctionCannotEscapeTheGuardedWorkspace()
    {
        await using var fixture = await StagingFixture.CreateAsync();
        var created = await fixture.Manager.CreateAsync(fixture.Request, CancellationToken.None);
        Assert.True(created.IsSuccessful);
        var descriptor = created.Value.Workspace.Descriptor;
        await created.Value.DisposeAsync();
        fixture.ReplacePayloadWithEscapingJunction(descriptor);

        var result = await fixture.Manager.ValidateOwnershipAsync(
            fixture.CreateCheckpoint(descriptor),
            fixture.TargetParent,
            CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Equal("DF-EXEC-003", result.Error?.Code);
        Assert.Equal("outside remains unchanged", await File.ReadAllTextAsync(fixture.OutsideSentinelPath));
    }

    [Fact]
    public async Task NestedPayloadJunctionInvalidatesOwnershipBeforeResume()
    {
        await using var fixture = await StagingFixture.CreateAsync();
        var created = await fixture.Manager.CreateAsync(fixture.Request, CancellationToken.None);
        Assert.True(created.IsSuccessful);
        var descriptor = created.Value.Workspace.Descriptor;
        await created.Value.DisposeAsync();
        fixture.AddEscapingJunctionInsidePayload(descriptor);

        var result = await fixture.Manager.ValidateOwnershipAsync(
            fixture.CreateCheckpoint(descriptor),
            fixture.TargetParent,
            CancellationToken.None);
        if (result.IsSuccessful)
        {
            await result.Value.DisposeAsync();
        }

        Assert.False(result.IsSuccessful);
        Assert.Equal("DF-EXEC-003", result.Error?.Code);
        Assert.Equal("outside remains unchanged", await File.ReadAllTextAsync(fixture.OutsideSentinelPath));
    }

    [Fact]
    public async Task CleanupRequiresEligibleRunAndNonFinalizedCheckpoint()
    {
        await using var fixture = await StagingFixture.CreateAsync();
        var created = await fixture.Manager.CreateAsync(fixture.Request, CancellationToken.None);
        Assert.True(created.IsSuccessful);
        var descriptor = created.Value.Workspace.Descriptor;
        await created.Value.DisposeAsync();
        var finalized = fixture.CreateCheckpoint(
            descriptor,
            RunStatus.Cancelled,
            FinalizationState.Succeeded);

        var refused = await fixture.Manager.CleanupAsync(
            finalized,
            fixture.TargetParent,
            CancellationToken.None);

        Assert.False(refused.IsSuccessful);
        Assert.True(await fixture.TargetParent.DirectoryExistsAsync(
            descriptor.ContainerDirectory,
            CancellationToken.None));

        var eligible = fixture.CreateCheckpoint(descriptor, RunStatus.Cancelled);
        var cleaned = await fixture.Manager.CleanupAsync(
            eligible,
            fixture.TargetParent,
            CancellationToken.None);

        Assert.True(cleaned.IsSuccessful);
        Assert.False(await fixture.TargetParent.DirectoryExistsAsync(
            descriptor.ContainerDirectory,
            CancellationToken.None));
    }

    [Fact]
    public async Task PreCancelledCreateDoesNotMutateTargetParent()
    {
        await using var fixture = await StagingFixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Manager.CreateAsync(fixture.Request, cancellation.Token));

        Assert.False(await fixture.TargetParent.DirectoryExistsAsync(
            Relative(".devforge-staging"),
            CancellationToken.None));
    }

    [Fact]
    public async Task CleanupKeepsGlobalLeaseUntilContainerDeletionCompletes()
    {
        await using var fixture = await StagingFixture.CreateAsync();
        var created = await fixture.Manager.CreateAsync(fixture.Request, CancellationToken.None);
        Assert.True(created.IsSuccessful);
        var descriptor = created.Value.Workspace.Descriptor;
        await created.Value.DisposeAsync();
        var checkpoint = fixture.CreateCheckpoint(descriptor, RunStatus.Cancelled);
        var reopenSucceeded = false;
        var probingWorkspace = new InterceptingWorkspace(
            fixture.TargetParent,
            beforeDelete: async () =>
            {
                var reopened = await fixture.Manager.ValidateOwnershipAsync(
                    checkpoint,
                    fixture.TargetParent,
                    CancellationToken.None);
                reopenSucceeded = reopened.IsSuccessful;
                if (reopened.IsSuccessful)
                {
                    await reopened.Value.DisposeAsync();
                }
            });

        var result = await fixture.Manager.CleanupAsync(
            checkpoint,
            probingWorkspace,
            CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.False(reopenSucceeded);
    }

    [Fact]
    public async Task ResumeRefusesTargetThatAppearedAfterStagingCreation()
    {
        await using var fixture = await StagingFixture.CreateAsync();
        var created = await fixture.Manager.CreateAsync(fixture.Request, CancellationToken.None);
        Assert.True(created.IsSuccessful);
        var descriptor = created.Value.Workspace.Descriptor;
        await created.Value.DisposeAsync();
        await fixture.TargetParent.CreateDirectoryAsync(
            fixture.Request.TargetDirectory,
            CancellationToken.None);

        var result = await fixture.Manager.ValidateOwnershipAsync(
            fixture.CreateCheckpoint(descriptor),
            fixture.TargetParent,
            CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Equal("DF-FINAL-001", result.Error?.Code);
        Assert.True(await fixture.TargetParent.DirectoryExistsAsync(
            descriptor.ContainerDirectory,
            CancellationToken.None));
    }

    [Fact]
    public async Task CancellationDuringMarkerWriteRemovesIncompleteRunContainer()
    {
        await using var fixture = await StagingFixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        var cancellingWorkspace = new InterceptingWorkspace(
            fixture.TargetParent,
            markerWriteCancellation: cancellation);
        var request = fixture.CreateRequestForRun("run-1", cancellingWorkspace);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Manager.CreateAsync(request, cancellation.Token));

        Assert.False(await fixture.TargetParent.DirectoryExistsAsync(
            Relative(".devforge-staging\\run-1"),
            CancellationToken.None));
    }

    [Fact]
    public async Task NonCanonicalRunIdentifierFailsClosedWithoutCreatingStaging()
    {
        await using var fixture = await StagingFixture.CreateAsync("..");

        var result = await fixture.Manager.CreateAsync(fixture.Request, CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Equal("DF-EXEC-003", result.Error?.Code);
        Assert.False(await fixture.TargetParent.DirectoryExistsAsync(
            Relative(".devforge-staging"),
            CancellationToken.None));
    }

    private static WorkspaceRelativePath Relative(string value) =>
        WorkspaceRelativePath.Create(value).Value;

    private sealed class StagingFixture : IAsyncDisposable
    {
        private string? _payloadJunctionPath;
        private string? _outsideRootPath;

        private StagingFixture(
            string rootPath,
            string targetParentPath,
            string runArtifactPath,
            IWorkspaceFileSystem targetParent,
            IWorkspaceFileSystem runArtifacts,
            ExecutionRequest request)
        {
            RootPath = rootPath;
            TargetParentPath = targetParentPath;
            RunArtifactPath = runArtifactPath;
            TargetParent = targetParent;
            RunArtifacts = runArtifacts;
            Request = request;
            Manager = new OwnedStagingWorkspaceManager(new WindowsFileSystem());
        }

        public string RootPath { get; }

        public string TargetParentPath { get; }

        public string RunArtifactPath { get; }

        public IWorkspaceFileSystem TargetParent { get; }

        public IWorkspaceFileSystem RunArtifacts { get; }

        public ExecutionRequest Request { get; }

        public OwnedStagingWorkspaceManager Manager { get; }

        public string OutsideSentinelPath => Path.Combine(
            _outsideRootPath ?? throw new InvalidOperationException("No outside root was created."),
            "sentinel.txt");

        public ExecutionRequest CreateRequestForRun(
            string runId,
            IWorkspaceFileSystem? targetParent = null) =>
            CreateRequest(targetParent ?? TargetParent, RunArtifacts, runId);

        public static async Task<StagingFixture> CreateAsync(string runId = "run-1")
        {
            var rootPath = Path.GetFullPath(Path.Combine(
                Path.GetTempPath(),
                "DevForge.StagingTests",
                Guid.NewGuid().ToString("N")));
            var targetParentPath = Path.Combine(rootPath, "target-parent");
            var runArtifactPath = Path.Combine(rootPath, "run-artifacts");
            Directory.CreateDirectory(targetParentPath);
            Directory.CreateDirectory(runArtifactPath);
            var fileSystem = new WindowsFileSystem();
            var targetParent = await fileSystem.OpenWorkspaceAsync(
                WorkspaceRoot.Create(targetParentPath).Value,
                CancellationToken.None);
            var runArtifacts = await fileSystem.OpenWorkspaceAsync(
                WorkspaceRoot.Create(runArtifactPath).Value,
                CancellationToken.None);
            var request = CreateRequest(targetParent, runArtifacts, runId);
            return new StagingFixture(
                rootPath,
                targetParentPath,
                runArtifactPath,
                targetParent,
                runArtifacts,
                request);
        }

        public RunCheckpoint CreateCheckpoint(
            StagingDescriptor staging,
            RunStatus status = RunStatus.Draft,
            FinalizationState finalizationState = FinalizationState.NotStarted)
        {
            var run = status switch
            {
                RunStatus.Draft => Request.Run,
                RunStatus.Cancelled => Request.Run.TransitionTo(RunStatus.Cancelled).Value,
                _ => throw new ArgumentOutOfRangeException(nameof(status)),
            };
            var target = TargetDescriptor.Create(
                TargetParent.Root,
                Request.TargetDirectory,
                null).Value;
            return RunCheckpoint.Create(
                run,
                Request.PlannedProject.Plan,
                Request.PlannedProject.Preview.Blueprint,
                Request.PlannedProject.BlueprintFingerprint,
                staging,
                target,
                RunArtifactDescriptor.Create(RunArtifacts.Root).Value,
                [],
                finalizationState,
                ReportPersistenceState.NotStarted).Value;
        }

        public async Task<string> ReadMarkerAsync(WorkspaceRelativePath markerPath)
        {
            await using var stream = await TargetParent.OpenReadAsync(markerPath, CancellationToken.None);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return await reader.ReadToEndAsync(CancellationToken.None);
        }

        public async Task<string> ReadTextAsync(WorkspaceRelativePath path)
        {
            await using var stream = await TargetParent.OpenReadAsync(path, CancellationToken.None);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return await reader.ReadToEndAsync(CancellationToken.None);
        }

        public async Task ReplaceMarkerAsync(WorkspaceRelativePath markerPath, string content)
        {
            await TargetParent.DeleteFileAsync(markerPath, CancellationToken.None);
            await WriteMarkerAsync(markerPath, content);
        }

        public async Task WriteMarkerAsync(WorkspaceRelativePath markerPath, string content)
        {
            await using var stream = await TargetParent.OpenWriteAsync(
                markerPath,
                overwrite: false,
                CancellationToken.None);
            await stream.WriteAsync(Encoding.UTF8.GetBytes(content), CancellationToken.None);
        }

        public void ReplacePayloadWithEscapingJunction(StagingDescriptor descriptor)
        {
            var payloadPath = Path.GetFullPath(Path.Combine(
                TargetParentPath,
                descriptor.PayloadDirectory.Value));
            var expectedPrefix = TargetParentPath + Path.DirectorySeparatorChar;
            if (!payloadPath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Refusing to replace a payload outside the test root.");
            }

            Directory.Delete(payloadPath);
            _outsideRootPath = Path.GetFullPath(Path.Combine(
                Path.GetTempPath(),
                "DevForge.StagingTests.Outside",
                Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(_outsideRootPath);
            File.WriteAllText(OutsideSentinelPath, "outside remains unchanged");
            JunctionFixture.Create(payloadPath, _outsideRootPath);
            _payloadJunctionPath = payloadPath;
        }

        public void CreateTargetJunctionToOutside()
        {
            _outsideRootPath = Path.GetFullPath(Path.Combine(
                Path.GetTempPath(),
                "DevForge.StagingTests.Outside",
                Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(_outsideRootPath);
            File.WriteAllText(OutsideSentinelPath, "outside remains unchanged");
            var targetPath = Path.Combine(TargetParentPath, Request.TargetDirectory.Value);
            JunctionFixture.Create(targetPath, _outsideRootPath);
            _payloadJunctionPath = targetPath;
        }

        public void AddEscapingJunctionInsidePayload(StagingDescriptor descriptor)
        {
            _outsideRootPath = Path.GetFullPath(Path.Combine(
                Path.GetTempPath(),
                "DevForge.StagingTests.Outside",
                Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(_outsideRootPath);
            File.WriteAllText(OutsideSentinelPath, "outside remains unchanged");
            var junctionPath = Path.GetFullPath(Path.Combine(
                TargetParentPath,
                descriptor.PayloadDirectory.Value,
                "linked"));
            JunctionFixture.Create(junctionPath, _outsideRootPath);
            _payloadJunctionPath = junctionPath;
        }

        public ValueTask DisposeAsync()
        {
            if (_payloadJunctionPath is not null && Directory.Exists(_payloadJunctionPath))
            {
                Directory.Delete(_payloadJunctionPath);
            }

            var safeParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "DevForge.StagingTests"));
            var resolved = Path.GetFullPath(RootPath);
            if (!resolved.StartsWith(
                    safeParent + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Refusing to remove a directory outside the test root.");
            }

            if (Directory.Exists(resolved))
            {
                Directory.Delete(resolved, recursive: true);
            }

            if (_outsideRootPath is not null)
            {
                var safeOutsideParent = Path.GetFullPath(Path.Combine(
                    Path.GetTempPath(),
                    "DevForge.StagingTests.Outside"));
                var outside = Path.GetFullPath(_outsideRootPath);
                if (!outside.StartsWith(
                        safeOutsideParent + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Refusing to remove an unexpected outside directory.");
                }

                if (Directory.Exists(outside))
                {
                    Directory.Delete(outside, recursive: true);
                }
            }

            return ValueTask.CompletedTask;
        }

        private static ExecutionRequest CreateRequest(
            IWorkspaceFileSystem targetParent,
            IWorkspaceFileSystem runArtifacts,
            string runId)
        {
            var hash = $"sha256:{new string('1', 64)}";
            var step = ExecutionStep.Create(
                "create",
                "Create",
                "create-directory",
                [],
                TimeSpan.FromSeconds(30),
                RetryPolicy.None).Value;
            var plan = ExecutionPlan.Create(hash, [step], []).Value;
            var reference = BlueprintReference.Create("desktop.csharp-wpf-tool", "1.0.0").Value;
            var preview = PlanPreview.Create(
                reference,
                [new PlanPreviewStep("create", "create-directory", TimeSpan.FromSeconds(30))],
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                GitOptions.Create().Value,
                CompletionOptions.Create().Value,
                hash).Value;
            var fingerprint = BlueprintFingerprint.Create(
                "built-in",
                Relative("desktop.csharp-wpf-tool\\1.0.0"),
                BlueprintTrust.BuiltIn,
                $"sha256:{new string('2', 64)}").Value;
            var planned = PlannedProject.Create(plan, preview, fingerprint).Value;
            return ExecutionRequest.Create(
                planned,
                ProjectRun.Create(runId, "recipe-1").Value,
                targetParent,
                Relative("project"),
                runArtifacts,
                ExecutionMode.Fresh).Value;
        }
    }

    private sealed class InterceptingWorkspace : IAtomicWorkspaceFileSystem
    {
        private readonly IWorkspaceFileSystem _inner;
        private readonly CancellationTokenSource? _markerWriteCancellation;
        private readonly Func<Task>? _beforeDelete;
        private readonly Func<WorkspaceRelativePath, CancellationToken, Task<bool>>? _atomicCreate;

        public InterceptingWorkspace(
            IWorkspaceFileSystem inner,
            CancellationTokenSource? markerWriteCancellation = null,
            Func<Task>? beforeDelete = null,
            Func<WorkspaceRelativePath, CancellationToken, Task<bool>>? atomicCreate = null)
        {
            _inner = inner;
            _markerWriteCancellation = markerWriteCancellation;
            _beforeDelete = beforeDelete;
            _atomicCreate = atomicCreate;
        }

        public WorkspaceRoot Root => _inner.Root;

        public Task<bool> FileExistsAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) =>
            _inner.FileExistsAsync(path, cancellationToken);

        public Task<bool> DirectoryExistsAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) =>
            _inner.DirectoryExistsAsync(path, cancellationToken);

        public Task CreateDirectoryAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) =>
            _inner.CreateDirectoryAsync(path, cancellationToken);

        public Task<bool> TryCreateDirectoryAsync(
            WorkspaceRelativePath path,
            CancellationToken cancellationToken)
        {
            return _atomicCreate is not null
                ? _atomicCreate(path, cancellationToken)
                : (_inner as IAtomicWorkspaceFileSystem
                    ?? throw new InvalidOperationException("The wrapped workspace is not atomic."))
                .TryCreateDirectoryAsync(path, cancellationToken);
        }

        public Task<Stream> OpenReadAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) =>
            _inner.OpenReadAsync(path, cancellationToken);

        public async Task<Stream> OpenWriteAsync(
            WorkspaceRelativePath path,
            bool overwrite,
            CancellationToken cancellationToken)
        {
            var stream = await _inner.OpenWriteAsync(path, overwrite, cancellationToken);
            return _markerWriteCancellation is not null
                && path.Value.EndsWith("\\ownership.json", StringComparison.Ordinal)
                ? new CancelOnFirstWriteStream(stream, _markerWriteCancellation)
                : stream;
        }

        public Task DeleteFileAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) =>
            _inner.DeleteFileAsync(path, cancellationToken);

        public Task<System.Collections.Immutable.ImmutableArray<WorkspaceRelativePath>> EnumerateAllFilesAsync(
            CancellationToken cancellationToken) =>
            _inner.EnumerateAllFilesAsync(cancellationToken);

        public Task<System.Collections.Immutable.ImmutableArray<WorkspaceRelativePath>> EnumerateRootDirectoriesAsync(
            CancellationToken cancellationToken) =>
            _inner.EnumerateRootDirectoriesAsync(cancellationToken);

        public Task<System.Collections.Immutable.ImmutableArray<WorkspaceRelativePath>> EnumerateFilesAsync(
            WorkspaceRelativePath directory,
            bool recursive,
            CancellationToken cancellationToken) =>
            _inner.EnumerateFilesAsync(directory, recursive, cancellationToken);

        public Task<System.Collections.Immutable.ImmutableArray<WorkspaceRelativePath>> EnumerateDirectoriesAsync(
            WorkspaceRelativePath directory,
            CancellationToken cancellationToken) =>
            _inner.EnumerateDirectoriesAsync(directory, cancellationToken);

        public async Task DeleteDirectoryAsync(
            WorkspaceRelativePath path,
            DirectoryCleanupIntent intent,
            CancellationToken cancellationToken)
        {
            if (_beforeDelete is not null)
            {
                await _beforeDelete();
            }

            await _inner.DeleteDirectoryAsync(path, intent, cancellationToken);
        }

        public Task MoveDirectoryAsync(
            WorkspaceRelativePath source,
            WorkspaceRelativePath destination,
            WorkspaceMoveIntent intent,
            CancellationToken cancellationToken) =>
            _inner.MoveDirectoryAsync(source, destination, intent, cancellationToken);
    }

    private sealed class CancelOnFirstWriteStream : Stream
    {
        private readonly Stream _inner;
        private readonly CancellationTokenSource _cancellation;

        public CancelOnFirstWriteStream(Stream inner, CancellationTokenSource cancellation)
        {
            _inner = inner;
            _cancellation = cancellation;
        }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => _inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            _cancellation.Cancel();
            return ValueTask.FromCanceled(_cancellation.Token);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
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
