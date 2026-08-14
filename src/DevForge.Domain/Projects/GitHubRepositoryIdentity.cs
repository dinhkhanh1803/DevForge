using DevForge.Domain.Validation;

namespace DevForge.Domain.Projects;

public sealed class GitHubRepositoryIdentity : IEquatable<GitHubRepositoryIdentity>
{
    private GitHubRepositoryIdentity(string account, string repositoryName)
    {
        Host = "github.com";
        Account = account;
        RepositoryName = repositoryName;
    }

    public string Host { get; }

    public string Account { get; }

    public string RepositoryName { get; }

    public string HttpsRemoteUrl => $"https://github.com/{Account}/{RepositoryName}.git";

    public string HttpsWebUrl => $"https://github.com/{Account}/{RepositoryName}";

    public static ValidationResult<GitHubRepositoryIdentity> Create(
        string? account,
        string? repositoryName)
    {
        var issues = new List<ValidationIssue>();
        var normalizedAccount = account?.Trim().ToLowerInvariant();
        var normalizedRepository = repositoryName?.Trim().ToLowerInvariant();
        if (!IsValidAccount(normalizedAccount))
        {
            issues.Add(new ValidationIssue(
                "github.account.invalid",
                "A canonical bounded GitHub personal account is required.",
                "account"));
        }

        if (!IsValidRepositoryName(normalizedRepository))
        {
            issues.Add(new ValidationIssue(
                "github.repository-name.invalid",
                "A canonical bounded GitHub repository name is required.",
                "repositoryName"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(
                new GitHubRepositoryIdentity(normalizedAccount!, normalizedRepository!))
            : ValidationResult.Failure<GitHubRepositoryIdentity>(issues);
    }

    public bool Equals(GitHubRepositoryIdentity? other) =>
        other is not null
        && StringComparer.Ordinal.Equals(Account, other.Account)
        && StringComparer.Ordinal.Equals(RepositoryName, other.RepositoryName);

    public override bool Equals(object? obj) => Equals(obj as GitHubRepositoryIdentity);

    public override int GetHashCode() => HashCode.Combine(
        StringComparer.Ordinal.GetHashCode(Account),
        StringComparer.Ordinal.GetHashCode(RepositoryName));

    internal static bool IsValidAccount(string? value)
    {
        if (value is null
            || value.Length is < 1 or > 39
            || !char.IsAsciiLetterOrDigit(value[0])
            || !char.IsAsciiLetterOrDigit(value[^1])
            || value.Contains("--", StringComparison.Ordinal))
        {
            return false;
        }

        return value.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');
    }

    internal static bool IsValidRepositoryName(string? value)
    {
        if (value is null
            || value.Length is < 1 or > 100
            || !char.IsAsciiLetterOrDigit(value[0])
            || value is "." or ".."
            || value.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return value.All(character => char.IsAsciiLetterOrDigit(character)
            || character is '-' or '_' or '.');
    }
}
