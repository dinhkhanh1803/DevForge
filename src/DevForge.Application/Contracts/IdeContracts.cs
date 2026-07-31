using DevForge.Domain.Validation;

namespace DevForge.Application.Contracts;

public sealed class IdeLaunchRequest
{
    private IdeLaunchRequest(IWorkspaceFileSystem workspace, string ideId)
    {
        Workspace = workspace;
        IdeId = ideId;
    }

    public IWorkspaceFileSystem Workspace { get; }

    public string IdeId { get; }

    public static ValidationResult<IdeLaunchRequest> Create(
        IWorkspaceFileSystem? workspace,
        string? ideId)
    {
        var issues = new List<ValidationIssue>();
        if (workspace is null)
        {
            issues.Add(
                new ValidationIssue(
                    "ide.workspace.required",
                    "A scoped workspace is required to launch an IDE.",
                    "workspace"));
        }

        if (string.IsNullOrWhiteSpace(ideId))
        {
            issues.Add(
                new ValidationIssue(
                    "ide.id.required",
                    "An IDE identifier is required.",
                    "ideId"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new IdeLaunchRequest(workspace!, ideId!.Trim()))
            : ValidationResult.Failure<IdeLaunchRequest>(issues);
    }
}

public interface IIdeLauncher
{
    Task LaunchAsync(
        IdeLaunchRequest request,
        CancellationToken cancellationToken);
}
