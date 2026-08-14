using System.Reflection;
using DevForge.Application.Contracts;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Blueprints.Abstractions.Validation;
using DevForge.Domain.Execution;
using DevForge.Domain.Projects;
using DevForge.Domain.Runs;

namespace DevForge.UnitTests.Application;

public sealed class PublicationContractTests
{
    [Fact]
    public void PublicationStatesHaveStableNonzeroValues()
    {
        Assert.Equal(
            ["NotRequested", "IntentPersisted", "RepositoryInitialized", "Committed", "Succeeded", "Failed"],
            Enum.GetNames<GitPublicationState>());
        Assert.Equal([1, 2, 3, 4, 5, 6], Enum.GetValues<GitPublicationState>().Select(value => (int)value));
        Assert.Equal(
            ["NotRequested", "IntentPersisted", "RemoteCreated", "Succeeded", "Failed"],
            Enum.GetNames<GitHubPublicationState>());
        Assert.Equal([1, 2, 3, 4, 5], Enum.GetValues<GitHubPublicationState>().Select(value => (int)value));
        Assert.Equal(
            ["NotRequested", "IntentPersisted", "Succeeded", "Failed"],
            Enum.GetNames<PublicationReceiptState>());
        Assert.Equal([1, 2, 3, 4], Enum.GetValues<PublicationReceiptState>().Select(value => (int)value));
    }

    [Fact]
    public void GitHubIdentityRequiresFixedHostAndStrictPersonalRepositoryNames()
    {
        var valid = GitHubRepositoryIdentity.Create("octocat", "devforge.sample");
        var invalid = GitHubRepositoryIdentity.Create("bad--account", "../repo");

        Assert.True(valid.IsValid);
        Assert.Equal("github.com", valid.Value.Host);
        Assert.Equal("https://github.com/octocat/devforge.sample.git", valid.Value.HttpsRemoteUrl);
        Assert.False(invalid.IsValid);
        Assert.Contains(invalid.Issues, issue => issue.Code == "github.account.invalid");
        Assert.Contains(invalid.Issues, issue => issue.Code == "github.repository-name.invalid");
    }

    [Fact]
    public void AuthenticationResultRejectsMissingIdentityAndUndefinedState()
    {
        var invalid = GitHubAuthenticationResult.Create(null, (GitHubAuthenticationState)999);
        var identity = GitHubRepositoryIdentity.Create("octocat", "devforge").Value;

        Assert.False(invalid.IsValid);
        Assert.Equal(2, invalid.Issues.Length);
        Assert.True(
            GitHubAuthenticationResult.Create(
                identity,
                GitHubAuthenticationState.Authenticated).IsValid);
    }

    [Fact]
    public void PublicationSnapshotAggregatesInvalidEvidenceAndSnapshotsBranches()
    {
        var identity = GitHubRepositoryIdentity.Create("octocat", "devforge").Value;
        var branches = new[] { "main", "develop" };
        var invalid = PublicationSnapshot.Create(
            (GitPublicationState)999,
            (GitHubPublicationState)999,
            (PublicationReceiptState)999,
            "not-a-digest",
            "not-a-commit",
            branches,
            identity,
            isPrivate: true,
            ownershipNonce: "weak",
            repositoryUrl: "ssh://github.com/octocat/devforge",
            receiptPath: null,
            receiptBodyDigest: "bad");

        Assert.False(invalid.IsValid);
        Assert.True(invalid.Issues.Length >= 7);

        var valid = PublicationSnapshot.Create(
            GitPublicationState.Succeeded,
            GitHubPublicationState.Succeeded,
            PublicationReceiptState.Succeeded,
            Digest('a'),
            new string('b', 40),
            branches,
            identity,
            isPrivate: true,
            ownershipNonce: new string('c', 32),
            repositoryUrl: identity.HttpsWebUrl,
            receiptPath: Path("reports\\run-1.publication.json"),
            receiptBodyDigest: Digest('d'));
        branches[0] = "tampered";

        Assert.True(valid.IsValid);
        Assert.Equal(["main", "develop"], valid.Value.Branches.ToArray());
    }

