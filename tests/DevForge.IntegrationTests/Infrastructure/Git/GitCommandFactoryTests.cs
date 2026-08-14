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

    [Fact]
    public void PublicationCommandsUseOnlyExactOriginAndOrdinaryBranchPushes()
    {
        var workspace = new CommandWorkspace();
        var remoteUrl = "https://github.com/octocat/devforge.git";
        var commitId = new string('a', 40);
        var helper = SensitiveProcessValue.Create(
            "C:\\Program Files\\GitHub CLI\\gh.exe").Value;
        var config = SensitiveProcessValue.Create("C:\\private-gh-config").Value;
        var commands = new[]
        {
            GitCommandFactory.Remotes(workspace),
            GitCommandFactory.OriginUrl(workspace, allowMissing: true),
            GitCommandFactory.PushOriginUrl(workspace),
            GitCommandFactory.AddOrigin(workspace, remoteUrl),
            GitCommandFactory.PushBranch(workspace, "main", commitId, remoteUrl, helper, config),
            GitCommandFactory.PushBranch(workspace, "develop", commitId, remoteUrl, helper, config),
        };

        Assert.Equal(["remote"], OperationArguments(commands[0]));
        Assert.Equal(["remote", "get-url", "origin"], OperationArguments(commands[1]));
        Assert.Equal([0, 2], commands[1].AllowedExitCodes.Order());
        Assert.Equal(
            ["remote", "get-url", "--push", "--all", "origin"],
            OperationArguments(commands[2]));
        Assert.Equal(["remote", "add", "origin", remoteUrl], OperationArguments(commands[3]));
        Assert.Equal(
            ["push", remoteUrl, $"{commitId}:refs/heads/main"],
            OperationArguments(commands[4]));
        Assert.Equal(
            ["push", remoteUrl, $"{commitId}:refs/heads/develop"],
            OperationArguments(commands[5]));
        Assert.Contains(
            commands[4].ArgumentList,
            argument => argument.StartsWith(
                "credential.https://github.com.helper=",
                StringComparison.Ordinal));
        Assert.Contains(helper, commands[4].RedactionNeedles);
        Assert.Contains(config, commands[4].RedactionNeedles);
        Assert.Equal(
            ProcessValueSensitivity.Sensitive,
            commands[4].EnvironmentVariables["GH_CONFIG_DIR"].Sensitivity);
        Assert.Contains(
            "credential.https://github.com.helper=!\"C:/Program Files/GitHub CLI/gh.exe\" auth git-credential",
            commands[4].ArgumentList);
        Assert.DoesNotContain(commands[4].ArgumentList, argument =>
            argument.Contains("auth token", StringComparison.OrdinalIgnoreCase)
            || argument.Contains("setup-git", StringComparison.OrdinalIgnoreCase));
        Assert.All(commands.SelectMany(OperationArguments), argument =>
            Assert.DoesNotContain("force", argument, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("C:\\tools\\$(malicious)\\gh.exe")]
    [InlineData("C:\\tools\\`malicious`\\gh.exe")]
    [InlineData("C:\\tools\\evil&command\\gh.exe")]
    [InlineData("C:\\tools\\not-gh.exe.bat")]
    public void CredentialHelperRejectsShellShapedOrWrongExecutablePaths(string path)
    {
        var helper = SensitiveProcessValue.Create(path).Value;

        Assert.Throws<ArgumentException>(() => GitCommandFactory.PushBranch(
            new CommandWorkspace(),
            "main",
            new string('a', 40),
            "https://github.com/octocat/devforge.git",
            helper,
            SensitiveProcessValue.Create("C:\\private-gh-config").Value));
    }

    private static string[] OperationArguments(CommandSpec command)
    {
        var operations = new HashSet<string>(StringComparer.Ordinal)
        {
            "--version", "init", "add", "commit", "status", "rev-parse", "branch", "remote", "push",
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
