using System.Collections.Immutable;
using DevForge.Domain.Projects;
using DevForge.Domain.Validation;

namespace DevForge.Application.Contracts;

public enum GitPublicationState
{
    NotRequested = 1,
    IntentPersisted = 2,
    RepositoryInitialized = 3,
    Committed = 4,
    Succeeded = 5,
    Failed = 6,
}

public enum GitHubPublicationState
{
    NotRequested = 1,
    IntentPersisted = 2,
    RemoteCreated = 3,
    Succeeded = 4,
    Failed = 5,
}

public enum PublicationReceiptState
{
    NotRequested = 1,
    IntentPersisted = 2,
    Succeeded = 3,
    Failed = 4,
}

public sealed class PublicationSnapshot
{
    private PublicationSnapshot(
        GitPublicationState gitState,
        GitHubPublicationState gitHubState,
        PublicationReceiptState receiptState,
        string? finalTreeDigest,
        string? initialCommitId,
        ImmutableArray<string> branches,
        GitHubRepositoryIdentity? repositoryIdentity,
        bool isPrivate,
        string? ownershipNonce,
        string? repositoryUrl,
        WorkspaceRelativePath? receiptPath,
        string? receiptBodyDigest)
    {
        GitState = gitState;
        GitHubState = gitHubState;
        ReceiptState = receiptState;
        FinalTreeDigest = finalTreeDigest;
        InitialCommitId = initialCommitId;
        Branches = branches;
        RepositoryIdentity = repositoryIdentity;
        IsPrivate = isPrivate;
        OwnershipNonce = ownershipNonce;
        RepositoryUrl = repositoryUrl;
        ReceiptPath = receiptPath;
        ReceiptBodyDigest = receiptBodyDigest;
    }

    public GitPublicationState GitState { get; }

    public GitHubPublicationState GitHubState { get; }

    public PublicationReceiptState ReceiptState { get; }

    public string? FinalTreeDigest { get; }

    public string? InitialCommitId { get; }

    public ImmutableArray<string> Branches { get; }

    public GitHubRepositoryIdentity? RepositoryIdentity { get; }

    public bool IsPrivate { get; }

    public string? OwnershipNonce { get; }

    public string? RepositoryUrl { get; }

    public WorkspaceRelativePath? ReceiptPath { get; }

    public string? ReceiptReference => ReceiptPath?.Value;

    public string? ReceiptBodyDigest { get; }

    public static ValidationResult<PublicationSnapshot> CreateNotRequested(string? finalTreeDigest) =>
        Create(
            GitPublicationState.NotRequested,
            GitHubPublicationState.NotRequested,
            PublicationReceiptState.NotRequested,
            finalTreeDigest,
            null,
            [],
            null,
            true,
            null,
            null,
            null,
            null);

