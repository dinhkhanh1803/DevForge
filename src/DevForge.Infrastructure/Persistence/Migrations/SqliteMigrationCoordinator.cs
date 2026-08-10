using System.Runtime.ExceptionServices;
using DevForge.Application.Contracts.Persistence;

namespace DevForge.Infrastructure.Persistence.Migrations;

public sealed class SqliteMigrationCoordinator
{
    private readonly ISqliteBackupTransport _backupTransport;
    private readonly IDatabaseMigrationExecutor _migrationExecutor;
    private readonly TimeProvider _timeProvider;

    public SqliteMigrationCoordinator(
        ISqliteBackupTransport backupTransport,
        IDatabaseMigrationExecutor migrationExecutor,
        TimeProvider timeProvider)
    {
        _backupTransport = backupTransport ?? throw new ArgumentNullException(nameof(backupTransport));
        _migrationExecutor = migrationExecutor ?? throw new ArgumentNullException(nameof(migrationExecutor));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<DatabaseMigrationResult> MigrateAsync(
        DatabaseLocation location,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        cancellationToken.ThrowIfCancellationRequested();
        var existedBeforeMigration = _backupTransport.DatabaseExists(location);
        IReadOnlyList<string> pendingMigrations;
        try
        {
            pendingMigrations = await _migrationExecutor
                .GetPendingMigrationsAsync(location, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return DatabaseMigrationResult.Failure(wasBackupCreated: false);
        }

        if (pendingMigrations.Count == 0)
        {
            try
            {
                var isValid = await _backupTransport
                    .VerifyIntegrityAsync(location, cancellationToken)
                    .ConfigureAwait(false);
                return isValid
                    ? DatabaseMigrationResult.Success(DatabaseMigrationState.UpToDate, wasBackupCreated: false)
                    : DatabaseMigrationResult.Failure(wasBackupCreated: false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return DatabaseMigrationResult.Failure(wasBackupCreated: false);
            }
        }

        string? backupSuffix = null;
        if (existedBeforeMigration)
        {
            backupSuffix = CreateBackupSuffix();
            try
            {
                await _backupTransport
                    .CreateBackupAsync(location, backupSuffix, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return DatabaseMigrationResult.Failure(wasBackupCreated: false);
            }
        }

        try
        {
            await _migrationExecutor.MigrateAsync(location, cancellationToken).ConfigureAwait(false);
            if (!await _backupTransport
                    .VerifyIntegrityAsync(location, cancellationToken)
                    .ConfigureAwait(false))
            {
                throw new PersistenceDataException();
            }

            return DatabaseMigrationResult.Success(
                existedBeforeMigration ? DatabaseMigrationState.Upgraded : DatabaseMigrationState.Created,
                wasBackupCreated: backupSuffix is not null);
        }
        catch (Exception failure)
        {
            if (backupSuffix is null)
            {
                if (failure is OperationCanceledException)
                {
                    ExceptionDispatchInfo.Capture(failure).Throw();
                }

                return DatabaseMigrationResult.Failure(wasBackupCreated: false);
            }

            try
            {
                await _backupTransport
                    .RestoreBackupAsync(location, backupSuffix, CancellationToken.None)
                    .ConfigureAwait(false);
                if (!await _backupTransport
                        .VerifyIntegrityAsync(location, CancellationToken.None)
                        .ConfigureAwait(false))
                {
                    return DatabaseMigrationResult.RecoveryFailed();
                }
            }
            catch (Exception)
            {
                return DatabaseMigrationResult.RecoveryFailed();
            }

            if (failure is OperationCanceledException)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }

            return DatabaseMigrationResult.Restored();
        }
    }

    private string CreateBackupSuffix()
    {
        var timestamp = _timeProvider.GetUtcNow().ToString(
            "yyyyMMddHHmmssfff",
            System.Globalization.CultureInfo.InvariantCulture);
        return $"upgrade-{timestamp}-{Guid.NewGuid():N}";
    }
}
