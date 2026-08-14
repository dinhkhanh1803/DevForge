using DevForge.Application.Contracts;
using DevForge.Domain.Projects;

namespace DevForge.Infrastructure.GitHub;

internal static class GitHubCommandFactory
{
    public const string ActiveLoginQuery =
        ".hosts[\"github.com\"][] | select(.active == true) | .login";
    public const string RepositoryFields =
        "nameWithOwner,description,visibility,isEmpty,isFork,isInOrganization,isArchived,isMirror,isTemplate,url";
    public const string BranchReferenceQuery =
        ".[] | [.ref, .object.sha] | @tsv";
    private static readonly ExecutableIdentity _gitHubCli =
        ExecutableIdentity.Create("gh").Value;
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);

    public static CommandSpec Version(
        IWorkspaceFileSystem workspace,
        SensitiveProcessValue configDirectory) =>
        Create(workspace, configDirectory, ["--version"]);

    public static CommandSpec AuthenticationStatus(
        IWorkspaceFileSystem workspace,
        SensitiveProcessValue configDirectory) =>
        Create(
            workspace,
            configDirectory,
            [
                "auth", "status", "--active", "--hostname", "github.com",
                "--json", "hosts", "--jq", ActiveLoginQuery,
            ],
            [0, 1]);

    public static CommandSpec CurrentLogin(
        IWorkspaceFileSystem workspace,
        SensitiveProcessValue configDirectory) =>
        Create(
            workspace,
            configDirectory,
            ["api", "user", "--hostname", "github.com", "--jq", ".login"],
            [0, 1]);

    public static CommandSpec ViewRepository(
        IWorkspaceFileSystem workspace,
        SensitiveProcessValue configDirectory,
        GitHubRepositoryIdentity repository,
        bool allowMissing) =>
        Create(
            workspace,
            configDirectory,
            ["repo", "view", Slug(repository), "--json", RepositoryFields],
            allowMissing ? [0, 1] : [0]);

    public static CommandSpec CreateRepository(
        IWorkspaceFileSystem workspace,
        SensitiveProcessValue configDirectory,
        GitHubRepositoryIdentity repository,
        bool isPrivate,
        string ownershipNonce) =>
        Create(
            workspace,
            configDirectory,
            [
                "repo", "create", Slug(repository), isPrivate ? "--private" : "--public",
                "--description", "DevForge ownership " + ownershipNonce,
            ],
            [0, 1]);

    public static CommandSpec BranchReferences(
        IWorkspaceFileSystem workspace,
        SensitiveProcessValue configDirectory,
        GitHubRepositoryIdentity repository) =>
        Create(
            workspace,
            configDirectory,
            [
                "api", $"repos/{Slug(repository)}/git/matching-refs/heads",
                "--hostname", "github.com", "--paginate", "--jq", BranchReferenceQuery,
            ]);

    private static CommandSpec Create(
        IWorkspaceFileSystem workspace,
        SensitiveProcessValue configDirectory,
        IEnumerable<string> arguments,
        IEnumerable<int>? allowedExitCodes = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(configDirectory);
        var environment = new KeyValuePair<string, ProcessEnvironmentValue?>[]
        {
            Safe("CLICOLOR", "0"),
            Sensitive("GH_CONFIG_DIR", configDirectory),
            Safe("GH_HOST", "github.com"),
            Safe("GH_PAGER", string.Empty),
            Safe("GH_PROMPT_DISABLED", "1"),
            Safe("LC_ALL", "C"),
            Safe("NO_COLOR", "1"),
            Safe("PAGER", string.Empty),
        };
        var command = CommandSpec.CreateAtWorkspaceRoot(
            _gitHubCli,
            arguments,
            workspace,
            environment,
            _timeout,
            allowedExitCodes ?? [0],
            [configDirectory]);
        return command.IsValid ? command.Value : throw new InvalidOperationException();
    }

    private static string Slug(GitHubRepositoryIdentity repository) =>
        repository.Account + "/" + repository.RepositoryName;

    private static KeyValuePair<string, ProcessEnvironmentValue?> Safe(
        string name,
        string value) => KeyValuePair.Create<string, ProcessEnvironmentValue?>(
            name,
            ProcessEnvironmentValue.CreateSafe(value).Value);

    private static KeyValuePair<string, ProcessEnvironmentValue?> Sensitive(
        string name,
        SensitiveProcessValue value) => KeyValuePair.Create<string, ProcessEnvironmentValue?>(
            name,
            ProcessEnvironmentValue.CreateSensitive(value).Value);
}
