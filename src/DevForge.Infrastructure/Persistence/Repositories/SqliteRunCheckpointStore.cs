using System.Collections.Immutable;
using DevForge.Application.Contracts;
using DevForge.Infrastructure.Persistence.Entities;
using DevForge.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;

namespace DevForge.Infrastructure.Persistence.Repositories;

public sealed class SqliteRunCheckpointStore : IRunCheckpointStore
{
    private readonly DevForgeDbContextFactory _factory;
    private readonly TimeProvider _timeProvider;

    public SqliteRunCheckpointStore(DevForgeDbContextFactory factory)
        : this(factory, TimeProvider.System)
    {
    }

    public SqliteRunCheckpointStore(
        DevForgeDbContextFactory factory,
        TimeProvider timeProvider)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task SaveAsync(
        RunCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(checkpoint);
        var now = _timeProvider.GetUtcNow();
        var steps = RunCheckpointMapper.CreateStepEntities(checkpoint);

        await using var context = _factory.CreateDbContext();
        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        var entity = await context.ProjectRuns
            .SingleOrDefaultAsync(item => item.Id == checkpoint.Run.Id, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            entity = RunCheckpointMapper.CreateEntity(checkpoint, now);
            context.ProjectRuns.Add(entity);
        }
        else
        {
            RunCheckpointMapper.UpdateEntity(entity, checkpoint, now);
        }

        await context.RunSteps
            .Where(step => step.RunId == checkpoint.Run.Id)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        context.RunSteps.AddRange(steps);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<RunCheckpoint?> FindAsync(
        string runId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(runId) || runId.Length > 128)
        {
            throw new ArgumentException("A bounded run identifier is required.", nameof(runId));
        }

        await using var context = _factory.CreateDbContext();
        var entity = await context.ProjectRuns.AsNoTracking()
            .Include(run => run.Steps)
            .SingleOrDefaultAsync(
                run => run.Id == runId && run.PlanHash != null,
                cancellationToken)
            .ConfigureAwait(false);
        return entity is null ? null : RunCheckpointMapper.ToModel(entity);
    }

    public async Task<ImmutableArray<RunCheckpoint>> ListAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var context = _factory.CreateDbContext();
        var entities = await context.ProjectRuns.AsNoTracking()
            .Where(run => run.PlanHash != null)
            .Include(run => run.Steps)
            .OrderByDescending(run => run.UpdatedAtUnixMs)
            .ThenBy(run => run.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. entities.Select(RunCheckpointMapper.ToModel)];
    }
}
