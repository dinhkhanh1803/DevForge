namespace DevForge.IntegrationTests.Infrastructure.Git;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GitEnvironmentIsolationTestGroup
{
    public const string Name = "Git environment isolation";
}
