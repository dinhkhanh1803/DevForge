using System.Collections.Immutable;
using DevForge.Application.Contracts.Persistence;
using DevForge.Infrastructure.Persistence.Entities;
using DevForge.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;

namespace DevForge.Infrastructure.Persistence.Repositories;

public sealed class RecentProjectStore : IRecentProjectStore
{
    private readonly DevForgeDbContextFactory _factory;

    public RecentProjectStore(DevForgeDbContextFactory factory) =>
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    public async Task<RecentProjectRecord?> GetAsync(
        string projectPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = RepositorySupport.NormalizeProjectPath(projectPath, nameof(projectPath));
        await using var context = _factory.CreateDbContext();
        var entity = await context.RecentProjects.AsNoTracking()
            .SingleOrDefaultAsync(item => item.ProjectPath == normalized, cancellationToken)
            .ConfigureAwait(false);
        return entity is null ? null : MetadataMapper.ToModel(entity);
    }

    public async Task<ImmutableArray<RecentProjectRecord>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var context = _factory.CreateDbContext();
        var entities = await context.RecentProjects.AsNoTracking()
            .OrderByDescending(item => item.LastOpenedAtUnixMs)
            .ThenBy(item => item.ProjectPath)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. entities.Select(MetadataMapper.ToModel)];
    }

    public Task UpsertAsync(RecentProjectRecord project, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        var entity = MetadataMapper.ToEntity(project);
        return RepositorySupport.UpsertAsync<RecentProjectEntity>(
            _factory,
            entity,
            [entity.ProjectPath],
            static (current, incoming) =>
            {
                current.DisplayName = incoming.DisplayName;
                current.RepositoryUrl = incoming.RepositoryUrl;
                current.IdeId = incoming.IdeId;
                current.LastOpenedAtUnixMs = incoming.LastOpenedAtUnixMs;
            },
            cancellationToken);
    }

    public Task<bool> RemoveAsync(string projectPath, CancellationToken cancellationToken)
    {
        var normalized = RepositorySupport.NormalizeProjectPath(projectPath, nameof(projectPath));
        return RepositorySupport.RemoveAsync<RecentProjectEntity>(_factory, [normalized], cancellationToken);
    }
}
