using System.Collections.Immutable;
using DevForge.Domain.Validation;

namespace DevForge.Domain.Projects;

public sealed class TeamProfile
{
    private TeamProfile(string id, string name, IEnumerable<KeyValuePair<string, string?>> standards)
    {
        Id = id;
        Name = name;
        Standards = standards.ToImmutableDictionary(
            standard => standard.Key,
            standard => standard.Value!,
            StringComparer.Ordinal);
    }

    public string Id { get; }

    public string Name { get; }

    public ImmutableDictionary<string, string> Standards { get; }

    public static ValidationResult<TeamProfile> Create(
        string? id,
        string? name,
        IEnumerable<KeyValuePair<string, string?>>? standards)
    {
        var issues = new List<ValidationIssue>();
        if (string.IsNullOrWhiteSpace(id))
        {
            issues.Add(new ValidationIssue("team.id.required", "Team profile identifier is required.", "id"));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            issues.Add(new ValidationIssue("team.name.required", "Team profile name is required.", "name"));
        }

        var standardsSnapshot = standards?.ToImmutableArray() ?? [];
        if (standards is null)
        {
            issues.Add(
                new ValidationIssue(
                    "team.standards.required",
                    "Team profile standards are required.",
                    "standards"));
        }
        else
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < standardsSnapshot.Length; index++)
            {
                var standard = standardsSnapshot[index];
                if (string.IsNullOrWhiteSpace(standard.Key))
                {
                    issues.Add(
                        new ValidationIssue(
                            "team.standard.name.required",
                            "A team standard name is required.",
                            $"standards[{index}].name"));
                }
                else if (!names.Add(standard.Key))
                {
                    issues.Add(
                        new ValidationIssue(
                            "team.standard.name.duplicate",
                            "Team standard names must be unique.",
                            $"standards[{index}].name"));
                }

                if (standard.Value is null)
                {
                    issues.Add(
                        new ValidationIssue(
                            "team.standard.value.required",
                            "A team standard value is required.",
                            $"standards[{index}].value"));
                }
            }
        }

        return issues.Count == 0
            ? ValidationResult.Success(new TeamProfile(id!.Trim(), name!.Trim(), standardsSnapshot))
            : ValidationResult.Failure<TeamProfile>(issues);
    }
}

public enum GitBranchPolicy
{
    Main,
    MainAndDevelop,
}

public sealed class GitOptions
{
    private GitOptions(
        bool initializeRepository,
        bool useDevelopBranch,
        bool publishToGitHub,
        bool isPrivate)
    {
        InitializeRepository = initializeRepository;
        PrimaryBranch = "main";
        UseDevelopBranch = useDevelopBranch;
        PublishToGitHub = publishToGitHub;
        IsPrivate = isPrivate;
        BranchPolicy = useDevelopBranch ? GitBranchPolicy.MainAndDevelop : GitBranchPolicy.Main;
    }

    public bool InitializeRepository { get; }

    public string PrimaryBranch { get; }

    public bool UseDevelopBranch { get; }

    public bool PublishToGitHub { get; }

    public bool IsPrivate { get; }

    public GitBranchPolicy BranchPolicy { get; }

    public static ValidationResult<GitOptions> Create(
        bool initializeRepository = true,
        string? primaryBranch = "main",
        bool useDevelopBranch = false,
        bool publishToGitHub = false,
        bool isPrivate = true)
    {
        if (string.IsNullOrWhiteSpace(primaryBranch))
        {
            return ValidationResult.Failure<GitOptions>(
            [
                new ValidationIssue(
                    "git.primary-branch.required",
                    "The primary branch is required.",
                    "primaryBranch"),
            ]);
        }

        if (!string.Equals(primaryBranch.Trim(), "main", StringComparison.Ordinal))
        {
            return ValidationResult.Failure<GitOptions>(
            [
                new ValidationIssue(
                    "git.primary-branch.unsupported",
                    "The MVP primary branch must be main.",
                    "primaryBranch"),
            ]);
        }

        return ValidationResult.Success(
            new GitOptions(
                initializeRepository,
                useDevelopBranch,
                publishToGitHub,
                isPrivate));
    }
}

public sealed record CompletionOptions(
    bool WriteGenerationReport = true,
    bool WriteHandoffDocument = true,
    bool OpenIde = false,
    string? IdeId = null);
