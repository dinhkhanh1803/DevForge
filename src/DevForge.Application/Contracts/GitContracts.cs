using System.Collections.Immutable;
using DevForge.Domain.Projects;
using DevForge.Domain.Validation;

namespace DevForge.Application.Contracts;

public sealed class GitBootstrapRequest
{
    private GitBootstrapRequest(
        IWorkspaceFileSystem workspace,
        GitBranchPolicy branchPolicy,
        string finalTreeDigest)
    {
        Workspace = workspace;
        BranchPolicy = branchPolicy;
        FinalTreeDigest = finalTreeDigest;
    }

    public IWorkspaceFileSystem Workspace { get; }

    public GitBranchPolicy BranchPolicy { get; }

    public string FinalTreeDigest { get; }

    public static ValidationResult<GitBootstrapRequest> Create(
        IWorkspaceFileSystem? workspace,
        GitBranchPolicy branchPolicy,
        string? finalTreeDigest)
    {
        var issues = GitContractValidation.ValidateCommon(workspace, branchPolicy, finalTreeDigest);
        return issues.Count == 0
            ? ValidationResult.Success(
                new GitBootstrapRequest(workspace!, branchPolicy, finalTreeDigest!))
            : ValidationResult.Failure<GitBootstrapRequest>(issues);
    }
}

public sealed class GitVerificationRequest
{
    private GitVerificationRequest(
        IWorkspaceFileSystem workspace,
        GitBranchPolicy branchPolicy,
        string finalTreeDigest,
        string initialCommitId,
        string? expectedOriginUrl)
    {
        Workspace = workspace;
        BranchPolicy = branchPolicy;
        FinalTreeDigest = finalTreeDigest;
        InitialCommitId = initialCommitId;
        ExpectedOriginUrl = expectedOriginUrl;
    }

    public IWorkspaceFileSystem Workspace { get; }

    public GitBranchPolicy BranchPolicy { get; }

    public string FinalTreeDigest { get; }

    public string InitialCommitId { get; }

    public string? ExpectedOriginUrl { get; }

    public static ValidationResult<GitVerificationRequest> Create(
        IWorkspaceFileSystem? workspace,
        GitBranchPolicy branchPolicy,
        string? finalTreeDigest,
        string? initialCommitId,
        string? expectedOriginUrl = null)
    {
        var issues = GitContractValidation.ValidateCommon(workspace, branchPolicy, finalTreeDigest);
        if (!PublicationSnapshot.IsObjectId(initialCommitId))
        {
            issues.Add(new ValidationIssue(
                "git.commit-id.invalid",
                "A canonical initial Git object identifier is required.",
                "initialCommitId"));
        }

        if (expectedOriginUrl is not null && !IsCanonicalGitHubRemote(expectedOriginUrl))
        {
            issues.Add(new ValidationIssue(
                "git.origin-url.invalid",
                "The expected origin must be a canonical github.com HTTPS remote.",
                "expectedOriginUrl"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new GitVerificationRequest(
                workspace!, branchPolicy, finalTreeDigest!, initialCommitId!, expectedOriginUrl))
            : ValidationResult.Failure<GitVerificationRequest>(issues);
    }

    private static bool IsCanonicalGitHubRemote(string value)
    {
        if (value.Length > 256
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || uri.Host != "github.com"
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        var path = uri.AbsolutePath.Trim('/');
        if (!path.EndsWith(".git", StringComparison.Ordinal))
        {
            return false;
        }

        var segments = path[..^4].Split('/');
        if (segments.Length != 2)
        {
            return false;
        }

        var identity = GitHubRepositoryIdentity.Create(segments[0], segments[1]);
        return identity.IsValid
            && StringComparer.Ordinal.Equals(identity.Value.HttpsRemoteUrl, value);
    }
}

public sealed class GitRepositoryReceipt
{
    private GitRepositoryReceipt(
        string initialCommitId,
        GitBranchPolicy branchPolicy,
        ImmutableArray<string> branches,
        string finalTreeDigest)
    {
        InitialCommitId = initialCommitId;
        BranchPolicy = branchPolicy;
        Branches = branches;
        FinalTreeDigest = finalTreeDigest;
    }

    public string InitialCommitId { get; }

    public GitBranchPolicy BranchPolicy { get; }

    public ImmutableArray<string> Branches { get; }

    public string FinalTreeDigest { get; }

    public static ValidationResult<GitRepositoryReceipt> Create(
        string? initialCommitId,
        GitBranchPolicy branchPolicy,
        IEnumerable<string?>? branches,
        string? finalTreeDigest)
    {
        var snapshot = PublicationSnapshot.SnapshotBranches(branches, out var branchesOverflow);
        var issues = GitContractValidation.ValidatePolicyAndDigest(branchPolicy, finalTreeDigest);
        if (!PublicationSnapshot.IsObjectId(initialCommitId))
        {
            issues.Add(new ValidationIssue(
                "git.commit-id.invalid",
                "A canonical initial Git object identifier is required.",
                "initialCommitId"));
        }

        if (branchesOverflow || !PublicationSnapshot.BranchesMatchPolicy(snapshot, branchPolicy))
        {
            issues.Add(new ValidationIssue(
                "git.branches.policy-mismatch",
                "Git branches must exactly match the reviewed branch policy.",
                "branches"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new GitRepositoryReceipt(
                initialCommitId!,
                branchPolicy,
                [.. snapshot.Select(item => item!)],
                finalTreeDigest!))
            : ValidationResult.Failure<GitRepositoryReceipt>(issues);
    }

}

public interface IGitService
{
    Task<GitRepositoryReceipt> BootstrapAsync(
        GitBootstrapRequest request,
        CancellationToken cancellationToken);

    Task<GitRepositoryReceipt> VerifyAsync(
        GitVerificationRequest request,
        CancellationToken cancellationToken);
}

internal static class GitContractValidation
{
    public static List<ValidationIssue> ValidateCommon(
        IWorkspaceFileSystem? workspace,
        GitBranchPolicy branchPolicy,
        string? finalTreeDigest)
    {
        var issues = ValidatePolicyAndDigest(branchPolicy, finalTreeDigest);
        if (workspace is null)
        {
            issues.Add(new ValidationIssue(
                "git.workspace.required",
                "A scoped workspace is required for Git publication.",
                "workspace"));
        }

        return issues;
    }

    public static List<ValidationIssue> ValidatePolicyAndDigest(
        GitBranchPolicy branchPolicy,
        string? finalTreeDigest)
    {
        var issues = new List<ValidationIssue>();
        if (!Enum.IsDefined(branchPolicy))
        {
            issues.Add(new ValidationIssue(
                "git.branch-policy.invalid",
                "The reviewed Git branch policy is not defined.",
                "branchPolicy"));
        }

        if (!ExecutionContractValidation.IsCanonicalDigest(finalTreeDigest))
        {
            issues.Add(new ValidationIssue(
                "git.final-tree-digest.invalid",
                "A canonical finalized project tree digest is required.",
                "finalTreeDigest"));
        }
        return issues;
    }
}
