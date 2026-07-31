using System.Collections.Immutable;

namespace DevForge.Domain.Projects;

public sealed class TeamProfile
{
    private TeamProfile(string id, string name, IEnumerable<KeyValuePair<string, string>> standards)
    {
        Id = id;
        Name = name;
        Standards = standards.ToImmutableDictionary(StringComparer.Ordinal);
    }

    public string Id { get; }

    public string Name { get; }

    public ImmutableDictionary<string, string> Standards { get; }

    public static TeamProfile Create(
        string id,
        string name,
        IEnumerable<KeyValuePair<string, string>> standards)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(standards);

        return new TeamProfile(id.Trim(), name.Trim(), standards);
    }
}

public sealed record GitOptions(
    bool InitializeRepository = true,
    string PrimaryBranch = "main",
    bool UseDevelopBranch = false,
    bool PublishToGitHub = false,
    bool IsPrivate = true);

public sealed record CompletionOptions(
    bool WriteGenerationReport = true,
    bool WriteHandoffDocument = true,
    bool OpenIde = false,
    string? IdeId = null);
