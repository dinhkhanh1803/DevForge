using DevForge.Application.Contracts.Persistence;
using Microsoft.Data.Sqlite;

namespace DevForge.Infrastructure.Persistence.Migrations;

public sealed class SqliteBackupTransport : ISqliteBackupTransport
{
    public bool DatabaseExists(DatabaseLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        return File.Exists(location.DatabasePath);
    }

    public async Task CreateBackupAsync(
        DatabaseLocation location,
        string suffix,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        cancellationToken.ThrowIfCancellationRequested();
        var backupPath = location.CreateBackupPath(suffix);
        await using var source = CreateConnection(location.DatabasePath, SqliteOpenMode.ReadOnly);
        await using var destination = CreateConnection(backupPath, SqliteOpenMode.ReadWriteCreate);
        await source.OpenAsync(cancellationToken).ConfigureAwait(false);
        await destination.OpenAsync(cancellationToken).ConfigureAwait(false);
        source.BackupDatabase(destination);
        cancellationToken.ThrowIfCancellationRequested();
    }

    public async Task RestoreBackupAsync(
        DatabaseLocation location,
        string suffix,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        cancellationToken.ThrowIfCancellationRequested();
        var backupPath = location.CreateBackupPath(suffix);
        await using var source = CreateConnection(backupPath, SqliteOpenMode.ReadOnly);
        await using var destination = CreateConnection(location.DatabasePath, SqliteOpenMode.ReadWriteCreate);
        await source.OpenAsync(cancellationToken).ConfigureAwait(false);
        await destination.OpenAsync(cancellationToken).ConfigureAwait(false);
        source.BackupDatabase(destination);
        cancellationToken.ThrowIfCancellationRequested();
    }

    public async Task<bool> VerifyIntegrityAsync(
        DatabaseLocation location,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        cancellationToken.ThrowIfCancellationRequested();
        await using var connection = CreateConnection(location.DatabasePath, SqliteOpenMode.ReadOnly);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var rowCount = 0;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rowCount++;
            if (!string.Equals(reader.GetString(0), "ok", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return rowCount == 1;
    }

    private static SqliteConnection CreateConnection(string dataSource, SqliteOpenMode mode)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dataSource,
            Mode = mode,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            ForeignKeys = true,
        }.ConnectionString;
        return new SqliteConnection(connectionString);
    }
}
