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
                var normalizedName = standard.Key?.Trim();
                if (normalizedName is not null && !names.Add(normalizedName))
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
            ? ValidationResult.Success(
                new TeamProfile(
                    id!.Trim(),
                    name!.Trim(),
                    standardsSnapshot.Select(standard => KeyValuePair.Create(standard.Key.Trim(), standard.Value))))
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
        var issues = new List<ValidationIssue>();
        if (string.IsNullOrWhiteSpace(primaryBranch))
        {
            issues.Add(new ValidationIssue(
                    "git.primary-branch.required",
                    "The primary branch is required.",
                    "primaryBranch"));
        }
        else if (!string.Equals(primaryBranch.Trim(), "main", StringComparison.Ordinal))
        {
            issues.Add(new ValidationIssue(
                    "git.primary-branch.unsupported",
                    "The MVP primary branch must be main.",
                    "primaryBranch"));
        }

        if (!initializeRepository && useDevelopBranch)
        {
            issues.Add(
                new ValidationIssue(
                    "git.develop.requires-initialization",
                    "The develop branch requires repository initialization.",
                    "useDevelopBranch"));
        }

        if (!initializeRepository && publishToGitHub)
        {
            issues.Add(
                new ValidationIssue(
                    "git.publish.requires-initialization",
                    "GitHub publishing requires repository initialization.",
                    "publishToGitHub"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(
                new GitOptions(
                    initializeRepository,
                    useDevelopBranch,
                    publishToGitHub,
                    isPrivate))
            : ValidationResult.Failure<GitOptions>(issues);
    }
}

public sealed class CompletionOptions
{
    private CompletionOptions(
        bool writeGenerationReport,
        bool writeHandoffDocument,
        bool openIde,
        string? ideId)
    {
        WriteGenerationReport = writeGenerationReport;
        WriteHandoffDocument = writeHandoffDocument;
        OpenIde = openIde;
        IdeId = ideId;
    }

    public bool WriteGenerationReport { get; }

    public bool WriteHandoffDocument { get; }

    public bool OpenIde { get; }

    public string? IdeId { get; }

    public static ValidationResult<CompletionOptions> Create(
        bool writeGenerationReport = true,
        bool writeHandoffDocument = true,
        bool openIde = false,
        string? ideId = null)
    {
        if (openIde && string.IsNullOrWhiteSpace(ideId))
        {
            return ValidationResult.Failure<CompletionOptions>(
            [
                new ValidationIssue(
                    "completion.ide.required",
                    "An IDE identifier is required when opening an IDE.",
                    "ideId"),
            ]);
        }

        if (!openIde && ideId is not null)
        {
            return ValidationResult.Failure<CompletionOptions>(
            [
                new ValidationIssue(
                    "completion.ide.unexpected",
                    "An IDE identifier cannot be selected when IDE launch is disabled.",
                    "ideId"),
            ]);
        }

        return ValidationResult.Success(
            new CompletionOptions(
                writeGenerationReport,
                writeHandoffDocument,
                openIde,
                ideId?.Trim()));
    }
}
