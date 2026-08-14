using System.Collections.Immutable;
using DevForge.Domain.Projects;
using DevForge.Domain.Validation;

namespace DevForge.Application.Contracts;

public sealed class GitHubAuthenticationRequest
{
    private GitHubAuthenticationRequest(GitHubRepositoryIdentity repository)
    {
        Repository = repository;
    }

    public GitHubRepositoryIdentity Repository { get; }

    public static ValidationResult<GitHubAuthenticationRequest> Create(
        GitHubRepositoryIdentity? repository) => repository is null
        ? ValidationResult.Failure<GitHubAuthenticationRequest>(
        [
            new ValidationIssue(
                "github.repository.required",
                "The reviewed GitHub repository identity is required.",
                "repository"),
        ])
        : ValidationResult.Success(new GitHubAuthenticationRequest(repository));
}

public enum GitHubAuthenticationState
{
    Authenticated = 1,
    NotAuthenticated = 2,
    DifferentAccount = 3,
}

public sealed class GitHubAuthenticationResult
{
    private GitHubAuthenticationResult(
        GitHubRepositoryIdentity repository,
        GitHubAuthenticationState state)
    {
        Repository = repository;
        State = state;
    }

    public GitHubRepositoryIdentity Repository { get; }

    public GitHubAuthenticationState State { get; }

