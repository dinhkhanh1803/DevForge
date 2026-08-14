using DevForge.Domain.Projects;

namespace DevForge.UnitTests.Domain;

public sealed class ProjectOptionTests
{
    [Fact]
    public void TeamProfileSnapshotsStandards()
    {
        var standards = new Dictionary<string, string?> { [" nullable "] = " enabled " };

        var result = TeamProfile.Create("team.standard", "Team Standard", standards);
        Assert.True(result.IsValid);
        var profile = result.Value;

        standards[" nullable "] = "disabled";

        Assert.Equal(" enabled ", profile.Standards["nullable"]);
    }

    [Fact]
    public void GitAndCompletionOptionsHaveSafeDefaults()
    {
        var result = GitOptions.Create();
        var completion = CompletionOptions.Create().Value;

        Assert.True(result.IsValid);
        var git = result.Value;
        Assert.True(git.InitializeRepository);
        Assert.Equal("main", git.PrimaryBranch);
        Assert.False(git.PublishToGitHub);
        Assert.True(git.IsPrivate);
        Assert.Equal(GitBranchPolicy.Main, git.BranchPolicy);
        Assert.True(completion.WriteGenerationReport);
        Assert.False(completion.OpenIde);
    }

    [Fact]
    public void TeamProfileCreateAggregatesExpectedInputIssues()
    {
        var result = TeamProfile.Create(" ", " ", null);

        Assert.False(result.IsValid);
        Assert.Equal(
            ["team.id.required", "team.name.required", "team.standards.required"],
            result.Issues.Select(issue => issue.Code));
    }

    [Theory]
    [InlineData("", "git.primary-branch.required")]
    [InlineData("master", "git.primary-branch.unsupported")]
    public void GitOptionsRejectsInvalidPrimaryBranchPolicies(string primaryBranch, string expectedCode)
    {
        var result = GitOptions.Create(primaryBranch: primaryBranch);

        Assert.False(result.IsValid);
        Assert.Equal(expectedCode, Assert.Single(result.Issues).Code);
    }

    [Fact]
    public void GitOptionsModelsTheSupportedMainAndDevelopPolicy()
    {
        var result = GitOptions.Create(useDevelopBranch: true);

        Assert.True(result.IsValid);
        Assert.Equal(GitBranchPolicy.MainAndDevelop, result.Value.BranchPolicy);
        Assert.True(result.Value.UseDevelopBranch);
    }

    [Fact]
    public void TeamProfileDetectsDuplicatesAfterIdentifierNormalization()
    {
        var standards = new Dictionary<string, string?>
        {
            [" style "] = "strict",
            ["style"] = "relaxed",
        };

        var result = TeamProfile.Create("team.standard", "Team Standard", standards);

        Assert.False(result.IsValid);
        Assert.Equal("team.standard.name.duplicate", Assert.Single(result.Issues).Code);
    }

    [Fact]
    public void GitOptionsRejectsFeaturesThatRequireDisabledInitialization()
    {
        var result = GitOptions.Create(
            initializeRepository: false,
            useDevelopBranch: true,
            publishToGitHub: true);

        Assert.False(result.IsValid);
        Assert.Equal(
            [
                "git.develop.requires-initialization",
                "git.publish.requires-initialization",
                "git.github-account.required",
                "git.github-repository.required",
            ],
            result.Issues.Select(issue => issue.Code));
    }

    [Theory]
    [InlineData(true, null, "completion.ide.required")]
    [InlineData(false, "visual-studio", "completion.ide.unexpected")]
    [InlineData(false, " ", "completion.ide.unexpected")]
    public void CompletionOptionsRejectsContradictoryIdeData(bool openIde, string? ideId, string expectedCode)
    {
        var result = CompletionOptions.Create(openIde: openIde, ideId: ideId);

        Assert.False(result.IsValid);
        Assert.Equal(expectedCode, Assert.Single(result.Issues).Code);
    }

    [Fact]
    public void CompletionOptionsTrimsSelectedIde()
    {
        var result = CompletionOptions.Create(openIde: true, ideId: " visual-studio ");

        Assert.True(result.IsValid);
        Assert.Equal("visual-studio", result.Value.IdeId);
    }
}