    [Fact]
    public void NotRequestedSnapshotCarriesNoPublicationSideEffectEvidence()
    {
        var snapshot = PublicationSnapshot.CreateNotRequested(Digest('a')).Value;

        Assert.Equal(GitPublicationState.NotRequested, snapshot.GitState);
        Assert.Equal(GitHubPublicationState.NotRequested, snapshot.GitHubState);
        Assert.Equal(PublicationReceiptState.NotRequested, snapshot.ReceiptState);
        Assert.Null(snapshot.InitialCommitId);
        Assert.Null(snapshot.RepositoryIdentity);
        Assert.Empty(snapshot.Branches);
    }

    [Fact]
    public void PublicationSnapshotRejectsOutOfPhaseEvidence()
    {
        var identity = GitHubRepositoryIdentity.Create("octocat", "devforge").Value;
        var notRequestedWithCommit = PublicationSnapshot.Create(
            GitPublicationState.NotRequested,
            GitHubPublicationState.NotRequested,
            PublicationReceiptState.NotRequested,
            Digest('a'),
            new string('b', 40),
            ["main"],
            null,
            true,
            null,
            null,
            null,
            null);
        var githubBeforeGit = PublicationSnapshot.Create(
            GitPublicationState.RepositoryInitialized,
            GitHubPublicationState.IntentPersisted,
            PublicationReceiptState.NotRequested,
            Digest('a'),
            null,
            [],
            identity,
            true,
            new string('c', 32),
            null,
            null,
            null);
        var receiptBeforeCompletion = PublicationSnapshot.Create(
            GitPublicationState.Committed,
            GitHubPublicationState.NotRequested,
            PublicationReceiptState.IntentPersisted,
            Digest('a'),
            new string('b', 40),
            ["main"],
            null,
            true,
            null,
            null,
            Path("reports\\run-1.publication.json"),
            Digest('d'));

        Assert.Contains(notRequestedWithCommit.Issues, issue => issue.Code == "publication.git-evidence.out-of-phase");
        Assert.Contains(githubBeforeGit.Issues, issue => issue.Code == "publication.github.before-git");
        Assert.Contains(receiptBeforeCompletion.Issues, issue => issue.Code == "publication.receipt.before-completion");
    }

    [Fact]
    public void FailedGitEvidenceKeepsCommitAndBranchesPaired()
    {
        var branchesWithoutCommit = PublicationSnapshot.Create(
            GitPublicationState.Failed,
            GitHubPublicationState.NotRequested,
            PublicationReceiptState.NotRequested,
            Digest('a'),
            null,
            ["main"],
            null,
            true,
            null,
            null,
            null,
            null);
        var commitWithoutBranches = PublicationSnapshot.Create(
            GitPublicationState.Failed,
            GitHubPublicationState.NotRequested,
            PublicationReceiptState.NotRequested,
            Digest('a'),
            new string('b', 40),
            [],
            null,
            true,
            null,
            null,
            null,
            null);

        Assert.Contains(branchesWithoutCommit.Issues, issue => issue.Code == "publication.git-evidence.unpaired");
        Assert.Contains(commitWithoutBranches.Issues, issue => issue.Code == "publication.git-evidence.unpaired");
    }

    [Fact]
    public void CompletedCheckpointRequiresPublicationMatchingReviewedGitIntent()
    {
        var fixture = PublicationCheckpointFixture.Create(publishToGitHub: true);
        var notRequested = PublicationSnapshot.CreateNotRequested(Digest('a')).Value;
        var completedRun = fixture.Run.TransitionTo(RunStatus.PublishPending).Value
            .TransitionTo(RunStatus.Completed).Value;
        var complete = fixture.CreateCheckpoint(completedRun, fixture.Publication);

        var missing = fixture.CreateCheckpoint(completedRun, notRequested);
        Assert.False(missing.IsValid);
        Assert.Contains(missing.Issues, issue => issue.Code == "checkpoint.publication.incomplete");
        Assert.Contains(
            fixture.CreateCheckpointWithoutPreview(completedRun, fixture.Publication).Issues,
            issue => issue.Code == "checkpoint.publication.preview-required");
        Assert.True(complete.IsValid);
    }