    public static ValidationResult<GitHubAuthenticationResult> Create(
        GitHubRepositoryIdentity? repository,
        GitHubAuthenticationState state)
    {
        var issues = new List<ValidationIssue>();
        if (repository is null)
        {
            issues.Add(new ValidationIssue(
                "github.authentication.repository.required",
                "The reviewed GitHub repository identity is required.",
                "repository"));
        }

        if (!Enum.IsDefined(state))
        {
            issues.Add(new ValidationIssue(
                "github.authentication.state.invalid",
                "The GitHub authentication state is not defined.",
                "state"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new GitHubAuthenticationResult(repository!, state))
            : ValidationResult.Failure<GitHubAuthenticationResult>(issues);
    }
}

public sealed class GitHubPublishRequest
{
    private GitHubPublishRequest(
        IWorkspaceFileSystem workspace,
        GitHubRepositoryIdentity repository,
        GitBranchPolicy branchPolicy,
        string initialCommitId,
        ImmutableArray<string> branches,
        bool isPrivate,
        string ownershipNonce)
    {
        Workspace = workspace;
        Repository = repository;
        BranchPolicy = branchPolicy;
        InitialCommitId = initialCommitId;
        Branches = branches;
        IsPrivate = isPrivate;
        OwnershipNonce = ownershipNonce;
    }

    public IWorkspaceFileSystem Workspace { get; }

    public GitHubRepositoryIdentity Repository { get; }

    public GitBranchPolicy BranchPolicy { get; }

    public string InitialCommitId { get; }

    public ImmutableArray<string> Branches { get; }

    public bool IsPrivate { get; }

    public string OwnershipNonce { get; }

    public static ValidationResult<GitHubPublishRequest> Create(
        IWorkspaceFileSystem? workspace,
        GitHubRepositoryIdentity? repository,
        GitBranchPolicy branchPolicy,
        string? initialCommitId,
        IEnumerable<string?>? branches,
        bool isPrivate,
        string? ownershipNonce)
    {
        var snapshot = PublicationSnapshot.SnapshotBranches(branches, out var branchesOverflow);
        var issues = new List<ValidationIssue>();
        if (workspace is null)
        {
            issues.Add(new ValidationIssue(
                "github.workspace.required",
                "A scoped workspace is required for GitHub publishing.",
                "workspace"));
        }

        if (repository is null)
        {
            issues.Add(new ValidationIssue(
                "github.repository.required",
                "The reviewed GitHub repository identity is required.",
                "repository"));
        }

        if (!Enum.IsDefined(branchPolicy))
        {
            issues.Add(new ValidationIssue(
                "github.branch-policy.invalid",
                "The reviewed Git branch policy is not defined.",
                "branchPolicy"));
        }

        if (!PublicationSnapshot.IsObjectId(initialCommitId))
        {
            issues.Add(new ValidationIssue(
                "github.commit-id.invalid",
                "A canonical initial Git object identifier is required.",
                "initialCommitId"));
        }

        if (branchesOverflow || !PublicationSnapshot.BranchesMatchPolicy(snapshot, branchPolicy))
        {
            issues.Add(new ValidationIssue(
                "github.branches.policy-mismatch",
                "GitHub branches must exactly match the reviewed branch policy.",
                "branches"));
        }

        if (!PublicationSnapshot.IsOwnershipNonce(ownershipNonce))
        {
            issues.Add(new ValidationIssue(
                "github.ownership-nonce.invalid",
                "A canonical publication ownership nonce is required.",
                "ownershipNonce"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new GitHubPublishRequest(
                workspace!,
                repository!,
                branchPolicy,
                initialCommitId!,
                [.. snapshot.Select(item => item!)],
                isPrivate,
                ownershipNonce!))
            : ValidationResult.Failure<GitHubPublishRequest>(issues);
    }
}

public sealed class GitHubPublishResult
{
    private GitHubPublishResult(
        GitHubRepositoryIdentity repository,
        string repositoryUrl,
        string initialCommitId,
        ImmutableArray<string> branches,
        GitBranchPolicy branchPolicy,
        bool isPrivate,
        string ownershipNonce)
    {
        Repository = repository;
        RepositoryUrl = repositoryUrl;
        InitialCommitId = initialCommitId;
        Branches = branches;
        BranchPolicy = branchPolicy;
        IsPrivate = isPrivate;
        OwnershipNonce = ownershipNonce;
    }

    public GitHubRepositoryIdentity Repository { get; }

    public string RepositoryUrl { get; }

    public string InitialCommitId { get; }

    public ImmutableArray<string> Branches { get; }

    public GitBranchPolicy BranchPolicy { get; }

    public bool IsPrivate { get; }

    public string OwnershipNonce { get; }

    public static ValidationResult<GitHubPublishResult> Create(
        GitHubRepositoryIdentity? repository,
        string? repositoryUrl,
        string? initialCommitId,
        IEnumerable<string?>? branches,
        GitBranchPolicy branchPolicy,
        bool isPrivate,
        string? ownershipNonce)
    {
        var snapshot = PublicationSnapshot.SnapshotBranches(branches, out var branchesOverflow);
        var issues = new List<ValidationIssue>();
        if (repository is null
            || !StringComparer.Ordinal.Equals(repository.HttpsWebUrl, repositoryUrl))
        {
            issues.Add(new ValidationIssue(
                "github.receipt.repository.invalid",
                "The GitHub receipt must match the reviewed HTTPS repository.",
                "repositoryUrl"));
        }

        if (!PublicationSnapshot.IsObjectId(initialCommitId))
        {
            issues.Add(new ValidationIssue(
                "github.receipt.commit-id.invalid",
                "The GitHub receipt requires the verified initial commit.",
                "initialCommitId"));
        }

        if (branchesOverflow || !PublicationSnapshot.BranchesMatchPolicy(snapshot, branchPolicy))
        {
            issues.Add(new ValidationIssue(
                "github.receipt.branches.invalid",
                "The GitHub receipt branches must match the reviewed policy.",
                "branches"));
        }

        if (!PublicationSnapshot.IsOwnershipNonce(ownershipNonce))
        {
            issues.Add(new ValidationIssue(
                "github.receipt.ownership-nonce.invalid",
                "The GitHub receipt requires the verified ownership nonce.",
                "ownershipNonce"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new GitHubPublishResult(
                repository!,
                repositoryUrl!,
                initialCommitId!,
                [.. snapshot.Select(item => item!)],
                branchPolicy,
                isPrivate,
                ownershipNonce!))
            : ValidationResult.Failure<GitHubPublishResult>(issues);
    }
}

public interface IGitHubService
{
    Task<GitHubAuthenticationResult> CheckAuthenticationAsync(
        GitHubAuthenticationRequest request,
        CancellationToken cancellationToken);

    Task<GitHubPublishResult> PublishAsync(
        GitHubPublishRequest request,
        CancellationToken cancellationToken);
}
