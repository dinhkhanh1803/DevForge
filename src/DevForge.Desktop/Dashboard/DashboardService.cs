using System.Collections.Immutable;
using DevForge.Application.Contracts;
using DevForge.Application.Contracts.Persistence;
using DevForge.Desktop.EnvironmentDoctor;
using DevForge.Domain.Runs;

namespace DevForge.Desktop.Dashboard;

public interface IDashboardService
{
    Task<DashboardSnapshot> LoadAsync(CancellationToken cancellationToken);
}

public sealed class DashboardService : IDashboardService
{
    private const int MaximumItemsPerSection = 20;
    private readonly IRecentProjectStore _recentProjectStore;
    private readonly IPresetStore _presetStore;
    private readonly IRunCheckpointStore _checkpointStore;
    private readonly IProjectLocationProbe _locationProbe;
    private readonly IEnvironmentDoctorService _environmentDoctorService;

    public DashboardService(
        IRecentProjectStore recentProjectStore,
        IPresetStore presetStore,
        IRunCheckpointStore checkpointStore,
        IProjectLocationProbe locationProbe,
        IEnvironmentDoctorService environmentDoctorService)
    {
        _recentProjectStore = recentProjectStore ?? throw new ArgumentNullException(nameof(recentProjectStore));
        _presetStore = presetStore ?? throw new ArgumentNullException(nameof(presetStore));
        _checkpointStore = checkpointStore ?? throw new ArgumentNullException(nameof(checkpointStore));
        _locationProbe = locationProbe ?? throw new ArgumentNullException(nameof(locationProbe));
        _environmentDoctorService = environmentDoctorService
            ?? throw new ArgumentNullException(nameof(environmentDoctorService));
    }

    public async Task<DashboardSnapshot> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var recentRecords = await _recentProjectStore.ListAsync(cancellationToken).ConfigureAwait(false);
        var recent = ImmutableArray.CreateBuilder<RecentProjectItem>();
        foreach (var record in recentRecords.Take(MaximumItemsPerSection))
        {
            cancellationToken.ThrowIfCancellationRequested();
            recent.Add(new RecentProjectItem(
                record.ProjectPath,
                record.DisplayName,
                record.IdeId,
                record.LastOpenedAt,
                await _locationProbe.InspectAsync(record.ProjectPath, cancellationToken)
                    .ConfigureAwait(false)));
        }

        var presets = await _presetStore.ListAsync(cancellationToken).ConfigureAwait(false);
        var checkpoints = await _checkpointStore.ListAsync(cancellationToken).ConfigureAwait(false);
        var environment = await _environmentDoctorService.LoadAsync(
            forceRefresh: false,
            cancellationToken).ConfigureAwait(false);

        return new DashboardSnapshot(
            recent.ToImmutable(),
            [.. presets.Take(MaximumItemsPerSection).Select(
                item => new SavedPresetItem(item.Id, item.Name, item.UpdatedAt))],
            [.. checkpoints
                .Where(item => IsActionNeeded(item.Run.Status))
                .Take(MaximumItemsPerSection)
                .Select(item => new ActionNeededRunItem(
                    item.Run.Id,
                    item.Run.RecipeId,
                    item.Run.Status))],
            environment);
    }

    private static bool IsActionNeeded(RunStatus status)
    {
        return status is RunStatus.PreflightFailed
            or RunStatus.ValidationFailed
            or RunStatus.PublishPending
            or RunStatus.Failed;
    }
}
