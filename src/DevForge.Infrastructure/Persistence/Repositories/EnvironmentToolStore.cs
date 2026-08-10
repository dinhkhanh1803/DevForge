using System.Collections.Immutable;
using DevForge.Application.Contracts.Persistence;
using DevForge.Infrastructure.Persistence.Entities;
using DevForge.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;

namespace DevForge.Infrastructure.Persistence.Repositories;

public sealed class EnvironmentToolStore : IEnvironmentToolStore
{
    private readonly DevForgeDbContextFactory _factory;

    public EnvironmentToolStore(DevForgeDbContextFactory factory) =>
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    public async Task<EnvironmentToolRecord?> GetAsync(string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = RepositorySupport.NormalizeIdentifier(id, nameof(id));
        await using var context = _factory.CreateDbContext();
        var entity = await context.EnvironmentTools.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == normalized, cancellationToken)
            .ConfigureAwait(false);
        return entity is null ? null : MetadataMapper.ToModel(entity);
    }

    public async Task<ImmutableArray<EnvironmentToolRecord>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var context = _factory.CreateDbContext();
        var entities = await context.EnvironmentTools.AsNoTracking()
            .OrderBy(item => item.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. entities.Select(MetadataMapper.ToModel)];
    }

    public Task UpsertAsync(EnvironmentToolRecord tool, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tool);
        var entity = MetadataMapper.ToEntity(tool);
        return RepositorySupport.UpsertAsync<EnvironmentToolEntity>(
            _factory,
            entity,
            [entity.Id],
            static (current, incoming) =>
            {
                current.ExecutablePath = incoming.ExecutablePath;
                current.Version = incoming.Version;
                current.Status = incoming.Status;
                current.ScannedAtUnixMs = incoming.ScannedAtUnixMs;
                current.ExpiresAtUnixMs = incoming.ExpiresAtUnixMs;
            },
            cancellationToken);
    }

    public Task<bool> RemoveAsync(string id, CancellationToken cancellationToken)
    {
        var normalized = RepositorySupport.NormalizeIdentifier(id, nameof(id));
        return RepositorySupport.RemoveAsync<EnvironmentToolEntity>(_factory, [normalized], cancellationToken);
    }
}
