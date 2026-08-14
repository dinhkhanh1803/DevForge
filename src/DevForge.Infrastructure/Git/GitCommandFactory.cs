using DevForge.Application.Contracts;

namespace DevForge.Infrastructure.Git;

internal static class GitCommandFactory
{
    public const string BootstrapMessage = "chore: bootstrap project with DevForge";
    public const string AuthorName = "DevForge Studio";
    public const string AuthorEmail = "devforge@localhost";
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);
    private static readonly ExecutableIdentity _git = ExecutableIdentity.Create("git").Value;
    private static readonly KeyValuePair<string, ProcessEnvironmentValue?>[] _environment =
    [
        Safe("GIT_CONFIG_NOSYSTEM", "1"),
        Safe("GIT_CONFIG_GLOBAL", "NUL"),
        Safe("GIT_TERMINAL_PROMPT", "0"),
        Safe("GCM_INTERACTIVE", "Never"),
        Safe("GIT_PAGER", string.Empty),
        Safe("PAGER", string.Empty),
        Safe("GIT_AUTHOR_NAME", AuthorName),
        Safe("GIT_AUTHOR_EMAIL", AuthorEmail),
        Safe("GIT_COMMITTER_NAME", AuthorName),
        Safe("GIT_COMMITTER_EMAIL", AuthorEmail),
        Safe("LC_ALL", "C"),
    ];
    private static readonly string[] _isolatedPrefix =
    [
        "--no-pager",
        "-c", "core.hooksPath=NUL",
        "-c", "core.fsmonitor=false",
        "-c", "core.autocrlf=false",
        "-c", "core.safecrlf=false",
        "-c", "core.attributesFile=NUL",
        "-c", "commit.gpgSign=false",
        "-c", "tag.gpgSign=false",
        "-c", "credential.helper=",
        "-c", "diff.external=",
    ];

    public static CommandSpec Version(IWorkspaceFileSystem workspace) =>
        Create(workspace, ["--version"]);

    public static CommandSpec Initialize(IWorkspaceFileSystem workspace) =>
        Create(workspace, ["init", "--initial-branch=main", "--template="]);

    public static CommandSpec AddAll(IWorkspaceFileSystem workspace) =>
        Create(workspace, ["add", "--all"]);

    public static CommandSpec Commit(IWorkspaceFileSystem workspace) =>
        Create(workspace, ["commit", "--no-verify", "--message", BootstrapMessage]);

    public static CommandSpec Status(IWorkspaceFileSystem workspace) =>
        Create(workspace, ["status", "--porcelain=v1", "--untracked-files=all"]);

    public static CommandSpec Head(IWorkspaceFileSystem workspace, bool allowMissing = false) =>
        Create(
            workspace,
            ["rev-parse", "--verify", "HEAD"],
            allowMissing ? [0, 128] : [0]);

    public static CommandSpec Branches(IWorkspaceFileSystem workspace) =>
        Create(workspace, ["branch", "--format=%(refname:short)"]);

    public static CommandSpec BranchHeads(IWorkspaceFileSystem workspace) =>
        Create(workspace, ["branch", "--format=%(refname:short) %(objectname)"]);

    public static CommandSpec CurrentBranch(IWorkspaceFileSystem workspace) =>
        Create(workspace, ["branch", "--show-current"]);

    public static CommandSpec CreateDevelop(IWorkspaceFileSystem workspace, string commitId) =>
        Create(workspace, ["branch", "develop", commitId]);

    private static CommandSpec Create(
        IWorkspaceFileSystem workspace,
        IEnumerable<string> operation,
        IEnumerable<int>? allowedExitCodes = null)
    {
        var command = CommandSpec.CreateAtWorkspaceRoot(
            _git,
            _isolatedPrefix.Concat(operation),
            workspace,
            _environment,
            _timeout,
            allowedExitCodes ?? [0],
            []);
        return command.IsValid ? command.Value : throw new InvalidOperationException();
    }

    private static KeyValuePair<string, ProcessEnvironmentValue?> Safe(string name, string value) =>
        KeyValuePair.Create<string, ProcessEnvironmentValue?>(
            name,
            ProcessEnvironmentValue.CreateSafe(value).Value);
}
