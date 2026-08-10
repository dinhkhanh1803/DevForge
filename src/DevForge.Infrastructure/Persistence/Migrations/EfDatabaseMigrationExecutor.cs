using DevForge.Application.Contracts.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevForge.Infrastructure.Persistence.Migrations;

public sealed class EfDatabaseMigrationExecutor : IDatabaseMigrationExecutor
{
    public async Task<IReadOnlyList<string>> GetPendingMigrationsAsync(
        DatabaseLocation location,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        cancellationToken.ThrowIfCancellationRequested();
        var factory = new DevForgeDbContextFactory(location);
        await using var context = factory.CreateDbContext();
        var pending = await context.Database
            .GetPendingMigrationsAsync(cancellationToken)
            .ConfigureAwait(false);
        return pending.ToArray();
    }

    public async Task MigrateAsync(DatabaseLocation location, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        cancellationToken.ThrowIfCancellationRequested();
        var factory = new DevForgeDbContextFactory(location);
        await using var context = factory.CreateDbContext();
        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }
}
