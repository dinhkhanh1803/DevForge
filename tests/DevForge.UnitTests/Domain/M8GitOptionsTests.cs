using DevForge.Domain.Projects;

namespace DevForge.UnitTests.Domain;

public sealed class M8GitOptionsTests
{
    [Fact]
    public void PublishingRequiresReviewedPersonalAccountAndRepository()
    {
        var invalid = GitOptions.Create(publishToGitHub: true);
        var valid = GitOptions.Create(
            publishToGitHub: true,
            githubAccount: "octocat",
            githubRepository: "devforge-sample");

        Assert.False(invalid.IsValid);
        Assert.Contains(invalid.Issues, issue => issue.Code == "git.github-account.required");
        Assert.Contains(invalid.Issues, issue => issue.Code == "git.github-repository.required");
        Assert.True(valid.IsValid);
        Assert.Equal("octocat", valid.Value.GitHubAccount);
        Assert.Equal("devforge-sample", valid.Value.GitHubRepository);
        Assert.True(valid.Value.IsPrivate);
    }

    [Fact]
    public void NonPublishingIntentRejectsAmbientRemoteIdentity()
    {
        var result = GitOptions.Create(
            publishToGitHub: false,
            githubAccount: "octocat",
            githubRepository: "devforge-sample");

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "git.github-identity.not-requested");
    }

    [Theory]
    [InlineData("bad--account", "devforge", "git.github-account.invalid")]
    [InlineData("octocat", "../devforge", "git.github-repository.invalid")]
    [InlineData("octocat", "devforge.git", "git.github-repository.invalid")]
    public void PublishingRejectsNonCanonicalGitHubIdentity(
        string account,
        string repository,
        string expectedCode)
    {
        var result = GitOptions.Create(
            publishToGitHub: true,
            githubAccount: account,
            githubRepository: repository);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == expectedCode);
    }
}
