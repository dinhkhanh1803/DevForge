using DevForge.Application.Contracts;
using DevForge.Domain.Projects;
using DevForge.Infrastructure.GitHub;

namespace DevForge.IntegrationTests.Infrastructure.GitHub;

public sealed class GitHubCommandFactoryTests
{
    [Fact]
    public void ClosedCommandsUseFixedHostSeparatedArgumentsAndMinimalSensitiveEnvironment()
    {
        var workspace = new CommandWorkspace();
        var configDirectory = SensitiveProcessValue.Create("C:\\Users\\person\\AppData\\Roaming\\GitHub CLI").Value;
        var identity = GitHubRepositoryIdentity.Create("octocat", "devforge").Value;
        var commands = new[]
        {
            GitHubCommandFactory.Version(workspace, configDirectory),
            GitHubCommandFactory.AuthenticationStatus(workspace, configDirectory),
            GitHubCommandFactory.CurrentLogin(workspace, configDirectory),
            GitHubCommandFactory.ViewRepository(workspace, configDirectory, identity, allowMissing: true),
            GitHubCommandFactory.CreateRepository(
                workspace,
                configDirectory,
                identity,
                isPrivate: true,
                ownershipNonce: new string('a', 32)),
            GitHubCommandFactory.BranchReferences(workspace, configDirectory, identity),
        };

        Assert.All(commands, command =>
        {
            Assert.Equal(ExecutableTool.GitHubCli, command.Executable.Tool);
            Assert.True(command.UsesWorkspaceRoot);
            Assert.Same(workspace, command.Workspace);
            Assert.Equal(
                ["CLICOLOR", "GH_CONFIG_DIR", "GH_HOST", "GH_PAGER", "GH_PROMPT_DISABLED", "LC_ALL", "NO_COLOR", "PAGER"],
                command.EnvironmentVariables.Keys.Order(StringComparer.Ordinal));
            Assert.Equal(
                ProcessValueSensitivity.Sensitive,
                command.EnvironmentVariables["GH_CONFIG_DIR"].Sensitivity);
            Assert.Single(command.RedactionNeedles);
            Assert.DoesNotContain(command.ArgumentList, argument =>
                argument.Equals("token", StringComparison.OrdinalIgnoreCase)
                || argument.Equals("login", StringComparison.OrdinalIgnoreCase)
                || argument.Equals("switch", StringComparison.OrdinalIgnoreCase)
                || argument.Equals("delete", StringComparison.OrdinalIgnoreCase)
                || argument.StartsWith("--force", StringComparison.OrdinalIgnoreCase)
                || argument.Equals("--with-token", StringComparison.OrdinalIgnoreCase));
        });

        Assert.Equal(["--version"], commands[0].ArgumentList.ToArray());
        Assert.Equal(
            ["auth", "status", "--active", "--hostname", "github.com", "--json", "hosts", "--jq", GitHubCommandFactory.ActiveLoginQuery],
            commands[1].ArgumentList.ToArray());
        Assert.Equal(
            ["api", "user", "--hostname", "github.com", "--jq", ".login"],
            commands[2].ArgumentList.ToArray());
        Assert.Equal(
            ["repo", "view", "octocat/devforge", "--json", GitHubCommandFactory.RepositoryFields],
            commands[3].ArgumentList.ToArray());
        Assert.Equal(
            ["repo", "create", "octocat/devforge", "--private", "--description", "DevForge ownership " + new string('a', 32)],
            commands[4].ArgumentList.ToArray());
        Assert.Equal(
            ["api", "repos/octocat/devforge/git/matching-refs/heads", "--hostname", "github.com", "--paginate", "--jq", GitHubCommandFactory.BranchReferenceQuery],
            commands[5].ArgumentList.ToArray());
        Assert.Equal([0, 1], commands[3].AllowedExitCodes.Order());
        Assert.Equal([0, 1], commands[1].AllowedExitCodes.Order());
        Assert.Equal([0, 1], commands[4].AllowedExitCodes.Order());
        Assert.Equal([0], commands[5].AllowedExitCodes.Order());
    }

    [Fact]
    public void PublicVisibilityRequiresExplicitFactoryChoice()
    {
        var command = GitHubCommandFactory.CreateRepository(
            new CommandWorkspace(),
            SensitiveProcessValue.Create("C:\\gh-config").Value,
            GitHubRepositoryIdentity.Create("octocat", "public-repo").Value,
            isPrivate: false,
            ownershipNonce: new string('b', 32));

        Assert.Contains("--public", command.ArgumentList);
        Assert.DoesNotContain("--private", command.ArgumentList);
    }

    private sealed class CommandWorkspace : IWorkspaceFileSystem
    {
        public WorkspaceRoot Root { get; } = WorkspaceRoot.Create("C:\\github-command-workspace").Value;

        public Task<bool> FileExistsAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DirectoryExistsAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CreateDirectoryAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Stream> OpenWriteAsync(WorkspaceRelativePath path, bool overwrite, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteFileAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<System.Collections.Immutable.ImmutableArray<WorkspaceRelativePath>> EnumerateAllFilesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<System.Collections.Immutable.ImmutableArray<WorkspaceRelativePath>> EnumerateRootDirectoriesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<System.Collections.Immutable.ImmutableArray<WorkspaceRelativePath>> EnumerateFilesAsync(WorkspaceRelativePath directory, bool recursive, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<System.Collections.Immutable.ImmutableArray<WorkspaceRelativePath>> EnumerateDirectoriesAsync(WorkspaceRelativePath directory, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteDirectoryAsync(WorkspaceRelativePath path, DirectoryCleanupIntent intent, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MoveDirectoryAsync(WorkspaceRelativePath source, WorkspaceRelativePath destination, WorkspaceMoveIntent intent, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