    [Fact]
    public void LocalReadyCannotCarryPublicationSideEffects()
    {
        var fixture = PublicationCheckpointFixture.Create(publishToGitHub: true);

        var result = fixture.CreateCheckpoint(fixture.Run, fixture.Publication);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "checkpoint.local-ready.publication-started");
    }

    [Fact]
    public void PublicationContractsExposeNoAuthenticationMaterialOrArbitraryCommandSurface()
    {
        var graph = new Queue<Type>(
        [
            typeof(IGitService),
            typeof(IGitHubService),
            typeof(PublicationSnapshot),
        ]);
        var seen = new HashSet<Type>();
        string[] forbidden = ["password", "token", "credential", "commandline", "shell"];

        while (graph.TryDequeue(out var type))
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (!seen.Add(type))
            {
                continue;
            }

            foreach (var argument in type.GetGenericArguments())
            {
                graph.Enqueue(argument);
            }

            if (type.Assembly != typeof(IGitService).Assembly)
            {
                continue;
            }

            Assert.DoesNotContain(forbidden, item => Normalize(type.Name).Contains(item, StringComparison.Ordinal));
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.DoesNotContain(forbidden, item => Normalize(property.Name).Contains(item, StringComparison.Ordinal));
                graph.Enqueue(property.PropertyType);
            }
        }

        Assert.Equal(
            ["BootstrapAsync", "VerifyAsync"],
            typeof(IGitService).GetMethods().Select(method => method.Name).Order(StringComparer.Ordinal));
        Assert.Equal(
            ["CheckAuthenticationAsync", "PublishAsync"],
            typeof(IGitHubService).GetMethods().Select(method => method.Name).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(
            typeof(IGitService).Assembly.GetExportedTypes(),
            type => type.Name is "GitCommitRequest"
                || type.Name.StartsWith("Git", StringComparison.Ordinal)
                    && type.GetProperty("Message") is not null);
    }

    [Fact]
    public void CheckpointBindsExactBranchPolicyAndPublicationLifecycle()
    {
        var fixture = PublicationCheckpointFixture.Create(publishToGitHub: true);
        var identity = GitHubRepositoryIdentity.Create("octocat", "devforge").Value;
        var wrongBranches = PublicationSnapshot.Create(
            GitPublicationState.Succeeded,
            GitHubPublicationState.Succeeded,
            PublicationReceiptState.Succeeded,
            Digest('a'),
            new string('b', 40),
            ["main", "develop"],
            identity,
            true,
            new string('c', 32),
            identity.HttpsWebUrl,
            Path("reports\\run-1.publication.json"),
            Digest('d')).Value;
        var completed = fixture.Run.TransitionTo(RunStatus.PublishPending).Value
            .TransitionTo(RunStatus.Completed).Value;

        Assert.Contains(
            fixture.CreateCheckpoint(completed, wrongBranches).Issues,
            issue => issue.Code == "checkpoint.publication.branch-policy-mismatch");

        var committedWrongBranches = PublicationSnapshot.Create(
            GitPublicationState.Committed,
            GitHubPublicationState.NotRequested,
            PublicationReceiptState.NotRequested,
            Digest('a'),
            new string('b', 40),
            ["main", "develop"],
            null,
            true,
            null,
            null,
            null,
            null).Value;
        var pending = fixture.Run.TransitionTo(RunStatus.PublishPending).Value;
        Assert.Contains(
            fixture.CreateCheckpoint(pending, committedWrongBranches).Issues,
            issue => issue.Code == "checkpoint.publication.branch-policy-mismatch");

        var receiptBeforeReviewedGitHub = PublicationSnapshot.Create(
            GitPublicationState.Succeeded,
            GitHubPublicationState.NotRequested,
            PublicationReceiptState.IntentPersisted,
            Digest('a'),
            new string('b', 40),
            ["main"],
            null,
            true,
            null,
            null,
            Path("reports\\run-1.publication.json"),
            Digest('d')).Value;
        Assert.Contains(
            fixture.CreateCheckpoint(pending, receiptBeforeReviewedGitHub).Issues,
            issue => issue.Code == "checkpoint.publication.receipt-before-github");

        foreach (var status in new[]
                 {
                     RunStatus.Draft,
                     RunStatus.Planning,
                     RunStatus.Executing,
                     RunStatus.Cancelled,
                     RunStatus.Failed,
                 })
        {
            var run = ProjectRun.Rehydrate("run-1", "recipe-1", status, null, [], []).Value;
            Assert.Contains(
                fixture.CreateCheckpoint(run, fixture.Publication).Issues,
                issue => issue.Code == "checkpoint.publication.status-invalid");
        }
    }

    [Fact]
    public void BranchInputsAreBoundedBeforeMaterialization()
    {
        var branches = new BoundedProbeEnumerable();

        var result = PublicationSnapshot.Create(
            GitPublicationState.Succeeded,
            GitHubPublicationState.NotRequested,
            PublicationReceiptState.NotRequested,
            Digest('a'),
            new string('b', 40),
            branches,
            null,
            true,
            null,
            null,
            null,
            null);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "publication.branches.too-many");
        Assert.Equal(3, branches.MoveNextCount);
    }

    [Fact]
    public void GitHubResultAttestsVisibilityOwnershipAndBranchPolicy()
    {
        var identity = GitHubRepositoryIdentity.Create("octocat", "devforge").Value;
        var result = GitHubPublishResult.Create(
            identity,
            identity.HttpsWebUrl,
            new string('b', 40),
            ["main"],
            GitBranchPolicy.Main,
            isPrivate: true,
            ownershipNonce: new string('c', 32));

        Assert.True(result.IsValid);
        Assert.Equal(GitBranchPolicy.Main, result.Value.BranchPolicy);
        Assert.True(result.Value.IsPrivate);
        Assert.Equal(new string('c', 32), result.Value.OwnershipNonce);
    }

    private static WorkspaceRelativePath Path(string value) => WorkspaceRelativePath.Create(value).Value;

    private static string Digest(char value) => $"sha256:{new string(value, 64)}";

    private static string Normalize(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private sealed class BoundedProbeEnumerable : IEnumerable<string?>
    {
        public int MoveNextCount { get; private set; }

        public IEnumerator<string?> GetEnumerator()
        {
            while (true)
            {
                MoveNextCount++;
                if (MoveNextCount > 3)
                {
                    throw new InvalidOperationException("The branch boundary enumerated beyond its cap.");
                }

                yield return "main";
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class PublicationCheckpointFixture
    {
        private PublicationCheckpointFixture(
            ProjectRun run,
            ExecutionPlan plan,
            PlanPreview preview,
            BlueprintReference blueprint,
            BlueprintFingerprint fingerprint,
            StagingDescriptor staging,
            TargetDescriptor target,
            RunArtifactDescriptor artifacts,
            PublicationSnapshot publication)
        {
            Run = run;
            Plan = plan;
            Preview = preview;
            Blueprint = blueprint;
            Fingerprint = fingerprint;
            Staging = staging;
            Target = target;
            Artifacts = artifacts;
            Publication = publication;
        }

        public ProjectRun Run { get; }

        private ExecutionPlan Plan { get; }

        private PlanPreview Preview { get; }

        private BlueprintReference Blueprint { get; }

        private BlueprintFingerprint Fingerprint { get; }

        private StagingDescriptor Staging { get; }

        private TargetDescriptor Target { get; }

        private RunArtifactDescriptor Artifacts { get; }

        public PublicationSnapshot Publication { get; }

        public static PublicationCheckpointFixture Create(bool publishToGitHub)
        {
            var git = GitOptions.Create(
                publishToGitHub: publishToGitHub,
                githubAccount: publishToGitHub ? "octocat" : null,
                githubRepository: publishToGitHub ? "devforge" : null).Value;
            var step = ExecutionStep.Create(
                "create",
                "Create",
                "create-directory",
                [],
                TimeSpan.FromSeconds(30),
                RetryPolicy.None).Value;
            var plan = ExecutionPlan.Create(Digest('1'), [step], []).Value;
            var blueprint = BlueprintReference.Create("desktop.csharp-wpf-tool", "1.0.0").Value;
            var preview = PlanPreview.Create(
                blueprint,
                [new PlanPreviewStep("create", "create-directory", TimeSpan.FromSeconds(30))],
                [], [], [], [], [], [], [], [],
                git,
                CompletionOptions.Create().Value,
                plan.Id).Value;
            var fingerprint = BlueprintFingerprint.Create(
                "built-in",
                Path("desktop.csharp-wpf-tool\\1.0.0"),
                BlueprintTrust.BuiltIn,
                Digest('2')).Value;
            var run = ProjectRun.Create("run-1", "recipe-1").Value
                .TransitionTo(RunStatus.Planning).Value
                .TransitionTo(RunStatus.Executing).Value
                .TransitionTo(RunStatus.LocalReady).Value;
            var identity = GitHubRepositoryIdentity.Create("octocat", "devforge").Value;
            var publication = PublicationSnapshot.Create(
                GitPublicationState.Succeeded,
                publishToGitHub ? GitHubPublicationState.Succeeded : GitHubPublicationState.NotRequested,
                PublicationReceiptState.Succeeded,
                Digest('a'),
                new string('b', 40),
                ["main"],
                publishToGitHub ? identity : null,
                isPrivate: true,
                ownershipNonce: publishToGitHub ? new string('c', 32) : null,
                repositoryUrl: publishToGitHub ? identity.HttpsWebUrl : null,
                receiptPath: Path("reports\\run-1.publication.json"),
                receiptBodyDigest: Digest('d')).Value;
            return new PublicationCheckpointFixture(
                run,
                plan,
                preview,
                blueprint,
                fingerprint,
                StagingDescriptor.Create(
                    Path(".devforge-staging\\run-1"),
                    Path(".devforge-staging\\run-1\\payload"),
                    Path(".devforge-staging\\run-1\\ownership.json"),
                    "marker-1").Value,
                TargetDescriptor.Create(
                    WorkspaceRoot.Create("C:\\target-parent").Value,
                    Path("project"),
                    null).Value,
                RunArtifactDescriptor.Create(WorkspaceRoot.Create("C:\\artifacts").Value).Value,
                publication);
        }

        public DevForge.Domain.Validation.ValidationResult<RunCheckpoint> CreateCheckpoint(
            ProjectRun run,
            PublicationSnapshot publication) => RunCheckpoint.Create(
                run,
                Plan,
                Preview,
                Blueprint,
                Fingerprint,
                Staging,
                Target,
                Artifacts,
                [],
                FinalizationState.Succeeded,
                ReportPersistenceState.Succeeded,
                publication);

        public DevForge.Domain.Validation.ValidationResult<RunCheckpoint> CreateCheckpointWithoutPreview(
            ProjectRun run,
            PublicationSnapshot publication) => RunCheckpoint.Create(
                run,
                Plan,
                null,
                Blueprint,
                Fingerprint,
                Staging,
                Target,
                Artifacts,
                [],
                FinalizationState.Succeeded,
                ReportPersistenceState.Succeeded,
                publication);
    }
}
