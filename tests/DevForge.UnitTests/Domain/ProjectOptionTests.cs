using DevForge.Domain.Projects;

namespace DevForge.UnitTests.Domain;

public sealed class ProjectOptionTests
{
    [Fact]
    public void TeamProfileSnapshotsStandards()
    {
        var standards = new Dictionary<string, string> { ["nullable"] = "enabled" };

        var profile = TeamProfile.Create("team.standard", "Team Standard", standards);
        standards["nullable"] = "disabled";

        Assert.Equal("enabled", profile.Standards["nullable"]);
    }

    [Fact]
    public void GitAndCompletionOptionsHaveSafeDefaults()
    {
        var git = new GitOptions();
        var completion = new CompletionOptions();

        Assert.True(git.InitializeRepository);
        Assert.Equal("main", git.PrimaryBranch);
        Assert.False(git.PublishToGitHub);
        Assert.True(git.IsPrivate);
        Assert.True(completion.WriteGenerationReport);
        Assert.False(completion.OpenIde);
    }
}
