using System.Collections.Immutable;
using DevForge.Application.Contracts;
using DevForge.Application.Publication;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Diagnostics;
using DevForge.Domain.Execution;
using DevForge.Domain.Privacy;
using DevForge.Domain.Projects;
using DevForge.Domain.Runs;

namespace DevForge.UnitTests.Application.Publication;

public sealed class ProjectPublicationCoordinatorTests
{
    [Fact]
    public async Task GitOnlyPersistsEveryPhaseAndCompletesWithReceipt()
    {
        var fixture = Fixture.Create(publishToGitHub: false);

        var result = await fixture.Sut.PublishAsync(fixture.Request, CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.Equal(RunStatus.Completed, result.Value.Run.Status);
        Assert.Equal(GitPublicationState.Succeeded, result.Value.Publication.GitState);
        Assert.Equal(GitHubPublicationState.NotRequested, result.Value.Publication.GitHubState);
        Assert.Equal(PublicationReceiptState.Succeeded, result.Value.Publication.ReceiptState);
        Assert.Equal(
            [
                GitPublicationState.IntentPersisted,
                GitPublicationState.RepositoryInitialized,
                GitPublicationState.Committed,
                GitPublicationState.Succeeded,
                GitPublicationState.Succeeded,
                GitPublicationState.Succeeded,
            ],
            fixture.Store.Saves.Select(item => item.Publication.GitState));
        Assert.Equal(1, fixture.Git.BootstrapCalls);
        Assert.Equal(0, fixture.GitHub.PublishCalls);
        Assert.Equal(1, fixture.Receipts.WriteCalls);
        Assert.All(fixture.Store.SaveTokens, token => Assert.False(token.CanBeCanceled));

        var saveCount = fixture.Store.Saves.Count;
        var verified = await fixture.Sut.PublishAsync(fixture.Request, CancellationToken.None);
        Assert.True(verified.IsSuccessful);
        Assert.Equal(PublicationReceiptAccessMode.VerifyOnly, fixture.Receipts.Modes[^1]);
        Assert.Equal(saveCount, fixture.Store.Saves.Count);
    }

    [Fact]
    public async Task GitHubPublicationPersistsNonceAndRemoteCreatedBeforeCompletion()
    {
        var fixture = Fixture.Create(publishToGitHub: true);

        var result = await fixture.Sut.PublishAsync(fixture.Request, CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.Contains(
            fixture.Store.Saves,
            item => item.Publication.GitHubState == GitHubPublicationState.IntentPersisted
                && item.Publication.OwnershipNonce == Fixture.Nonce);
        Assert.Contains(
            fixture.Store.Saves,
            item => item.Publication.GitHubState == GitHubPublicationState.RemoteCreated);
        Assert.Equal(GitHubPublicationState.Succeeded, result.Value.Publication.GitHubState);
        Assert.Equal("https://github.com/octocat/devforge", result.Value.Publication.RepositoryUrl);
        Assert.Equal(1, fixture.GitHub.PublishCalls);
    }

    [Fact]
    public async Task SafeReadOnlyRefusesBeforeLeaseOrCheckpointAccess()
    {
        var fixture = Fixture.Create(publishToGitHub: false);
        var request = PublicationRequest.Create(fixture.Request.RunId, PublicationMutationMode.SafeReadOnly).Value;

        var result = await fixture.Sut.PublishAsync(request, CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Equal("DF-PUB-READONLY", result.Error!.Code);
        Assert.Equal(0, fixture.Leases.AcquireCalls);
        Assert.Equal(0, fixture.Store.FindCalls);
        Assert.Empty(fixture.Store.Saves);
    }

    [Fact]
    public async Task LeaseContentionRefusesBeforeAuthoritativeReloadOrMutation()
    {
        var fixture = Fixture.Create(publishToGitHub: false, leaseAvailable: false);

        var result = await fixture.Sut.PublishAsync(fixture.Request, CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Equal("DF-PUB-LEASE", result.Error!.Code);
        Assert.Equal(0, fixture.Store.FindCalls);
        Assert.Equal(0, fixture.Git.BootstrapCalls);
    }

    [Fact]
    public async Task CancellationAfterIntentPersistsRecoverableFailureAndPropagates()
    {
        var fixture = Fixture.Create(publishToGitHub: false, cancelGit: true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Sut.PublishAsync(fixture.Request, new CancellationTokenSource().Token));

        var last = Assert.Single(
            fixture.Store.Saves,
            item => item.Publication.GitState == GitPublicationState.Failed);
        Assert.Equal(RunStatus.PublishPending, last.Run.Status);
        Assert.Equal(fixture.Checkpoint.Run.Attempts, last.Run.Attempts);
        Assert.False(fixture.Leases.Lease.IsHeld);
    }

    [Fact]
    public async Task RetryReloadsFailedCheckpointAndDoesNotDuplicateGenerationEvidence()
    {
        var fixture = Fixture.Create(publishToGitHub: false);
        var pending = fixture.MakePending(GitPublicationState.Failed);
        fixture.Store.Current = pending;

        var result = await fixture.Sut.PublishAsync(fixture.Request, CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.Equal(RunStatus.Completed, result.Value.Run.Status);
        Assert.Equal(pending.Run.Attempts, result.Value.Run.Attempts);
        Assert.Equal(pending.Evidence, result.Value.Evidence);
        Assert.Equal(1, fixture.Git.BootstrapCalls);
    }

    [Fact]
    public async Task PersistedReceiptIntentMustUseTheExactRunBoundPath()
    {
        var fixture = Fixture.Create(publishToGitHub: false);
        fixture.Store.Current = fixture.MakeReceiptIntent("reports\\other.publication.json");

        var result = await fixture.Sut.PublishAsync(fixture.Request, CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Equal("DF-PUB-001", result.Error!.Code);
        Assert.Equal(0, fixture.Receipts.WriteCalls);
        Assert.Empty(fixture.Store.Saves);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PersistedSucceededEvidenceIsReverifiedBeforeReceiptOrCompletion(
        bool publishToGitHub)
    {
        var fixture = Fixture.Create(
            publishToGitHub,
            failGitVerification: !publishToGitHub,
            failGitHubVerification: publishToGitHub);
        fixture.Store.Current = fixture.MakeSucceededBeforeReceipt(publishToGitHub);

        var result = await fixture.Sut.PublishAsync(fixture.Request, CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Equal(RunStatus.PublishPending, fixture.Store.Current.Run.Status);
        Assert.Equal(0, fixture.Receipts.WriteCalls);
        Assert.Equal(1, fixture.Git.VerifyCalls);
        Assert.Equal(publishToGitHub ? 1 : 0, fixture.GitHub.VerifyCalls);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(false, 3)]
    [InlineData(false, 4)]
    [InlineData(false, 5)]
    [InlineData(false, 6)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(true, 3)]
    [InlineData(true, 4)]
    [InlineData(true, 5)]
    [InlineData(true, 6)]
    [InlineData(true, 7)]
    [InlineData(true, 8)]
    [InlineData(true, 9)]
    public async Task AppKillAfterEveryDurablePhaseResumesToExactCompletion(
        bool publishToGitHub,
        int killAfterSave)
    {
        var fixture = Fixture.Create(publishToGitHub);
        fixture.Store.TerminateAfterSaveNumber = killAfterSave;

        await Assert.ThrowsAsync<AccessViolationException>(
            () => fixture.Sut.PublishAsync(fixture.Request, CancellationToken.None));

        fixture.Store.ResumeProcess();
        var recovered = await fixture.Sut.PublishAsync(fixture.Request, CancellationToken.None);

        Assert.True(recovered.IsSuccessful);
        Assert.Equal(RunStatus.Completed, recovered.Value.Run.Status);
        Assert.Equal(GitPublicationState.Succeeded, recovered.Value.Publication.GitState);
        Assert.Equal(PublicationReceiptState.Succeeded, recovered.Value.Publication.ReceiptState);
        Assert.Equal(
            publishToGitHub ? GitHubPublicationState.Succeeded : GitHubPublicationState.NotRequested,
            recovered.Value.Publication.GitHubState);
        Assert.Equal(fixture.Checkpoint.Run.Attempts, recovered.Value.Run.Attempts);
        Assert.Equal(fixture.Checkpoint.Evidence, recovered.Value.Evidence);
    }

    [Theory]
    [InlineData("commit", false)]
    [InlineData("develop", false)]
    [InlineData("origin", true)]
    [InlineData("push-main", true)]
    [InlineData("push-develop", true)]
    [InlineData("receipt-write", true)]
    public async Task AppKillAfterIrreversibleMutationResumesWithoutDuplicatingEffect(
        string killWindow,
        bool publishToGitHub)
    {
        var fixture = Fixture.Create(
            publishToGitHub,
            useDevelopBranch: killWindow is "develop" or "push-develop",
            killWindow: killWindow);

        await Assert.ThrowsAsync<AccessViolationException>(
            () => fixture.Sut.PublishAsync(fixture.Request, CancellationToken.None));

        var recovered = await fixture.Sut.PublishAsync(fixture.Request, CancellationToken.None);

        Assert.True(recovered.IsSuccessful);
        Assert.Equal(RunStatus.Completed, recovered.Value.Run.Status);
        Assert.Equal(1, fixture.MutationCount(killWindow));
        Assert.Equal(fixture.Checkpoint.Run.Attempts, recovered.Value.Run.Attempts);
        Assert.Equal(fixture.Checkpoint.Evidence, recovered.Value.Evidence);
    }

    private sealed class Fixture
    {
        public const string RunId = "run-0123456789abcdef0123456789abcdef";
        public const string Digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        public const string Commit = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        public const string Nonce = "cccccccccccccccccccccccccccccccc";

        private Fixture(
            RunCheckpoint checkpoint,
            Store store,
            LeaseProvider leases,
            GitService git,
            GitHubService gitHub,
            ReceiptStore receipts,
            ProjectPublicationCoordinator sut)
        {
            Checkpoint = checkpoint;
            Store = store;
            Leases = leases;
            Git = git;
            GitHub = gitHub;
            Receipts = receipts;
            Sut = sut;
            Request = PublicationRequest.Create(RunId, PublicationMutationMode.Normal).Value;
        }

        public RunCheckpoint Checkpoint { get; }
        public Store Store { get; }
        public LeaseProvider Leases { get; }
        public GitService Git { get; }
        public GitHubService GitHub { get; }
        public ReceiptStore Receipts { get; }
        public ProjectPublicationCoordinator Sut { get; }
        public PublicationRequest Request { get; }

        public static Fixture Create(
            bool publishToGitHub,
            bool leaseAvailable = true,
            bool cancelGit = false,
            bool failGitVerification = false,
            bool failGitHubVerification = false,
            bool useDevelopBranch = false,
            string? killWindow = null)
        {
            var checkpoint = CreateCheckpoint(publishToGitHub, useDevelopBranch);
            var store = new Store(checkpoint);
            var leases = new LeaseProvider(leaseAvailable);
            var finalProject = new Workspace("C:\\target-parent\\project");
            var artifacts = new Workspace("C:\\artifacts");
            var workspaces = new WorkspaceFactory(finalProject, artifacts);
            var git = new GitService(cancelGit, failGitVerification, killWindow);
            var gitHub = new GitHubService(failGitHubVerification, killWindow);
            var receipts = new ReceiptStore(killWindow);
            var sut = new ProjectPublicationCoordinator(
                store,
                leases,
                workspaces,
                git,
                gitHub,
                receipts,
                new NonceGenerator());
            return new Fixture(checkpoint, store, leases, git, gitHub, receipts, sut);
        }

        public int MutationCount(string name) => name switch
        {
            "commit" or "develop" => Git.MutationCount(name),
            "origin" or "push-main" or "push-develop" => GitHub.MutationCount(name),
            "receipt-write" => Receipts.WriteEffects,
            _ => 0,
        };

        public RunCheckpoint MakePending(GitPublicationState gitState)
        {
            var publication = PublicationSnapshot.Create(
                gitState,
                GitHubPublicationState.NotRequested,
                PublicationReceiptState.NotRequested,
                Digest,
                null,
                [],
                null,
                true,
                null,
                null,
                null,
                null).Value;
            return Recreate(
                Checkpoint,
                Checkpoint.Run.TransitionTo(RunStatus.PublishPending).Value,
                publication);
        }

        public RunCheckpoint MakeReceiptIntent(string path)
        {
            var publication = PublicationSnapshot.Create(
                GitPublicationState.Succeeded,
                GitHubPublicationState.NotRequested,
                PublicationReceiptState.IntentPersisted,
                Digest,
                Commit,
                ["main"],
                null,
                true,
                null,
                null,
                Path(path),
                Hash('d')).Value;
            return Recreate(
                Checkpoint,
                Checkpoint.Run.TransitionTo(RunStatus.PublishPending).Value,
                publication);
        }

        public RunCheckpoint MakeSucceededBeforeReceipt(bool publishToGitHub)
        {
            var identity = publishToGitHub
                ? GitHubRepositoryIdentity.Create("octocat", "devforge").Value
                : null;
            var publication = PublicationSnapshot.Create(
                GitPublicationState.Succeeded,
                publishToGitHub
                    ? GitHubPublicationState.Succeeded
                    : GitHubPublicationState.NotRequested,
                PublicationReceiptState.NotRequested,
                Digest,
                Commit,
                ["main"],
                identity,
                true,
                publishToGitHub ? Nonce : null,
                publishToGitHub ? identity!.HttpsWebUrl : null,
                null,
                null).Value;
            return Recreate(
                Checkpoint,
                Checkpoint.Run.TransitionTo(RunStatus.PublishPending).Value,
                publication);
        }

        private static RunCheckpoint CreateCheckpoint(bool publishToGitHub, bool useDevelopBranch)
        {
            var git = GitOptions.Create(
                useDevelopBranch: useDevelopBranch,
                publishToGitHub: publishToGitHub,
                githubAccount: publishToGitHub ? "octocat" : null,
                githubRepository: publishToGitHub ? "devforge" : null).Value;
            var step = ExecutionStep.Create(
                "create", "Create", "create-directory", [],
                TimeSpan.FromSeconds(30), RetryPolicy.None).Value;
            var plan = ExecutionPlan.Create(Hash('1'), [step], []).Value;
            var blueprint = BlueprintReference.Create("desktop.csharp-wpf-tool", "1.0.0").Value;
            var preview = PlanPreview.Create(
                blueprint,
                [new PlanPreviewStep("create", "create-directory", TimeSpan.FromSeconds(30))],
                [], [], [], [], [], [], [], [],
                git,
                CompletionOptions.Create().Value,
                plan.Id).Value;
            var fingerprint = BlueprintFingerprint.Create(
                "built-in", Path("desktop.csharp-wpf-tool\\1.0.0"),
                BlueprintTrust.BuiltIn, Hash('2')).Value;
            var run = ProjectRun.Create(RunId, "recipe-1").Value
                .TransitionTo(RunStatus.Planning).Value
                .TransitionTo(RunStatus.Executing).Value
                .TransitionTo(RunStatus.LocalReady).Value;
            return RunCheckpoint.Create(
                run,
                plan,
                preview,
                blueprint,
                fingerprint,
                StagingDescriptor.Create(
                    Path($".devforge-staging\\{RunId}"),
                    Path($".devforge-staging\\{RunId}\\payload"),
                    Path($".devforge-staging\\{RunId}\\ownership.json"),
                    "marker-1").Value,
                TargetDescriptor.Create(
                    WorkspaceRoot.Create("C:\\target-parent").Value,
                    Path("project"),
                    null).Value,
                RunArtifactDescriptor.Create(WorkspaceRoot.Create("C:\\artifacts").Value).Value,
                [],
                FinalizationState.Succeeded,
                ReportPersistenceState.Succeeded,
                PublicationSnapshot.CreateNotRequested(Digest).Value).Value;
        }

        private static RunCheckpoint Recreate(
            RunCheckpoint checkpoint,
            ProjectRun run,
            PublicationSnapshot publication) => RunCheckpoint.Create(
                run, checkpoint.Plan, checkpoint.Preview, checkpoint.Blueprint,
                checkpoint.BlueprintFingerprint, checkpoint.Staging, checkpoint.Target,
                checkpoint.RunArtifacts, checkpoint.Evidence, checkpoint.FinalizationState,
                checkpoint.ReportState, publication).Value;

        private static string Hash(char value) => $"sha256:{new string(value, 64)}";
        private static WorkspaceRelativePath Path(string value) => WorkspaceRelativePath.Create(value).Value;
    }

    private sealed class Store(RunCheckpoint current) : IRunCheckpointStore
    {
        public RunCheckpoint Current { get; set; } = current;
        public int FindCalls { get; private set; }
        public List<RunCheckpoint> Saves { get; } = [];
        public List<CancellationToken> SaveTokens { get; } = [];
        public int? TerminateAfterSaveNumber { get; set; }
        private bool IsTerminated { get; set; }

        public Task<RunCheckpoint?> FindAsync(string runId, CancellationToken cancellationToken)
        {
            FindCalls++;
            return Task.FromResult<RunCheckpoint?>(Current.Run.Id == runId ? Current : null);
        }

        public Task SaveAsync(RunCheckpoint checkpoint, CancellationToken cancellationToken)
        {
            if (IsTerminated)
            {
#pragma warning disable CA2201 // Simulates immediate process termination after a durable write.
                throw new AccessViolationException();
#pragma warning restore CA2201
            }

            Current = checkpoint;
            Saves.Add(checkpoint);
            SaveTokens.Add(cancellationToken);
            if (Saves.Count == TerminateAfterSaveNumber)
            {
                IsTerminated = true;
#pragma warning disable CA2201 // Simulates immediate process termination after a durable write.
                throw new AccessViolationException();
#pragma warning restore CA2201
            }

            return Task.CompletedTask;
        }

        public void ResumeProcess()
        {
            IsTerminated = false;
            TerminateAfterSaveNumber = null;
        }

        public Task<ImmutableArray<RunCheckpoint>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ImmutableArray.Create(Current));
    }

    private sealed class LeaseProvider(bool available) : IPublicationLeaseProvider
    {
        public int AcquireCalls { get; private set; }
        public Lease Lease { get; } = new();

        public Task<ExecutionOperationResult<IPublicationLease>> AcquireAsync(
            string runId,
            CancellationToken cancellationToken)
        {
            AcquireCalls++;
            return Task.FromResult(available
                ? ExecutionOperationResult.Success<IPublicationLease>(Lease)
                : ExecutionOperationResult.Failure<IPublicationLease>(Error("DF-PUB-LEASE")));
        }
    }

    private sealed class Lease : IPublicationLease
    {
        public bool IsHeld { get; private set; } = true;
        public ValueTask DisposeAsync()
        {
            IsHeld = false;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class WorkspaceFactory(Workspace finalProject, Workspace artifacts)
        : IProjectPublicationWorkspaceFactory
    {
        public Task<ExecutionOperationResult<ProjectPublicationWorkspaces>> OpenAsync(
            RunCheckpoint checkpoint,
            CancellationToken cancellationToken) => Task.FromResult(
                ExecutionOperationResult.Success(
                    new ProjectPublicationWorkspaces(finalProject, artifacts)));
    }

    private sealed class GitService(bool cancel, bool failVerification, string? killWindow)
        : IGitService, IPublicationGitService
    {
        private readonly Dictionary<string, int> _mutations = [];
        private readonly HashSet<string> _killed = [];
        public int BootstrapCalls { get; private set; }
        public int VerifyCalls { get; private set; }

        public Task<GitRepositoryReceipt> BootstrapAsync(
            GitBootstrapRequest request,
            CancellationToken cancellationToken) =>
            BootstrapAsync(request, new NoopGitProgress(), cancellationToken);

        public async Task<GitRepositoryReceipt> BootstrapAsync(
            GitBootstrapRequest request,
            IGitPublicationProgress progress,
            CancellationToken cancellationToken)
        {
            BootstrapCalls++;
            await progress.RepositoryInitializedAsync(CancellationToken.None);
            if (cancel)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            Mutate("commit");
            if (request.BranchPolicy == GitBranchPolicy.MainAndDevelop)
            {
                Mutate("develop");
            }

            return GitRepositoryReceipt.Create(
                Fixture.Commit,
                request.BranchPolicy,
                request.BranchPolicy == GitBranchPolicy.Main ? ["main"] : ["main", "develop"],
                request.FinalTreeDigest).Value;
        }

        public int MutationCount(string name) => _mutations.GetValueOrDefault(name);

        private void Mutate(string name)
        {
            if (!_mutations.ContainsKey(name))
            {
                _mutations[name] = 1;
            }

            if (StringComparer.Ordinal.Equals(killWindow, name) && _killed.Add(name))
            {
#pragma warning disable CA2201 // Simulates immediate process termination after an irreversible effect.
                throw new AccessViolationException();
#pragma warning restore CA2201
            }
        }

        public Task<GitRepositoryReceipt> VerifyAsync(
            GitVerificationRequest request,
            CancellationToken cancellationToken)
        {
            VerifyCalls++;
            if (failVerification)
            {
                throw new InvalidOperationException("Simulated local publication drift.");
            }

            return Task.FromResult(GitRepositoryReceipt.Create(
                request.InitialCommitId,
                request.BranchPolicy,
                request.BranchPolicy == GitBranchPolicy.Main ? ["main"] : ["main", "develop"],
                request.FinalTreeDigest).Value);
        }
    }

    private sealed class NoopGitProgress : IGitPublicationProgress
    {
        public Task RepositoryInitializedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class GitHubService(bool failVerification, string? killWindow)
        : IGitHubService, IPublicationGitHubService
    {
        private readonly Dictionary<string, int> _mutations = [];
        private readonly HashSet<string> _killed = [];
        public int PublishCalls { get; private set; }
        public int VerifyCalls { get; private set; }

        public Task<GitHubAuthenticationResult> CheckAuthenticationAsync(
            GitHubAuthenticationRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<GitHubPublishResult> PublishAsync(
            GitHubPublishRequest request,
            CancellationToken cancellationToken) =>
            PublishAsync(request, new NoopGitHubProgress(), cancellationToken);

        public async Task<GitHubPublishResult> PublishAsync(
            GitHubPublishRequest request,
            IGitHubPublicationProgress progress,
            CancellationToken cancellationToken)
        {
            PublishCalls++;
            await progress.RemoteCreatedAsync(CancellationToken.None);
            Mutate("origin");
            foreach (var branch in request.Branches)
            {
                Mutate($"push-{branch}");
            }

            return GitHubPublishResult.Create(
                request.Repository,
                request.Repository.HttpsWebUrl,
                request.InitialCommitId,
                request.Branches,
                request.BranchPolicy,
                request.IsPrivate,
                request.OwnershipNonce).Value;
        }

        public int MutationCount(string name) => _mutations.GetValueOrDefault(name);

        private void Mutate(string name)
        {
            if (!_mutations.ContainsKey(name))
            {
                _mutations[name] = 1;
            }

            if (StringComparer.Ordinal.Equals(killWindow, name) && _killed.Add(name))
            {
#pragma warning disable CA2201 // Simulates immediate process termination after an irreversible effect.
                throw new AccessViolationException();
#pragma warning restore CA2201
            }
        }

        public Task<GitHubPublishResult> VerifyAsync(
            GitHubPublishRequest request,
            CancellationToken cancellationToken)
        {
            VerifyCalls++;
            if (failVerification)
            {
                throw new InvalidOperationException("Simulated remote publication drift.");
            }

            return Task.FromResult(GitHubPublishResult.Create(
                request.Repository,
                request.Repository.HttpsWebUrl,
                request.InitialCommitId,
                request.Branches,
                request.BranchPolicy,
                request.IsPrivate,
                request.OwnershipNonce).Value);
        }
    }

    private sealed class NoopGitHubProgress : IGitHubPublicationProgress
    {
        public Task RemoteCreatedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ReceiptStore(string? killWindow) : IPublicationReceiptStore
    {
        private bool _killed;
        public int WriteCalls { get; private set; }
        public List<PublicationReceiptAccessMode> Modes { get; } = [];
        public int WriteEffects { get; private set; }

        public Task<ExecutionOperationResult<PublicationReceiptWriteResult>> WriteOrVerifyAsync(
            PublicationReceiptWriteRequest request,
            CancellationToken cancellationToken)
        {
            WriteCalls++;
            Modes.Add(request.AccessMode);
            if (request.AccessMode == PublicationReceiptAccessMode.WriteOrVerify
                && WriteEffects == 0)
            {
                WriteEffects++;
                if (StringComparer.Ordinal.Equals(killWindow, "receipt-write") && !_killed)
                {
                    _killed = true;
#pragma warning disable CA2201 // Simulates termination after atomic write but before checkpoint save.
                    throw new AccessViolationException();
#pragma warning restore CA2201
                }
            }

            return Task.FromResult(ExecutionOperationResult.Success(
                new PublicationReceiptWriteResult(request.Path, request.BodyDigest, false)));
        }
    }

    private sealed class NonceGenerator : IPublicationNonceGenerator
    {
        public string CreateOwnershipNonce() => Fixture.Nonce;
    }

    private sealed class Workspace(string root) : IWorkspaceFileSystem
    {
        public WorkspaceRoot Root { get; } = WorkspaceRoot.Create(root).Value;
        public Task<bool> FileExistsAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DirectoryExistsAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CreateDirectoryAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Stream> OpenWriteAsync(WorkspaceRelativePath path, bool overwrite, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteFileAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateAllFilesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateRootDirectoriesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateFilesAsync(WorkspaceRelativePath directory, bool recursive, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateDirectoriesAsync(WorkspaceRelativePath directory, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteDirectoryAsync(WorkspaceRelativePath path, DirectoryCleanupIntent intent, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MoveDirectoryAsync(WorkspaceRelativePath source, WorkspaceRelativePath destination, WorkspaceMoveIntent intent, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private static DevForgeError Error(string code) => DevForgeError.Create(
        code,
        "Publication failed safely.",
        RedactedText.FromTrustedRedaction("Publication failed safely.").Value,
        "publication",
        null,
        false,
        [],
        []).Value;

}
