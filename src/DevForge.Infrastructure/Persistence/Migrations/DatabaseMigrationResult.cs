namespace DevForge.Infrastructure.Persistence.Migrations;

public enum DatabaseMigrationState
{
    Created = 1,
    UpToDate = 2,
    Upgraded = 3,
    Failed = 4,
    RestoredAfterFailure = 5,
    RecoveryFailed = 6,
}

public sealed class DatabaseMigrationResult
{
    private DatabaseMigrationResult(
        DatabaseMigrationState state,
        bool wasBackupCreated,
        bool wasRestored,
        bool backupRetained,
        string? code,
        string message)
    {
        State = state;
        WasBackupCreated = wasBackupCreated;
        WasRestored = wasRestored;
        BackupRetained = backupRetained;
        Code = code;
        Message = message;
    }

    public DatabaseMigrationState State { get; }

    public bool IsSuccess => State is DatabaseMigrationState.Created
        or DatabaseMigrationState.UpToDate
        or DatabaseMigrationState.Upgraded;

    public bool WasBackupCreated { get; }

    public bool WasRestored { get; }

    public bool BackupRetained { get; }

    public string? Code { get; }

    public string Message { get; }

    internal static DatabaseMigrationResult Success(
        DatabaseMigrationState state,
        bool wasBackupCreated)
    {
        var message = state switch
        {
            DatabaseMigrationState.Created => "The local database was created successfully.",
            DatabaseMigrationState.UpToDate => "The local database is up to date.",
            DatabaseMigrationState.Upgraded => "The local database was upgraded successfully.",
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };
        return new DatabaseMigrationResult(
            state,
            wasBackupCreated,
            false,
            wasBackupCreated,
            null,
            message);
    }

    internal static DatabaseMigrationResult Failure(bool wasBackupCreated)
    {
        return new DatabaseMigrationResult(
            DatabaseMigrationState.Failed,
            wasBackupCreated,
            false,
            wasBackupCreated,
            PersistenceDataException.ErrorCode,
            "The local database could not be prepared safely.");
    }

    internal static DatabaseMigrationResult Restored()
    {
        return new DatabaseMigrationResult(
            DatabaseMigrationState.RestoredAfterFailure,
            true,
            true,
            true,
            PersistenceDataException.ErrorCode,
            "The database upgrade failed and the previous database was restored.");
    }

    internal static DatabaseMigrationResult RecoveryFailed()
    {
        return new DatabaseMigrationResult(
            DatabaseMigrationState.RecoveryFailed,
            true,
            false,
            true,
            PersistenceDataException.ErrorCode,
            "The database upgrade and automatic recovery failed; recovery artifacts were retained.");
    }
}
