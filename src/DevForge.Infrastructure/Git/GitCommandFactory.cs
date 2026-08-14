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

    public static CommandSpec Remotes(IWorkspaceFileSystem workspace) =>
        Create(workspace, ["remote"]);

    public static CommandSpec OriginUrl(
        IWorkspaceFileSystem workspace,
        bool allowMissing = false) => Create(
            workspace,
            ["remote", "get-url", "origin"],
            allowMissing ? [0, 2] : [0]);

    public static CommandSpec PushOriginUrl(IWorkspaceFileSystem workspace) => Create(
        workspace,
        ["remote", "get-url", "--push", "--all", "origin"]);

    public static CommandSpec AddOrigin(
        IWorkspaceFileSystem workspace,
        string remoteUrl) => Create(
            workspace,
            ["remote", "add", "origin", remoteUrl]);

    public static CommandSpec PushBranch(
        IWorkspaceFileSystem workspace,
        string branch,
        string commitId,
        string remoteUrl,
        SensitiveProcessValue credentialHelperExecutable,
        SensitiveProcessValue gitHubConfigDirectory)
    {
        if (branch is not ("main" or "develop"))
        {
            throw new ArgumentOutOfRangeException(nameof(branch));
        }

        if (!PublicationSnapshot.IsObjectId(commitId))
        {
            throw new ArgumentException("A canonical commit identifier is required.", nameof(commitId));
        }

        if (!Uri.TryCreate(remoteUrl, UriKind.Absolute, out var remote)
            || remote.Scheme != Uri.UriSchemeHttps
            || remote.Host != "github.com"
            || !remote.IsDefaultPort
            || !string.IsNullOrEmpty(remote.UserInfo)
            || !string.IsNullOrEmpty(remote.Query)
            || !string.IsNullOrEmpty(remote.Fragment)
            || !remote.AbsolutePath.EndsWith(".git", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A canonical github.com HTTPS remote is required.",
                nameof(remoteUrl));
        }

        ArgumentNullException.ThrowIfNull(credentialHelperExecutable);
        ArgumentNullException.ThrowIfNull(gitHubConfigDirectory);
        var helperPath = credentialHelperExecutable.RevealForProcessStart();
        if (!Path.IsPathFullyQualified(helperPath)
            || helperPath.StartsWith(@"\\", StringComparison.Ordinal)
            || !helperPath.EndsWith("gh.exe", StringComparison.OrdinalIgnoreCase)
            || helperPath.Any(character => !IsSafeHelperPathCharacter(character)))
        {
            throw new ArgumentException(
                "A trusted local GitHub CLI path is required.",
                nameof(credentialHelperExecutable));
        }

        var normalizedHelperPath = helperPath.Replace('\\', '/');
        var normalizedHelperNeedle = SensitiveProcessValue.Create(normalizedHelperPath);
        if (!normalizedHelperNeedle.IsValid)
        {
            throw new ArgumentException(
                "A trusted local GitHub CLI path is required.",
                nameof(credentialHelperExecutable));
        }

        var helper = $"!\"{normalizedHelperPath}\" auth git-credential";

        return Create(
            workspace,
            [
                "push", remoteUrl, $"{commitId}:refs/heads/{branch}",
            ],
            timeout: TimeSpan.FromMinutes(2),
            additionalConfiguration:
            [
                "-c", "credential.https://github.com.helper=" + helper,
                "-c", "credential.https://github.com.useHttpPath=true",
            ],
            additionalEnvironment:
            [
                Sensitive("GH_CONFIG_DIR", gitHubConfigDirectory),
                Safe("GH_HOST", "github.com"),
                Safe("GH_PAGER", string.Empty),
                Safe("GH_PROMPT_DISABLED", "1"),
            ],
            redactionNeedles:
            [
                credentialHelperExecutable,
                normalizedHelperNeedle.Value,
                gitHubConfigDirectory,
            ]);
    }

    private static CommandSpec Create(
        IWorkspaceFileSystem workspace,
        IEnumerable<string> operation,
        IEnumerable<int>? allowedExitCodes = null,
        TimeSpan? timeout = null,
        IEnumerable<string>? additionalConfiguration = null,
        IEnumerable<KeyValuePair<string, ProcessEnvironmentValue?>>? additionalEnvironment = null,
        IEnumerable<SensitiveProcessValue>? redactionNeedles = null)
    {
        var command = CommandSpec.CreateAtWorkspaceRoot(
            _git,
            _isolatedPrefix.Concat(additionalConfiguration ?? []).Concat(operation),
            workspace,
            _environment.Concat(additionalEnvironment ?? []),
            timeout ?? _timeout,
            allowedExitCodes ?? [0],
            redactionNeedles ?? []);
        return command.IsValid ? command.Value : throw new InvalidOperationException();
    }

    private static KeyValuePair<string, ProcessEnvironmentValue?> Safe(string name, string value) =>
        KeyValuePair.Create<string, ProcessEnvironmentValue?>(
            name,
            ProcessEnvironmentValue.CreateSafe(value).Value);

    private static KeyValuePair<string, ProcessEnvironmentValue?> Sensitive(
        string name,
        SensitiveProcessValue value) => KeyValuePair.Create<string, ProcessEnvironmentValue?>(
            name,
            ProcessEnvironmentValue.CreateSensitive(value).Value);

    private static bool IsSafeHelperPathCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character)
        || character is ':' or '\\' or '/' or ' ' or '.' or '_' or '-' or '(' or ')';
}
