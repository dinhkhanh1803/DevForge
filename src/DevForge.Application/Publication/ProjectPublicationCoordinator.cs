using DevForge.Application.Contracts;
using DevForge.Application.Execution;
using DevForge.Domain.Diagnostics;
using DevForge.Domain.Privacy;
using DevForge.Domain.Projects;
using DevForge.Domain.Runs;

namespace DevForge.Application.Publication;

public sealed class ProjectPublicationCoordinator(
    IRunCheckpointStore checkpointStore,
    IPublicationLeaseProvider leaseProvider,
    IProjectPublicationWorkspaceFactory workspaceFactory,
    IPublicationGitService gitService,
    IPublicationGitHubService gitHubService,
    IPublicationReceiptStore receiptStore,
    IPublicationNonceGenerator nonceGenerator) : IProjectPublicationCoordinator
{
    private readonly IRunCheckpointStore _checkpointStore = checkpointStore
        ?? throw new ArgumentNullException(nameof(checkpointStore));
    private readonly IPublicationLeaseProvider _leaseProvider = leaseProvider
        ?? throw new ArgumentNullException(nameof(leaseProvider));
    private readonly IProjectPublicationWorkspaceFactory _workspaceFactory = workspaceFactory
        ?? throw new ArgumentNullException(nameof(workspaceFactory));
    private readonly IPublicationGitService _gitService = gitService
        ?? throw new ArgumentNullException(nameof(gitService));
    private readonly IPublicationGitHubService _gitHubService = gitHubService
        ?? throw new ArgumentNullException(nameof(gitHubService));
    private readonly IPublicationReceiptStore _receiptStore = receiptStore
        ?? throw new ArgumentNullException(nameof(receiptStore));
    private readonly IPublicationNonceGenerator _nonceGenerator = nonceGenerator
        ?? throw new ArgumentNullException(nameof(nonceGenerator));

    public async Task<ExecutionOperationResult<RunCheckpoint>> PublishAsync(
        PublicationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.MutationMode == PublicationMutationMode.SafeReadOnly)
        {
            return Failure<RunCheckpoint>("DF-PUB-READONLY", "Publication is disabled in safe mode.");
        }

        if (!ExecutionActivityGate.TryEnter())
        {
            return Failure<RunCheckpoint>("DF-PUB-ACTIVE", "Another DevForge operation is active.");
        }

        try
        {
            var acquired = await _leaseProvider.AcquireAsync(request.RunId, cancellationToken)
                .ConfigureAwait(false);
            if (!acquired.IsSuccessful)
            {
                return ExecutionOperationResult.Failure<RunCheckpoint>(acquired.Error!);
            }

            await using var lease = acquired.Value;
            var checkpoint = await _checkpointStore.FindAsync(request.RunId, cancellationToken)
                .ConfigureAwait(false);
            if (checkpoint is null)
            {
                return Failure<RunCheckpoint>("DF-PUB-001", "The publication checkpoint was not found.");
            }

            var validation = ValidateCheckpoint(checkpoint);
            if (validation is not null)
            {
                return ExecutionOperationResult.Failure<RunCheckpoint>(validation);
            }

            var opened = await _workspaceFactory.OpenAsync(checkpoint, cancellationToken)
                .ConfigureAwait(false);
            if (!opened.IsSuccessful)
            {
                return ExecutionOperationResult.Failure<RunCheckpoint>(opened.Error!);
            }

            if (!WorkspaceMatches(checkpoint, opened.Value))
            {
                return Failure<RunCheckpoint>("DF-PUB-001", "The finalized publication workspace changed.");
            }

            var session = new Session(checkpoint, opened.Value);
            try
            {
                if (session.Checkpoint.Run.Status == RunStatus.Completed)
                {
                    await EnsureGitAsync(session, cancellationToken).ConfigureAwait(false);
                    await EnsureGitHubAsync(session, cancellationToken).ConfigureAwait(false);
                    return await VerifyCompletedReceiptAsync(session, cancellationToken)
                        .ConfigureAwait(false);
                }

                await EnsureGitIntentAsync(session).ConfigureAwait(false);
                await EnsureGitAsync(session, cancellationToken).ConfigureAwait(false);
                await EnsureGitHubAsync(session, cancellationToken).ConfigureAwait(false);
                await EnsureReceiptAsync(session, cancellationToken).ConfigureAwait(false);
                return ExecutionOperationResult.Success(session.Checkpoint);
            }
            catch (OperationCanceledException)
            {
                await PersistFailureAsync(session, "DF-PUB-CANCELLED").ConfigureAwait(false);
                throw;
            }
            catch (Exception exception) when (IsRecoverable(exception))
            {
                var error = await PersistFailureAsync(session, "DF-PUB-003").ConfigureAwait(false);
                return ExecutionOperationResult.Failure<RunCheckpoint>(error);
            }
        }
        finally
        {
            ExecutionActivityGate.Exit();
        }
    }

    private async Task EnsureGitIntentAsync(Session session)
    {
        if (session.Checkpoint.Run.Status == RunStatus.LocalReady)
        {
            var publication = CreatePublication(
                session.Checkpoint,
                gitState: GitPublicationState.IntentPersisted);
            var run = session.Checkpoint.Run.TransitionTo(RunStatus.PublishPending).Value;
            session.Checkpoint = Recreate(session.Checkpoint, run, publication);
            await SaveAsync(session.Checkpoint).ConfigureAwait(false);
        }
    }

    private async Task EnsureGitAsync(Session session, CancellationToken cancellationToken)
    {
        if (session.Checkpoint.Publication.GitState == GitPublicationState.Succeeded)
        {
            var publication = session.Checkpoint.Publication;
            var githubIncomplete = session.Checkpoint.Preview!.Git.PublishToGitHub
                && publication.GitHubState != GitHubPublicationState.Succeeded;
            if (!githubIncomplete)
            {
                var expectedOrigin = publication.GitHubState == GitHubPublicationState.Succeeded
                    ? publication.RepositoryIdentity!.HttpsRemoteUrl
                    : null;
                var verify = GitVerificationRequest.Create(
                    session.Workspaces.FinalProject,
                    session.Checkpoint.Preview.Git.BranchPolicy,
                    publication.FinalTreeDigest,
                    publication.InitialCommitId,
                    expectedOrigin).Value;
                await _gitService.VerifyAsync(verify, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        var options = session.Checkpoint.Preview!.Git;
        var request = GitBootstrapRequest.Create(
            session.Workspaces.FinalProject,
            options.BranchPolicy,
            session.Checkpoint.Publication.FinalTreeDigest).Value;
        var progress = new GitProgress(this, session);
        var receipt = await _gitService.BootstrapAsync(request, progress, cancellationToken)
            .ConfigureAwait(false);
        var committed = CreatePublication(
            session.Checkpoint,
            gitState: GitPublicationState.Committed,
            initialCommitId: receipt.InitialCommitId,
            branches: receipt.Branches);
        session.Checkpoint = Recreate(session.Checkpoint, session.Checkpoint.Run, committed);
        await SaveAsync(session.Checkpoint).ConfigureAwait(false);

        var succeeded = CreatePublication(
            session.Checkpoint,
            gitState: GitPublicationState.Succeeded,
            initialCommitId: receipt.InitialCommitId,
            branches: receipt.Branches);
        session.Checkpoint = Recreate(session.Checkpoint, session.Checkpoint.Run, succeeded);
        await SaveAsync(session.Checkpoint).ConfigureAwait(false);
    }

    private async Task EnsureGitHubAsync(Session session, CancellationToken cancellationToken)
    {
        var options = session.Checkpoint.Preview!.Git;
        if (!options.PublishToGitHub)
        {
            return;
        }

        var current = session.Checkpoint.Publication;
        if (current.GitHubState == GitHubPublicationState.NotRequested)
        {
            var identity = GitHubRepositoryIdentity.Create(
                options.GitHubAccount,
                options.GitHubRepository).Value;
            var nonce = _nonceGenerator.CreateOwnershipNonce();
            var intent = CreatePublication(
                session.Checkpoint,
                githubState: GitHubPublicationState.IntentPersisted,
                repositoryIdentity: identity,
                ownershipNonce: nonce);
            session.Checkpoint = Recreate(session.Checkpoint, session.Checkpoint.Run, intent);
            await SaveAsync(session.Checkpoint).ConfigureAwait(false);
            current = intent;
        }

        var request = GitHubPublishRequest.Create(
            session.Workspaces.FinalProject,
            current.RepositoryIdentity,
            options.BranchPolicy,
            current.InitialCommitId,
            current.Branches,
            current.IsPrivate,
            current.OwnershipNonce,
            current.FinalTreeDigest).Value;
        if (current.GitHubState == GitHubPublicationState.Succeeded)
        {
            await _gitHubService.VerifyAsync(request, cancellationToken).ConfigureAwait(false);
            return;
        }

        var result = await _gitHubService.PublishAsync(
                request,
                new GitHubProgress(this, session),
                cancellationToken)
            .ConfigureAwait(false);
        var succeeded = CreatePublication(
            session.Checkpoint,
            githubState: GitHubPublicationState.Succeeded,
            repositoryIdentity: result.Repository,
            ownershipNonce: result.OwnershipNonce,
            repositoryUrl: result.RepositoryUrl);
        session.Checkpoint = Recreate(session.Checkpoint, session.Checkpoint.Run, succeeded);
        await SaveAsync(session.Checkpoint).ConfigureAwait(false);
    }

    private async Task EnsureReceiptAsync(Session session, CancellationToken cancellationToken)
    {
        var publication = session.Checkpoint.Publication;
        var path = publication.ReceiptPath ?? Relative($"reports\\{session.Checkpoint.Run.Id}.publication.json");
        var serialized = PublicationReceiptSerializer.Serialize(session.Checkpoint, publication);
        if (publication.ReceiptState == PublicationReceiptState.NotRequested)
        {
            publication = CreatePublication(
                session.Checkpoint,
                receiptState: PublicationReceiptState.IntentPersisted,
                receiptPath: path,
                receiptBodyDigest: serialized.Digest);
            session.Checkpoint = Recreate(session.Checkpoint, session.Checkpoint.Run, publication);
            await SaveAsync(session.Checkpoint).ConfigureAwait(false);
        }
        else if (!StringComparer.Ordinal.Equals(publication.ReceiptBodyDigest, serialized.Digest)
                 || !publication.ReceiptPath!.Equals(path))
        {
            throw new InvalidOperationException("Persisted publication receipt intent changed.");
        }

        var write = PublicationReceiptWriteRequest.Create(
            session.Workspaces.RunArtifacts,
            path,
            serialized.Body,
            serialized.Digest).Value;
        var written = await _receiptStore.WriteOrVerifyAsync(write, cancellationToken)
            .ConfigureAwait(false);
        if (!written.IsSuccessful)
        {
            throw new PublicationBoundaryException(written.Error!);
        }

        var completedPublication = CreatePublication(
            session.Checkpoint,
            receiptState: PublicationReceiptState.Succeeded,
            receiptPath: path,
            receiptBodyDigest: serialized.Digest);
        var completedRun = session.Checkpoint.Run.TransitionTo(RunStatus.Completed).Value;
        session.Checkpoint = Recreate(session.Checkpoint, completedRun, completedPublication);
        await SaveAsync(session.Checkpoint).ConfigureAwait(false);
    }

    private async Task<ExecutionOperationResult<RunCheckpoint>> VerifyCompletedReceiptAsync(
        Session session,
        CancellationToken cancellationToken)
    {
        var publication = session.Checkpoint.Publication;
        var serialized = PublicationReceiptSerializer.Serialize(session.Checkpoint, publication);
        if (publication.ReceiptPath is null
            || !StringComparer.Ordinal.Equals(publication.ReceiptBodyDigest, serialized.Digest))
        {
            return Failure<RunCheckpoint>("DF-PUB-RECEIPT", "Publication receipt evidence is invalid.");
        }

        var request = PublicationReceiptWriteRequest.Create(
            session.Workspaces.RunArtifacts,
            publication.ReceiptPath,
            serialized.Body,
            serialized.Digest,
            PublicationReceiptAccessMode.VerifyOnly).Value;
        var verified = await _receiptStore.WriteOrVerifyAsync(request, cancellationToken)
            .ConfigureAwait(false);
        return verified.IsSuccessful
            ? ExecutionOperationResult.Success(session.Checkpoint)
            : ExecutionOperationResult.Failure<RunCheckpoint>(verified.Error!);
    }

    private async Task<DevForgeError> PersistFailureAsync(Session session, string code)
    {
        if (session.Checkpoint.Run.Status != RunStatus.PublishPending)
        {
            return Error(code, "Publication stopped before durable intent.");
        }

        var current = session.Checkpoint.Publication;
        var failed = current.ReceiptState != PublicationReceiptState.NotRequested
            ? CreatePublication(session.Checkpoint, receiptState: PublicationReceiptState.Failed)
            : current.GitHubState is not (GitHubPublicationState.NotRequested
                or GitHubPublicationState.Succeeded)
                ? CreatePublication(session.Checkpoint, githubState: GitHubPublicationState.Failed)
                : current.GitState != GitPublicationState.Succeeded
                    ? CreatePublication(session.Checkpoint, gitState: GitPublicationState.Failed)
                    : current;
        var error = Error(code, "Publication remains recoverable from its durable checkpoint.");
        var run = session.Checkpoint.Run.AppendError(error).Value;
        session.Checkpoint = Recreate(session.Checkpoint, run, failed);
        await SaveAsync(session.Checkpoint).ConfigureAwait(false);
        return error;
    }

    private Task SaveAsync(RunCheckpoint checkpoint) =>
        _checkpointStore.SaveAsync(checkpoint, CancellationToken.None);

    private static DevForgeError? ValidateCheckpoint(RunCheckpoint checkpoint)
    {
        var expectedReceiptPath = Relative($"reports\\{checkpoint.Run.Id}.publication.json");
        if (checkpoint.Run.Status is not (RunStatus.LocalReady
                or RunStatus.PublishPending
                or RunStatus.Completed)
            || checkpoint.Preview is null
            || !checkpoint.Preview.Git.InitializeRepository
            || checkpoint.FinalizationState != FinalizationState.Succeeded
            || checkpoint.ReportState != ReportPersistenceState.Succeeded
            || !ExecutionContractValidation.IsCanonicalDigest(
                checkpoint.Publication.FinalTreeDigest)
            || checkpoint.Publication.ReceiptState != PublicationReceiptState.NotRequested
                && !expectedReceiptPath.Equals(checkpoint.Publication.ReceiptPath))
        {
            return Error("DF-PUB-001", "The checkpoint is not eligible for publication.");
        }

        return null;
    }

    private static bool WorkspaceMatches(
        RunCheckpoint checkpoint,
        ProjectPublicationWorkspaces workspaces)
    {
        var targetRoot = WorkspaceRoot.Create(Path.Combine(
            checkpoint.Target.ParentRoot.RevealForFileSystem(),
            checkpoint.Target.TargetDirectory.RevealForFileSystem()));
        return targetRoot.IsValid
            && workspaces.FinalProject.Root.Equals(targetRoot.Value)
            && workspaces.RunArtifacts.Root.Equals(checkpoint.RunArtifacts.Root);
    }

    private static PublicationSnapshot CreatePublication(
        RunCheckpoint checkpoint,
        GitPublicationState? gitState = null,
        GitHubPublicationState? githubState = null,
        PublicationReceiptState? receiptState = null,
        string? initialCommitId = null,
        IEnumerable<string>? branches = null,
        GitHubRepositoryIdentity? repositoryIdentity = null,
        string? ownershipNonce = null,
        string? repositoryUrl = null,
        WorkspaceRelativePath? receiptPath = null,
        string? receiptBodyDigest = null)
    {
        var current = checkpoint.Publication;
        return PublicationSnapshot.Create(
            gitState ?? current.GitState,
            githubState ?? current.GitHubState,
            receiptState ?? current.ReceiptState,
            current.FinalTreeDigest,
            initialCommitId ?? current.InitialCommitId,
            branches ?? current.Branches,
            repositoryIdentity ?? current.RepositoryIdentity,
            current.IsPrivate,
            ownershipNonce ?? current.OwnershipNonce,
            repositoryUrl ?? current.RepositoryUrl,
            receiptPath ?? current.ReceiptPath,
            receiptBodyDigest ?? current.ReceiptBodyDigest).Value;
    }

    private static RunCheckpoint Recreate(
        RunCheckpoint checkpoint,
        ProjectRun run,
        PublicationSnapshot publication) => RunCheckpoint.Create(
            run,
            checkpoint.Plan,
            checkpoint.Preview,
            checkpoint.Blueprint,
            checkpoint.BlueprintFingerprint,
            checkpoint.Staging,
            checkpoint.Target,
            checkpoint.RunArtifacts,
            checkpoint.Evidence,
            checkpoint.FinalizationState,
            checkpoint.ReportState,
            publication).Value;

    private static WorkspaceRelativePath Relative(string value) =>
        WorkspaceRelativePath.Create(value).Value;

    private static ExecutionOperationResult<T> Failure<T>(string code, string summary)
        where T : class => ExecutionOperationResult.Failure<T>(Error(code, summary));

    private static DevForgeError Error(string code, string summary) => DevForgeError.Create(
        code,
        summary,
        RedactedText.FromTrustedRedaction(summary).Value,
        "publication",
        null,
        true,
        [],
        []).Value;

    private static bool IsRecoverable(Exception exception) => exception is not OutOfMemoryException
        and not StackOverflowException
        and not AccessViolationException;

    private sealed class Session(
        RunCheckpoint checkpoint,
        ProjectPublicationWorkspaces workspaces)
    {
        public RunCheckpoint Checkpoint { get; set; } = checkpoint;
        public ProjectPublicationWorkspaces Workspaces { get; } = workspaces;
    }

    private sealed class GitProgress(ProjectPublicationCoordinator owner, Session session)
        : IGitPublicationProgress
    {
        public async Task RepositoryInitializedAsync(CancellationToken cancellationToken)
        {
            if (session.Checkpoint.Publication.InitialCommitId is not null)
            {
                return;
            }

            var publication = CreatePublication(
                session.Checkpoint,
                gitState: GitPublicationState.RepositoryInitialized);
            session.Checkpoint = Recreate(session.Checkpoint, session.Checkpoint.Run, publication);
            await owner.SaveAsync(session.Checkpoint).ConfigureAwait(false);
        }
    }

    private sealed class GitHubProgress(ProjectPublicationCoordinator owner, Session session)
        : IGitHubPublicationProgress
    {
        public async Task RemoteCreatedAsync(CancellationToken cancellationToken)
        {
            var publication = CreatePublication(
                session.Checkpoint,
                githubState: GitHubPublicationState.RemoteCreated);
            session.Checkpoint = Recreate(session.Checkpoint, session.Checkpoint.Run, publication);
            await owner.SaveAsync(session.Checkpoint).ConfigureAwait(false);
        }
    }

    private sealed class PublicationBoundaryException(DevForgeError error) : Exception
    {
        public DevForgeError Error { get; } = error;
    }
}
