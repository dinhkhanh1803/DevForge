using DevForge.Application.Contracts.Persistence;

namespace DevForge.Infrastructure.Persistence.Migrations;

public interface IDatabaseMigrationExecutor
{
    Task<IReadOnlyList<string>> GetPendingMigrationsAsync(
        DatabaseLocation location,
        CancellationToken cancellationToken);

    Task MigrateAsync(DatabaseLocation location, CancellationToken cancellationToken);
}
