using DevForge.Domain.Validation;

namespace DevForge.Application.Contracts;

public sealed class GitHubPublishRequest
{
    private GitHubPublishRequest(
        IWorkspaceFileSystem workspace,
        string repositoryName,
        bool isPrivate)
    {
        Workspace = workspace;
        RepositoryName = repositoryName;
        IsPrivate = isPrivate;
    }

    public IWorkspaceFileSystem Workspace { get; }

    public string RepositoryName { get; }

    public bool IsPrivate { get; }

    public static ValidationResult<GitHubPublishRequest> Create(
        IWorkspaceFileSystem? workspace,
        string? repositoryName,
        bool isPrivate = true)
    {
        var issues = new List<ValidationIssue>();
        if (workspace is null)
        {
            issues.Add(
                new ValidationIssue(
                    "github.workspace.required",
                    "A scoped workspace is required for GitHub publishing.",
                    "workspace"));
        }

        if (string.IsNullOrWhiteSpace(repositoryName))
        {
            issues.Add(
                new ValidationIssue(
                    "github.repository-name.required",
                    "A GitHub repository name is required.",
                    "repositoryName"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(
                new GitHubPublishRequest(workspace!, repositoryName!.Trim(), isPrivate))
            : ValidationResult.Failure<GitHubPublishRequest>(issues);
    }
}

public sealed record GitHubPublishResult(string RepositoryUrl);

public interface IGitHubService
{
    Task<GitHubPublishResult> PublishAsync(
        GitHubPublishRequest request,
        CancellationToken cancellationToken);
}
