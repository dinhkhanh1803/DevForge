using System.IO;
using DevForge.Application.Contracts;
using DevForge.Application.Publication;
using DevForge.Domain.Projects;
using DevForge.Domain.Runs;
using DevForge.E2ETests.M7;
using DevForge.Infrastructure.Creation;
using DevForge.Infrastructure.FileSystem;
using DevForge.Infrastructure.Git;
using DevForge.Infrastructure.Processes;
using DevForge.Infrastructure.Publication;
using DevForge.Infrastructure.Security;

namespace DevForge.E2ETests.M8;

internal sealed class M8PublicationFixture : IAsyncDisposable
{
    private readonly M7BlueprintFixture _generation;
    private readonly ProjectPublicationCoordinator _publication;

    private M8PublicationFixture(
        M7BlueprintFixture generation,
        ProjectPublicationCoordinator publication,
        DeterministicGitHubService gitHub)
    {
        _generation = generation;
        _publication = publication;
        GitHub = gitHub;
        LocalGitDraft = generation.ValidDraft;
        PrivateGitHubDraft = CreatePrivateGitHubDraft(generation.ValidDraft);
    }

    public ProjectCreationDraft LocalGitDraft { get; }

    public ProjectCreationDraft PrivateGitHubDraft { get; }

    public DeterministicGitHubService GitHub { get; }

    public string TargetPath => _generation.TargetPath;

    public static async Task<M8PublicationFixture> CreateAsync()
    {
        var generation = await M7BlueprintFixture.CreateAsync();
        try
        {
            var fileSystem = new WindowsFileSystem();
            var localDataRoot = WorkspaceRoot.Create(generation.LocalDataRoot).Value;
            var targets = new WindowsProjectTargetService(fileSystem, localDataRoot);
            var git = new LocalGitService(new WindowsProcessRunner(), new WorkspaceSecretScanner());
            var gitHub = new DeterministicGitHubService();
            var publication = new ProjectPublicationCoordinator(
                generation.CheckpointStore,
                new WindowsPublicationLeaseProvider(fileSystem, localDataRoot),
                new ProjectPublicationWorkspaceFactory(targets),
                git,
                gitHub,
                new AtomicPublicationReceiptStore(),
                new FixedNonceGenerator());
            return new M8PublicationFixture(generation, publication, gitHub);
        }
        catch
        {
            await generation.DisposeAsync();
            throw;
        }
    }

    public async Task<ProjectCreationPlanSnapshot> CreatePlanAsync(ProjectCreationDraft draft)
    {
        var result = await _generation.Workflow.CreatePlanAsync(draft, CancellationToken.None);
        return result.IsValid
            ? result.Value
            : throw new InvalidOperationException(
                string.Join("; ", result.Issues.Select(issue => issue.Code)));
    }

    public async Task<ProjectCreationExecutionSnapshot> ExecuteAsync(ProjectCreationPlanSnapshot plan)
    {
        var result = await _generation.Workflow.ExecuteAsync(plan, null, CancellationToken.None);
        return result.IsValid
            ? result.Value
            : throw new InvalidOperationException(
                string.Join("; ", result.Issues.Select(issue => issue.Code)));
    }

    public Task<ExecutionOperationResult<RunCheckpoint>> PublishAsync(string runId) =>
        _publication.PublishAsync(
            PublicationRequest.Create(runId, PublicationMutationMode.Normal).Value,
            CancellationToken.None);

    public async Task<RunCheckpoint> LoadCheckpointAsync(string runId) =>
        await _generation.CheckpointStore.FindAsync(runId, CancellationToken.None)
        ?? throw new InvalidOperationException("The expected durable checkpoint is missing.");

    public string PublicationReceiptPath(string runId) => Path.Combine(
        _generation.LocalDataRoot,
        "runs",
        runId,
        "reports",
        $"{runId}.publication.json");

    public ValueTask DisposeAsync() => _generation.DisposeAsync();

    private static ProjectCreationDraft CreatePrivateGitHubDraft(ProjectCreationDraft source) =>
        ProjectCreationDraft.Create(
            source.Name,
            source.RootPath,
            source.OutputFolder,
            source.Blueprint,
            source.Inputs.Select(item =>
                new KeyValuePair<string, DynamicInputValue?>(item.Key, item.Value)),
            source.Features,
            source.IdeId,
            initializeRepository: true,
            branchPolicy: GitBranchPolicy.MainAndDevelop,
            publishToGitHub: true,
            isPrivate: true,
            githubAccount: "octocat",
            githubRepository: "m8-sample").Value;

    internal sealed class DeterministicGitHubService : IPublicationGitHubService
    {
        private bool _failNextPublish;

        public int PublishCalls { get; private set; }

        public List<GitHubPublishRequest> Requests { get; } = [];

        public void FailNextPublish() => _failNextPublish = true;

        public async Task<GitHubPublishResult> PublishAsync(
            GitHubPublishRequest request,
            IGitHubPublicationProgress progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PublishCalls++;
            Requests.Add(request);
            await progress.RemoteCreatedAsync(CancellationToken.None);
            if (_failNextPublish)
            {
                _failNextPublish = false;
                throw new IOException("Deterministic simulated GitHub interruption.");
            }

            return Result(request);
        }

        public Task<GitHubPublishResult> VerifyAsync(
            GitHubPublishRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Result(request));
        }

        private static GitHubPublishResult Result(GitHubPublishRequest request) =>
            GitHubPublishResult.Create(
                request.Repository,
                request.Repository.HttpsWebUrl,
                request.InitialCommitId,
                request.Branches,
                request.BranchPolicy,
                request.IsPrivate,
                request.OwnershipNonce).Value;
    }

    private sealed class FixedNonceGenerator : IPublicationNonceGenerator
    {
        public string CreateOwnershipNonce() => new('a', 32);
    }
}
