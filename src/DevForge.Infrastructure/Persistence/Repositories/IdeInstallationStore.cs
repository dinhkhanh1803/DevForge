using System.Collections.Immutable;
using DevForge.Application.Contracts.Persistence;
using DevForge.Infrastructure.Persistence.Entities;
using DevForge.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;

namespace DevForge.Infrastructure.Persistence.Repositories;

public sealed class IdeInstallationStore : IIdeInstallationStore
{
    private readonly DevForgeDbContextFactory _factory;

    public IdeInstallationStore(DevForgeDbContextFactory factory) =>
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    public async Task<IdeInstallationRecord?> GetAsync(string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = RepositorySupport.NormalizeIdentifier(id, nameof(id));
        await using var context = _factory.CreateDbContext();
        var entity = await context.IdeInstallations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == normalized, cancellationToken)
            .ConfigureAwait(false);
        return entity is null ? null : MetadataMapper.ToModel(entity);
    }

    public async Task<ImmutableArray<IdeInstallationRecord>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var context = _factory.CreateDbContext();
        var entities = await context.IdeInstallations.AsNoTracking()
            .OrderBy(item => item.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. entities.Select(MetadataMapper.ToModel)];
    }

    public Task UpsertAsync(IdeInstallationRecord installation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(installation);
        var entity = MetadataMapper.ToEntity(installation);
        return RepositorySupport.UpsertAsync<IdeInstallationEntity>(
            _factory,
            entity,
            [entity.Id],
            static (current, incoming) =>
            {
                current.Kind = incoming.Kind;
                current.ExecutablePath = incoming.ExecutablePath;
                current.Version = incoming.Version;
                current.ValidationState = incoming.ValidationState;
                current.ScannedAtUnixMs = incoming.ScannedAtUnixMs;
            },
            cancellationToken);
    }

    public Task<bool> RemoveAsync(string id, CancellationToken cancellationToken)
    {
        var normalized = RepositorySupport.NormalizeIdentifier(id, nameof(id));
        return RepositorySupport.RemoveAsync<IdeInstallationEntity>(_factory, [normalized], cancellationToken);
    }
}
