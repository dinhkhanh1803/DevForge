using DevForge.Application.Contracts;
using DevForge.Infrastructure.Git;

namespace DevForge.IntegrationTests.Infrastructure.Git;

public sealed class GitCommandFactoryTests
{
    [Fact]
    public void ClosedCommandsUseSeparatedArgumentsAndIsolatedEnvironment()
    {
        var workspace = new CommandWorkspace();

        var commands = new[]
        {
            GitCommandFactory.Version(workspace),
            GitCommandFactory.Initialize(workspace),
            GitCommandFactory.AddAll(workspace),
            GitCommandFactory.Commit(workspace),
            GitCommandFactory.Status(workspace),
            GitCommandFactory.Head(workspace),
            GitCommandFactory.Branches(workspace),
            GitCommandFactory.BranchHeads(workspace),
            GitCommandFactory.CurrentBranch(workspace),
            GitCommandFactory.CreateDevelop(workspace, new string('a', 40)),
        };

        Assert.All(commands, command =>
        {
            Assert.Equal(ExecutableTool.Git, command.Executable.Tool);
            Assert.True(command.UsesWorkspaceRoot);
            Assert.Same(workspace, command.Workspace);
            Assert.Equal([0], command.AllowedExitCodes.Order());
            Assert.Empty(command.RedactionNeedles);
            Assert.Equal(
                [
                    "GCM_INTERACTIVE", "GIT_AUTHOR_EMAIL", "GIT_AUTHOR_NAME",
                    "GIT_COMMITTER_EMAIL", "GIT_COMMITTER_NAME", "GIT_CONFIG_GLOBAL",
                    "GIT_CONFIG_NOSYSTEM", "GIT_PAGER", "GIT_TERMINAL_PROMPT", "LC_ALL", "PAGER",
                ],
                command.EnvironmentVariables.Keys.Order(StringComparer.Ordinal));
            Assert.All(command.EnvironmentVariables.Values, value =>
                Assert.Equal(ProcessValueSensitivity.Safe, value.Sensitivity));
            Assert.Contains("core.hooksPath=NUL", command.ArgumentList);
            Assert.Contains("core.autocrlf=false", command.ArgumentList);
            Assert.Contains("commit.gpgSign=false", command.ArgumentList);
            Assert.Contains("credential.helper=", command.ArgumentList);
            Assert.DoesNotContain(command.ArgumentList, argument =>
                argument.Contains("cmd", StringComparison.OrdinalIgnoreCase)
                || argument.Contains("powershell", StringComparison.OrdinalIgnoreCase)
                || argument.Contains("--force", StringComparison.OrdinalIgnoreCase)
                || argument.Contains("token", StringComparison.OrdinalIgnoreCase));
        });

        Assert.Equal(
            ["init", "--initial-branch=main", "--template="],
            OperationArguments(commands[1]));
        Assert.Equal(["add", "--all"], OperationArguments(commands[2]));
        Assert.Equal(
            ["commit", "--no-verify", "--message", "chore: bootstrap project with DevForge"],
            OperationArguments(commands[3]));
        Assert.Equal(
            ["branch", "develop", new string('a', 40)],
            OperationArguments(commands[9]));
        Assert.All(commands.SelectMany(OperationArguments), argument =>
            Assert.False(argument is "show" or "switch" or "checkout" or "reset" or "clean"));
    }

    [Fact]
    public void ReadCommandsHaveOnlyTheirExplicitAdditionalExitCodes()
    {
        var workspace = new CommandWorkspace();

        var head = GitCommandFactory.Head(workspace, allowMissing: true);

        Assert.Equal([0, 128], head.AllowedExitCodes.Order());
        Assert.Equal(["rev-parse", "--verify", "HEAD"], OperationArguments(head));
    }

    private static string[] OperationArguments(CommandSpec command)
    {
        var operations = new HashSet<string>(StringComparer.Ordinal)
        {
            "--version", "init", "add", "commit", "status", "rev-parse", "branch",
        };
        var index = -1;
        for (var candidate = 0; candidate < command.ArgumentList.Length; candidate++)
        {
            if (operations.Contains(command.ArgumentList[candidate]))
            {
                index = candidate;
                break;
            }
        }
        Assert.True(index >= 0);
        return [.. command.ArgumentList.Skip(index)];
    }

    private sealed class CommandWorkspace : IWorkspaceFileSystem
    {
        public WorkspaceRoot Root { get; } = WorkspaceRoot.Create("C:\\git-command-workspace").Value;

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
