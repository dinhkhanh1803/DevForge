using System.Collections.Immutable;
using DevForge.Domain.Runs;

namespace DevForge.Application.Contracts;

public interface IRunJournalStore
{
    /// <summary>
    /// Atomically replaces the persisted immutable snapshot for a run identifier.
    /// </summary>
    Task SaveAsync(ProjectRun run, CancellationToken cancellationToken);

    /// <summary>
    /// Returns detached run snapshots in deterministic most-recently-updated order.
    /// </summary>
    Task<ImmutableArray<ProjectRun>> ListAsync(CancellationToken cancellationToken);
}
