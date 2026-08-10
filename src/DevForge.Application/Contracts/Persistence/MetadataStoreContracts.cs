using System.Collections.Immutable;

namespace DevForge.Application.Contracts.Persistence;

public interface IIdeInstallationStore
{
    Task<IdeInstallationRecord?> GetAsync(string id, CancellationToken cancellationToken);

    Task<ImmutableArray<IdeInstallationRecord>> ListAsync(CancellationToken cancellationToken);

    Task UpsertAsync(IdeInstallationRecord installation, CancellationToken cancellationToken);

    Task<bool> RemoveAsync(string id, CancellationToken cancellationToken);
}

public interface IEnvironmentToolStore
{
    Task<EnvironmentToolRecord?> GetAsync(string id, CancellationToken cancellationToken);

    Task<ImmutableArray<EnvironmentToolRecord>> ListAsync(CancellationToken cancellationToken);

    Task UpsertAsync(EnvironmentToolRecord tool, CancellationToken cancellationToken);

    Task<bool> RemoveAsync(string id, CancellationToken cancellationToken);
}

public interface IBlueprintMetadataStore
{
    Task<BlueprintMetadataRecord?> GetAsync(
        string id,
        string version,
        CancellationToken cancellationToken);

    Task<ImmutableArray<BlueprintMetadataRecord>> ListAsync(CancellationToken cancellationToken);

    Task UpsertAsync(BlueprintMetadataRecord blueprint, CancellationToken cancellationToken);

    Task<bool> RemoveAsync(string id, string version, CancellationToken cancellationToken);
}

public interface ITeamProfileStore
{
    Task<TeamProfileRecord?> GetAsync(string id, CancellationToken cancellationToken);

    Task<ImmutableArray<TeamProfileRecord>> ListAsync(CancellationToken cancellationToken);

    Task UpsertAsync(TeamProfileRecord profile, CancellationToken cancellationToken);

    Task<bool> RemoveAsync(string id, CancellationToken cancellationToken);
}

public interface IPresetStore
{
    Task<PresetRecord?> GetAsync(string id, CancellationToken cancellationToken);

    Task<ImmutableArray<PresetRecord>> ListAsync(CancellationToken cancellationToken);

    Task UpsertAsync(PresetRecord preset, CancellationToken cancellationToken);

    Task<bool> RemoveAsync(string id, CancellationToken cancellationToken);
}

public interface IRecentProjectStore
{
    Task<RecentProjectRecord?> GetAsync(string projectPath, CancellationToken cancellationToken);

    Task<ImmutableArray<RecentProjectRecord>> ListAsync(CancellationToken cancellationToken);

    Task UpsertAsync(RecentProjectRecord project, CancellationToken cancellationToken);

    Task<bool> RemoveAsync(string projectPath, CancellationToken cancellationToken);
}
