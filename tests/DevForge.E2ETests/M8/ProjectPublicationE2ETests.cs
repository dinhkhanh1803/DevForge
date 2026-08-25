using System.IO;
using DevForge.Application.Contracts;
using DevForge.Domain.Runs;

namespace DevForge.E2ETests.M8;

[Collection(M8PublicationTestGroup.Name)]
public sealed class ProjectPublicationE2ETests
{
    [Fact]
    public async Task GeneratedValidatedProjectCompletesWithExactCleanLocalGitAndDurableReceipt()
    {
        await using var fixture = await M8PublicationFixture.CreateAsync();
        var plan = await fixture.CreatePlanAsync(fixture.LocalGitDraft);
        var generated = await fixture.ExecuteAsync(plan);
        var generationEvidence = generated.Checkpoint.Evidence;

        Assert.Equal(RunStatus.LocalReady, generated.Checkpoint.Run.Status);
        Assert.False(Directory.Exists(Path.Combine(fixture.TargetPath, ".git")));

        var publication = await fixture.PublishAsync(plan.RunId);

        Assert.True(publication.IsSuccessful);
        Assert.Equal(RunStatus.Completed, publication.Value.Run.Status);
        Assert.Equal(GitPublicationState.Succeeded, publication.Value.Publication.GitState);
        Assert.Equal(GitHubPublicationState.NotRequested, publication.Value.Publication.GitHubState);
        Assert.Equal(PublicationReceiptState.Succeeded, publication.Value.Publication.ReceiptState);
        Assert.Equal(["main"], publication.Value.Publication.Branches.ToArray());
        Assert.Equal(generationEvidence.ToArray(), publication.Value.Evidence.ToArray());
        Assert.True(Directory.Exists(Path.Combine(fixture.TargetPath, ".git")));
        Assert.True(File.Exists(fixture.PublicationReceiptPath(plan.RunId)));

        var verified = await fixture.PublishAsync(plan.RunId);
        Assert.True(verified.IsSuccessful);
        Assert.Equal(publication.Value.Publication.InitialCommitId, verified.Value.Publication.InitialCommitId);
        Assert.Equal(generationEvidence.ToArray(), verified.Value.Evidence.ToArray());
    }

    [Fact]
    public async Task PrivateFakeGitHubFailurePersistsPendingAndRetryCompletesWithoutDuplicateGenerationOrCommit()
    {
        await using var fixture = await M8PublicationFixture.CreateAsync();
        var plan = await fixture.CreatePlanAsync(fixture.PrivateGitHubDraft);
        var generated = await fixture.ExecuteAsync(plan);
        var generationEvidence = generated.Checkpoint.Evidence;
        fixture.GitHub.FailNextPublish();

        var failed = await fixture.PublishAsync(plan.RunId);
        var pending = await fixture.LoadCheckpointAsync(plan.RunId);

        Assert.False(failed.IsSuccessful);
        Assert.Equal(RunStatus.PublishPending, pending.Run.Status);
        Assert.Equal(GitPublicationState.Succeeded, pending.Publication.GitState);
        Assert.Equal(GitHubPublicationState.Failed, pending.Publication.GitHubState);
        Assert.Equal(generationEvidence.ToArray(), pending.Evidence.ToArray());
        var initialCommit = pending.Publication.InitialCommitId;

        var retried = await fixture.PublishAsync(plan.RunId);

        Assert.True(retried.IsSuccessful);
        Assert.Equal(RunStatus.Completed, retried.Value.Run.Status);
        Assert.Equal(initialCommit, retried.Value.Publication.InitialCommitId);
        Assert.Equal(["main", "develop"], retried.Value.Publication.Branches.ToArray());
        Assert.Equal(generationEvidence.ToArray(), retried.Value.Evidence.ToArray());
        Assert.Equal(2, fixture.GitHub.PublishCalls);
        Assert.All(fixture.GitHub.Requests, request => Assert.True(request.IsPrivate));
        Assert.Single(fixture.GitHub.Requests.Select(request => request.OwnershipNonce).Distinct());
        Assert.True(File.Exists(fixture.PublicationReceiptPath(plan.RunId)));
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class M8PublicationTestGroup
{
    public const string Name = "M8 publication E2E";
}
