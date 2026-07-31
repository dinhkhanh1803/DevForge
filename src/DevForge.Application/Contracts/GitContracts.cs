using DevForge.Domain.Validation;

namespace DevForge.Application.Contracts;

public sealed class GitCommitRequest
{
    private GitCommitRequest(IWorkspaceFileSystem workspace, string message)
    {
        Workspace = workspace;
        Message = message;
    }

    public IWorkspaceFileSystem Workspace { get; }

    public string Message { get; }

    public static ValidationResult<GitCommitRequest> Create(
        IWorkspaceFileSystem? workspace,
        string? message)
    {
        var issues = new List<ValidationIssue>();
        if (workspace is null)
        {
            issues.Add(
                new ValidationIssue(
                    "git.workspace.required",
                    "A scoped workspace is required for a Git commit.",
                    "workspace"));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            issues.Add(
                new ValidationIssue(
                    "git.commit-message.required",
                    "A Git commit message is required.",
                    "message"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new GitCommitRequest(workspace!, message!.Trim()))
            : ValidationResult.Failure<GitCommitRequest>(issues);
    }
}

public interface IGitService
{
    Task InitializeAsync(
        IWorkspaceFileSystem workspace,
        CancellationToken cancellationToken);

    Task CommitAsync(
        GitCommitRequest request,
        CancellationToken cancellationToken);
}
