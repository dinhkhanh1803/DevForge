using System.Collections.Immutable;
using DevForge.Domain.Runs;

namespace DevForge.Application.Contracts;

public interface IRunJournalStore
{
    Task SaveAsync(ProjectRun run, CancellationToken cancellationToken);

    Task<ImmutableArray<ProjectRun>> ListAsync(CancellationToken cancellationToken);
}
