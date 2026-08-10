using DevForge.Application.Contracts.Persistence;

namespace DevForge.Infrastructure.Persistence.Migrations;

public interface ISqliteBackupTransport
{
    bool DatabaseExists(DatabaseLocation location);

    Task CreateBackupAsync(
        DatabaseLocation location,
        string suffix,
        CancellationToken cancellationToken);

    Task RestoreBackupAsync(
        DatabaseLocation location,
        string suffix,
        CancellationToken cancellationToken);

    Task<bool> VerifyIntegrityAsync(
        DatabaseLocation location,
        CancellationToken cancellationToken);
}
