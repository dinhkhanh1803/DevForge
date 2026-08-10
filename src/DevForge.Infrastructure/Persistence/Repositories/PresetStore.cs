using System.Collections.Immutable;
using DevForge.Application.Contracts.Persistence;
using DevForge.Infrastructure.Persistence.Entities;
using DevForge.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;

namespace DevForge.Infrastructure.Persistence.Repositories;

public sealed class PresetStore : IPresetStore
{
    private readonly DevForgeDbContextFactory _factory;

    public PresetStore(DevForgeDbContextFactory factory) =>
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    public async Task<PresetRecord?> GetAsync(string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = RepositorySupport.NormalizeIdentifier(id, nameof(id));
        await using var context = _factory.CreateDbContext();
        var entity = await context.Presets.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == normalized, cancellationToken)
            .ConfigureAwait(false);
        return entity is null ? null : MetadataMapper.ToModel(entity);
    }

    public async Task<ImmutableArray<PresetRecord>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var context = _factory.CreateDbContext();
        var entities = await context.Presets.AsNoTracking()
            .OrderBy(item => item.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. entities.Select(MetadataMapper.ToModel)];
    }

    public Task UpsertAsync(PresetRecord preset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preset);
        var entity = MetadataMapper.ToEntity(preset);
        return RepositorySupport.UpsertAsync<PresetEntity>(
            _factory,
            entity,
            [entity.Id],
            static (current, incoming) =>
            {
                current.Name = incoming.Name;
                current.SchemaVersion = incoming.SchemaVersion;
                current.RecipeJson = incoming.RecipeJson;
                current.UpdatedAtUnixMs = incoming.UpdatedAtUnixMs;
            },
            cancellationToken);
    }

    public Task<bool> RemoveAsync(string id, CancellationToken cancellationToken)
    {
        var normalized = RepositorySupport.NormalizeIdentifier(id, nameof(id));
        return RepositorySupport.RemoveAsync<PresetEntity>(_factory, [normalized], cancellationToken);
    }
}
