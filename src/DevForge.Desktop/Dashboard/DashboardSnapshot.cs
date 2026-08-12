using System.Collections.Immutable;
using DevForge.Application.Contracts;
using DevForge.Desktop.EnvironmentDoctor;
using DevForge.Domain.Runs;

namespace DevForge.Desktop.Dashboard;

public sealed record RecentProjectItem(
    string ProjectPath,
    string DisplayName,
    string? IdeId,
    DateTimeOffset LastOpenedAt,
    ProjectLocationStatus LocationStatus);

public sealed record SavedPresetItem(string Id, string Name, DateTimeOffset UpdatedAt);

public sealed record ActionNeededRunItem(string RunId, string RecipeId, RunStatus Status);

public sealed record DashboardSnapshot(
    ImmutableArray<RecentProjectItem> RecentProjects,
    ImmutableArray<SavedPresetItem> SavedPresets,
    ImmutableArray<ActionNeededRunItem> ActionNeededRuns,
    EnvironmentHealthSnapshot EnvironmentHealth)
{
    public bool HasNoRecentProjects => RecentProjects.IsEmpty;

    public bool HasNoSavedPresets => SavedPresets.IsEmpty;

    public bool HasNoActionNeededRuns => ActionNeededRuns.IsEmpty;
}
