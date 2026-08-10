using System.Collections.Immutable;
using DevForge.Application.Contracts.Persistence;
using DevForge.Infrastructure.Persistence.Entities;
using DevForge.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;

namespace DevForge.Infrastructure.Persistence.Repositories;

public sealed class BlueprintMetadataStore : IBlueprintMetadataStore
{
    private readonly DevForgeDbContextFactory _factory;

    public BlueprintMetadataStore(DevForgeDbContextFactory factory) =>
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    public async Task<BlueprintMetadataRecord?> GetAsync(
        string id,
        string version,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = RepositorySupport.NormalizeBlueprintKey(id, version, nameof(id), nameof(version));
        await using var context = _factory.CreateDbContext();
        var entity = await context.Blueprints.AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == key.Id && item.Version == key.Version,
                cancellationToken)
            .ConfigureAwait(false);
        return entity is null ? null : MetadataMapper.ToModel(entity);
    }

    public async Task<ImmutableArray<BlueprintMetadataRecord>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var context = _factory.CreateDbContext();
        var entities = await context.Blueprints.AsNoTracking()
            .OrderBy(item => item.Id)
            .ThenBy(item => item.Version)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. entities.Select(MetadataMapper.ToModel)];
    }

    public Task UpsertAsync(BlueprintMetadataRecord blueprint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        var entity = MetadataMapper.ToEntity(blueprint);
        return RepositorySupport.UpsertAsync<BlueprintEntity>(
            _factory,
            entity,
            [entity.Id, entity.Version],
            static (current, incoming) =>
            {
                current.Source = incoming.Source;
                current.Trust = incoming.Trust;
                current.Checksum = incoming.Checksum;
                current.IsDisabled = incoming.IsDisabled;
                current.DiscoveredAtUnixMs = incoming.DiscoveredAtUnixMs;
            },
            cancellationToken);
    }

    public Task<bool> RemoveAsync(string id, string version, CancellationToken cancellationToken)
    {
        var key = RepositorySupport.NormalizeBlueprintKey(id, version, nameof(id), nameof(version));
        return RepositorySupport.RemoveAsync<BlueprintEntity>(
            _factory,
            [key.Id, key.Version],
            cancellationToken);
    }
}