    public static ValidationResult<PublicationSnapshot> Create(
        GitPublicationState gitState,
        GitHubPublicationState gitHubState,
        PublicationReceiptState receiptState,
        string? finalTreeDigest,
        string? initialCommitId,
        IEnumerable<string?>? branches,
        GitHubRepositoryIdentity? repositoryIdentity,
        bool isPrivate,
        string? ownershipNonce,
        string? repositoryUrl,
        WorkspaceRelativePath? receiptPath,
        string? receiptBodyDigest)
    {
        var snapshot = SnapshotBranches(branches, out var branchesOverflow);
        var issues = new List<ValidationIssue>();
        ValidateEnum(gitState, "publication.git-state.invalid", "gitState", issues);
        ValidateEnum(gitHubState, "publication.github-state.invalid", "gitHubState", issues);
        ValidateEnum(receiptState, "publication.receipt-state.invalid", "receiptState", issues);
        if (!ExecutionContractValidation.IsCanonicalDigest(finalTreeDigest))
        {
            issues.Add(new ValidationIssue(
                "publication.final-tree-digest.invalid",
                "A canonical final-tree digest is required.",
                "finalTreeDigest"));
        }

        if (initialCommitId is not null && !IsObjectId(initialCommitId))
        {
            issues.Add(new ValidationIssue(
                "publication.commit-id.invalid",
                "A canonical Git object identifier is required.",
                "initialCommitId"));
        }

        if ((initialCommitId is null) != (snapshot.Length == 0))
        {
            issues.Add(new ValidationIssue(
                "publication.git-evidence.unpaired",
                "Commit and branch evidence must be present or absent together.",
                "initialCommitId"));
        }

        if (branchesOverflow)
        {
            issues.Add(new ValidationIssue(
                "publication.branches.too-many",
                "Publication branch evidence exceeds the closed MVP policy.",
                "branches"));
        }

        var validBranches = !branchesOverflow
            && snapshot.All(item => item is "main" or "develop")
            && snapshot.Distinct(StringComparer.Ordinal).Count() == snapshot.Length
            && (snapshot.Length == 0
                || snapshot.SequenceEqual(["main"], StringComparer.Ordinal)
                || snapshot.SequenceEqual(["main", "develop"], StringComparer.Ordinal));
        if (!validBranches)
        {
            issues.Add(new ValidationIssue(
                "publication.branches.invalid",
                "Publication branches must be the reviewed main or main and develop policy.",
                "branches"));
        }

        var gitHasCommit = gitState is GitPublicationState.Committed or GitPublicationState.Succeeded;
        if (gitHasCommit && (initialCommitId is null || snapshot.Length == 0))
        {
            issues.Add(new ValidationIssue(
                "publication.git-evidence.incomplete",
                "Committed Git publication requires commit and branch evidence.",
                "gitState"));
        }
        else if (gitState is GitPublicationState.NotRequested
                or GitPublicationState.IntentPersisted
                or GitPublicationState.RepositoryInitialized
            && (initialCommitId is not null || snapshot.Length != 0))
        {
            issues.Add(new ValidationIssue(
                "publication.git-evidence.out-of-phase",
                "Commit and branch evidence is not allowed before the bootstrap commit.",
                "gitState"));
        }

        var githubRequested = gitHubState != GitHubPublicationState.NotRequested;
        if (githubRequested)
        {
            if (repositoryIdentity is null)
            {
                issues.Add(new ValidationIssue(
                    "publication.github-identity.required",
                    "GitHub publication requires the reviewed repository identity.",
                    "repositoryIdentity"));
            }

            if (gitState != GitPublicationState.Succeeded)
            {
                issues.Add(new ValidationIssue(
                    "publication.github.before-git",
                    "GitHub publication requires verified successful Git evidence.",
                    "gitHubState"));
            }

            if (!IsOwnershipNonce(ownershipNonce))
            {
                issues.Add(new ValidationIssue(
                    "publication.ownership-nonce.invalid",
                    "GitHub publication requires a canonical ownership nonce.",
                    "ownershipNonce"));
            }
        }
        else if (repositoryIdentity is not null || ownershipNonce is not null || repositoryUrl is not null)
        {
            issues.Add(new ValidationIssue(
                "publication.github-evidence.not-requested",
                "GitHub evidence is not allowed when publication was not requested.",
                "gitHubState"));
        }

        if (gitHubState == GitHubPublicationState.Succeeded
            && (gitState != GitPublicationState.Succeeded
                || repositoryIdentity is null
                || !StringComparer.Ordinal.Equals(repositoryUrl, repositoryIdentity.HttpsWebUrl)))
        {
            issues.Add(new ValidationIssue(
                "publication.github-evidence.incomplete",
                "Successful GitHub publication requires matching Git and HTTPS repository evidence.",
                "repositoryUrl"));
        }
        else if (gitHubState != GitHubPublicationState.Succeeded && repositoryUrl is not null)
        {
            issues.Add(new ValidationIssue(
                "publication.github-url.out-of-phase",
                "A repository URL is allowed only after verified GitHub publication.",
                "repositoryUrl"));
        }

        var receiptRequested = receiptState != PublicationReceiptState.NotRequested;
        if (receiptRequested
            && (receiptPath is null || !ExecutionContractValidation.IsCanonicalDigest(receiptBodyDigest)))
        {
            issues.Add(new ValidationIssue(
                "publication.receipt-evidence.incomplete",
                "Publication receipt intent requires a guarded path and canonical body digest.",
                "receiptState"));
        }
        else if (!receiptRequested && (receiptPath is not null || receiptBodyDigest is not null))
        {
            issues.Add(new ValidationIssue(
                "publication.receipt-evidence.not-requested",
                "Receipt evidence is not allowed when a receipt was not requested.",
                "receiptState"));
        }

        if (receiptState == PublicationReceiptState.Succeeded
            && (gitState != GitPublicationState.Succeeded
                || gitHubState is not (GitHubPublicationState.NotRequested or GitHubPublicationState.Succeeded)))
        {
            issues.Add(new ValidationIssue(
                "publication.receipt.before-completion",
                "A successful receipt requires completed Git and reviewed GitHub publication.",
                "receiptState"));
        }
        else if (receiptRequested
            && (gitState != GitPublicationState.Succeeded
                || gitHubState is not (GitHubPublicationState.NotRequested
                    or GitHubPublicationState.Succeeded)))
        {
            issues.Add(new ValidationIssue(
                "publication.receipt.before-completion",
                "Receipt persistence requires completed Git and reviewed GitHub publication.",
                "receiptState"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new PublicationSnapshot(
                gitState,
                gitHubState,
                receiptState,
                finalTreeDigest,
                initialCommitId,
                [.. snapshot.Select(item => item!)],
                repositoryIdentity,
                isPrivate,
                ownershipNonce,
                repositoryUrl,
                receiptPath,
                receiptBodyDigest))
            : ValidationResult.Failure<PublicationSnapshot>(issues);
    }

    internal static PublicationSnapshot LegacyNotRequested() => new(
        GitPublicationState.NotRequested,
        GitHubPublicationState.NotRequested,
        PublicationReceiptState.NotRequested,
        null,
        null,
        [],
        null,
        true,
        null,
        null,
        null,
        null);

    private static void ValidateEnum<T>(
        T value,
        string code,
        string location,
        List<ValidationIssue> issues)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            issues.Add(new ValidationIssue(code, "The publication state is not defined.", location));
        }
    }

    internal static bool IsObjectId(string? value) =>
        value is not null
        &&
        value.Length is 40 or 64
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    internal static bool IsOwnershipNonce(string? value) =>
        value is { Length: 32 }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    internal static bool BranchesMatchPolicy(
        IEnumerable<string?> branches,
        GitBranchPolicy branchPolicy)
    {
        var expected = branchPolicy switch
        {
            GitBranchPolicy.Main => new[] { "main" },
            GitBranchPolicy.MainAndDevelop => ["main", "develop"],
            _ => [],
        };
        return expected.Length != 0 && branches.SequenceEqual(expected, StringComparer.Ordinal);
    }

    internal static ImmutableArray<string?> SnapshotBranches(
        IEnumerable<string?>? branches,
        out bool overflow)
    {
        overflow = false;
        if (branches is null)
        {
            return [];
        }

        var builder = ImmutableArray.CreateBuilder<string?>(3);
        using var enumerator = branches.GetEnumerator();
        while (builder.Count < 3 && enumerator.MoveNext())
        {
            builder.Add(enumerator.Current);
        }

        overflow = builder.Count > 2;
        return builder.ToImmutable();
    }
}
