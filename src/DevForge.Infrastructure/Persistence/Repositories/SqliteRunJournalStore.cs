using System.Collections.Immutable;
using DevForge.Application.Contracts;
using DevForge.Domain.Runs;
using DevForge.Infrastructure.Persistence.Entities;
using DevForge.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;

namespace DevForge.Infrastructure.Persistence.Repositories;

public sealed class SqliteRunJournalStore : IRunJournalStore
{
    private readonly DevForgeDbContextFactory _factory;
    private readonly TimeProvider _timeProvider;

    public SqliteRunJournalStore(DevForgeDbContextFactory factory)
        : this(factory, TimeProvider.System)
    {
    }

    public SqliteRunJournalStore(DevForgeDbContextFactory factory, TimeProvider timeProvider)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task SaveAsync(ProjectRun run, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(run);
        var now = _timeProvider.GetUtcNow();
        var steps = RunJournalMapper.CreateStepEntities(run);

        await using var context = _factory.CreateDbContext();
        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        var entity = await context.ProjectRuns
            .SingleOrDefaultAsync(item => item.Id == run.Id, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            entity = RunJournalMapper.CreateEntity(run, now);
            context.ProjectRuns.Add(entity);
        }
        else
        {
            RunJournalMapper.UpdateEntity(entity, run, now);
        }

        await context.RunSteps
            .Where(step => step.RunId == run.Id)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        context.RunSteps.AddRange(steps);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ImmutableArray<ProjectRun>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var context = _factory.CreateDbContext();
        var entities = await context.ProjectRuns.AsNoTracking()
            .Include(run => run.Steps)
            .OrderByDescending(run => run.UpdatedAtUnixMs)
            .ThenBy(run => run.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. entities.Select(RunJournalMapper.ToModel)];
    }
}
