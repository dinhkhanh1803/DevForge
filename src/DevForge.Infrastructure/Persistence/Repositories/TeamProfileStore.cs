using System.Collections.Immutable;
using DevForge.Application.Contracts.Persistence;
using DevForge.Infrastructure.Persistence.Entities;
using DevForge.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;

namespace DevForge.Infrastructure.Persistence.Repositories;

public sealed class TeamProfileStore : ITeamProfileStore
{
    private readonly DevForgeDbContextFactory _factory;

    public TeamProfileStore(DevForgeDbContextFactory factory) =>
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    public async Task<TeamProfileRecord?> GetAsync(string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = RepositorySupport.NormalizeIdentifier(id, nameof(id));
        await using var context = _factory.CreateDbContext();
        var entity = await context.TeamProfiles.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == normalized, cancellationToken)
            .ConfigureAwait(false);
        return entity is null ? null : MetadataMapper.ToModel(entity);
    }

    public async Task<ImmutableArray<TeamProfileRecord>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var context = _factory.CreateDbContext();
        var entities = await context.TeamProfiles.AsNoTracking()
            .OrderBy(item => item.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. entities.Select(MetadataMapper.ToModel)];
    }

    public Task UpsertAsync(TeamProfileRecord profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var entity = MetadataMapper.ToEntity(profile);
        return RepositorySupport.UpsertAsync<TeamProfileEntity>(
            _factory,
            entity,
            [entity.Id],
            static (current, incoming) =>
            {
                current.Name = incoming.Name;
                current.SchemaVersion = incoming.SchemaVersion;
                current.PolicyJson = incoming.PolicyJson;
                current.UpdatedAtUnixMs = incoming.UpdatedAtUnixMs;
            },
            cancellationToken);
    }

    public Task<bool> RemoveAsync(string id, CancellationToken cancellationToken)
    {
        var normalized = RepositorySupport.NormalizeIdentifier(id, nameof(id));
        return RepositorySupport.RemoveAsync<TeamProfileEntity>(_factory, [normalized], cancellationToken);
    }
}
