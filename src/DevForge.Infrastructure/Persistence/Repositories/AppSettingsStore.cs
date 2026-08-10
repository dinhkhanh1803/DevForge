using System.Collections.Immutable;
using DevForge.Application.Contracts.Persistence;
using DevForge.Infrastructure.Persistence.Entities;
using DevForge.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;

namespace DevForge.Infrastructure.Persistence.Repositories;

public sealed class AppSettingsStore : IAppSettingsStore
{
    private readonly DevForgeDbContextFactory _factory;

    public AppSettingsStore(DevForgeDbContextFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public async Task<AppSetting?> GetAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = RepositorySupport.NormalizeSettingKey(key, nameof(key));
        await using var context = _factory.CreateDbContext();
        var entity = await context.AppSettings.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Key == normalized, cancellationToken)
            .ConfigureAwait(false);
        return entity is null ? null : MetadataMapper.ToModel(entity);
    }

    public async Task<ImmutableArray<AppSetting>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var context = _factory.CreateDbContext();
        var entities = await context.AppSettings.AsNoTracking()
            .OrderBy(item => item.Key)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. entities.Select(MetadataMapper.ToModel)];
    }

    public Task UpsertAsync(AppSetting setting, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(setting);
        var entity = MetadataMapper.ToEntity(setting);
        return RepositorySupport.UpsertAsync<AppSettingEntity>(
            _factory,
            entity,
            [entity.Key],
            static (current, incoming) =>
            {
                if (incoming.UpdatedAtUnixMs < current.UpdatedAtUnixMs
                    || incoming.UpdatedAtUnixMs == current.UpdatedAtUnixMs
                    && CompareCanonical(incoming, current) <= 0)
                {
                    return;
                }

                current.ValueKind = incoming.ValueKind;
                current.SerializedValue = incoming.SerializedValue;
                current.UpdatedAtUnixMs = incoming.UpdatedAtUnixMs;
            },
            cancellationToken);
    }

    public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken)
    {
        var normalized = RepositorySupport.NormalizeSettingKey(key, nameof(key));
        return RepositorySupport.RemoveAsync<AppSettingEntity>(_factory, [normalized], cancellationToken);
    }

    private static int CompareCanonical(AppSettingEntity left, AppSettingEntity right)
    {
        var kindComparison = string.CompareOrdinal(left.ValueKind, right.ValueKind);
        return kindComparison != 0
            ? kindComparison
            : string.CompareOrdinal(left.SerializedValue, right.SerializedValue);
    }
}
