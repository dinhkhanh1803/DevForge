using DevForge.Application.Contracts.Persistence;
using DevForge.Infrastructure.Persistence;
using DevForge.Infrastructure.Persistence.Migrations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DevForge.IntegrationTests.Persistence;

public sealed class MigrationRecoveryTests
{
    [Fact]
    public async Task FreshDatabaseMigratesWithoutBackup()
    {
        await using var database = PersistenceTestDatabase.Create();
        var transport = new RecordingBackupTransport(new SqliteBackupTransport());
        var coordinator = CreateCoordinator(transport, new EfDatabaseMigrationExecutor());

        var result = await coordinator.MigrateAsync(database.Location, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(DatabaseMigrationState.Created, result.State);
        Assert.False(result.WasBackupCreated);
        Assert.True(await transport.VerifyIntegrityAsync(database.Location, CancellationToken.None));
    }

    [Fact]
    public async Task ExistingDatabaseUpgradesAfterOnlineBackupAndPreservesData()
    {
        await using var database = PersistenceTestDatabase.Create();
        await CreateInitialDatabaseWithSentinelAsync(database);
        var transport = new RecordingBackupTransport(new SqliteBackupTransport());
        var coordinator = CreateCoordinator(transport, new EfDatabaseMigrationExecutor());

        var result = await coordinator.MigrateAsync(database.Location, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(DatabaseMigrationState.Upgraded, result.State);
        Assert.True(result.WasBackupCreated);
        Assert.Equal("dark", ReadSentinel(database));
        Assert.Equal(4L, ReadScalar(database, "SELECT COUNT(*) FROM SchemaMigrations;"));
        Assert.NotNull(transport.LastBackupSuffix);
        Assert.True(File.Exists(database.Location.CreateBackupPath(transport.LastBackupSuffix)));
    }

    [Fact]
    public async Task MigrationFailureRestoresOriginalAndReturnsScrubbedResult()
    {
        await using var database = PersistenceTestDatabase.Create();
        await CreateInitialDatabaseWithSentinelAsync(database);
        var transport = new RecordingBackupTransport(new SqliteBackupTransport());
        var coordinator = CreateCoordinator(transport, new MutatingFailureExecutor());
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = database.Location.DatabasePath,
        }.ConnectionString;

        var result = await coordinator.MigrateAsync(database.Location, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(DatabaseMigrationState.RestoredAfterFailure, result.State);
        Assert.Equal("DF-DB-001", result.Code);
        Assert.True(result.WasRestored);
        Assert.Equal("dark", ReadSentinel(database));
        Assert.True(await transport.VerifyIntegrityAsync(database.Location, CancellationToken.None));
        Assert.DoesNotContain(database.Location.DatabasePath, result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(connectionString, result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("raw migration failure", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IntegrityFailureAfterUpgradeRestoresOriginalBackup()
    {
        await using var database = PersistenceTestDatabase.Create();
        await CreateInitialDatabaseWithSentinelAsync(database);
        var transport = new RecordingBackupTransport(
            new SqliteBackupTransport(),
            failFirstIntegrityCheck: true);
        var coordinator = CreateCoordinator(transport, new EfDatabaseMigrationExecutor());

        var result = await coordinator.MigrateAsync(database.Location, CancellationToken.None);

        Assert.Equal(DatabaseMigrationState.RestoredAfterFailure, result.State);
        Assert.True(result.WasRestored);
        Assert.Equal("dark", ReadSentinel(database));
        Assert.True(await transport.VerifyIntegrityAsync(database.Location, CancellationToken.None));
    }

    [Fact]
    public async Task RestoreFailureReportsRecoveryFailureAndRetainsBothArtifacts()
    {
        await using var database = PersistenceTestDatabase.Create();
        await CreateInitialDatabaseWithSentinelAsync(database);
        var transport = new RecordingBackupTransport(
            new SqliteBackupTransport(),
            failRestore: true);
        var coordinator = CreateCoordinator(transport, new MutatingFailureExecutor());

        var result = await coordinator.MigrateAsync(database.Location, CancellationToken.None);

        Assert.Equal(DatabaseMigrationState.RecoveryFailed, result.State);
        Assert.Equal("DF-DB-001", result.Code);
        Assert.True(result.WasBackupCreated);
        Assert.True(result.BackupRetained);
        Assert.Equal("mutated", ReadSentinel(database));
        Assert.NotNull(transport.LastBackupSuffix);
        Assert.True(File.Exists(database.Location.CreateBackupPath(transport.LastBackupSuffix)));
        Assert.DoesNotContain(database.Location.DatabasePath, result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancellationAfterMutationRestoresBeforePropagatingCancellation()
    {
        await using var database = PersistenceTestDatabase.Create();
        await CreateInitialDatabaseWithSentinelAsync(database);
        using var cancellation = new CancellationTokenSource();
        var transport = new RecordingBackupTransport(new SqliteBackupTransport());
        var coordinator = CreateCoordinator(
            transport,
            new MutatingCancellationExecutor(cancellation));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => coordinator.MigrateAsync(database.Location, cancellation.Token));

        Assert.Equal("dark", ReadSentinel(database));
        Assert.True(await transport.VerifyIntegrityAsync(database.Location, CancellationToken.None));
    }

    private static SqliteMigrationCoordinator CreateCoordinator(
        ISqliteBackupTransport transport,
        IDatabaseMigrationExecutor executor)
    {
        return new SqliteMigrationCoordinator(
            transport,
            executor,
            new TestTimeProvider(DateTimeOffset.UnixEpoch));
    }

    private static async Task CreateInitialDatabaseWithSentinelAsync(PersistenceTestDatabase database)
    {
        var factory = new DevForgeDbContextFactory(database.Location);
        await using (var context = factory.CreateDbContext())
        {
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(
                PersistenceMigrationNames.InitialSchema,
                CancellationToken.None);
        }

        using var connection = database.OpenConnection();
        using var insert = connection.CreateCommand();
        insert.CommandText =
            "INSERT INTO AppSettings (Key, ValueKind, SerializedValue, UpdatedAtUnixMs) "
            + "VALUES ('ui.theme', 'Text', 'dark', 0);";
        Assert.Equal(1, insert.ExecuteNonQuery());
    }

    private static string ReadSentinel(PersistenceTestDatabase database)
    {
        using var connection = database.OpenConnection(SqliteOpenMode.ReadOnly);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT SerializedValue FROM AppSettings WHERE Key = 'ui.theme';";
        return Assert.IsType<string>(command.ExecuteScalar());
    }

    private static long ReadScalar(PersistenceTestDatabase database, string commandText)
    {
        using var connection = database.OpenConnection(SqliteOpenMode.ReadOnly);
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        return Assert.IsType<long>(command.ExecuteScalar());
    }

    private sealed class MutatingFailureExecutor : IDatabaseMigrationExecutor
    {
        public Task<IReadOnlyList<string>> GetPendingMigrationsAsync(
            DatabaseLocation location,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<string>>(["InjectedFailure"]);
        }

        public async Task MigrateAsync(DatabaseLocation location, CancellationToken cancellationToken)
        {
            var factory = new DevForgeDbContextFactory(location);
            await using var context = factory.CreateDbContext();
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE AppSettings SET SerializedValue = 'mutated' WHERE Key = 'ui.theme';",
                cancellationToken);
            throw new InvalidOperationException($"raw migration failure at {location.DatabasePath}");
        }
    }

    private sealed class MutatingCancellationExecutor(CancellationTokenSource cancellation)
        : IDatabaseMigrationExecutor
    {
        public Task<IReadOnlyList<string>> GetPendingMigrationsAsync(
            DatabaseLocation location,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<string>>(["InjectedCancellation"]);
        }

        public async Task MigrateAsync(DatabaseLocation location, CancellationToken cancellationToken)
        {
            var factory = new DevForgeDbContextFactory(location);
            await using var context = factory.CreateDbContext();
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE AppSettings SET SerializedValue = 'mutated' WHERE Key = 'ui.theme';",
                CancellationToken.None);
            await cancellation.CancelAsync();
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private sealed class RecordingBackupTransport(
        ISqliteBackupTransport inner,
        bool failFirstIntegrityCheck = false,
        bool failRestore = false) : ISqliteBackupTransport
    {
        private bool _integrityFailurePending = failFirstIntegrityCheck;

        public string? LastBackupSuffix { get; private set; }

        public bool DatabaseExists(DatabaseLocation location) => inner.DatabaseExists(location);

        public async Task CreateBackupAsync(
            DatabaseLocation location,
            string suffix,
            CancellationToken cancellationToken)
        {
            LastBackupSuffix = suffix;
            await inner.CreateBackupAsync(location, suffix, cancellationToken);
        }

        public Task RestoreBackupAsync(
            DatabaseLocation location,
            string suffix,
            CancellationToken cancellationToken)
        {
            return failRestore
                ? Task.FromException(new InvalidOperationException("injected restore failure"))
                : inner.RestoreBackupAsync(location, suffix, cancellationToken);
        }

        public Task<bool> VerifyIntegrityAsync(
            DatabaseLocation location,
            CancellationToken cancellationToken)
        {
            if (_integrityFailurePending)
            {
                _integrityFailurePending = false;
                return Task.FromResult(false);
            }

            return inner.VerifyIntegrityAsync(location, cancellationToken);
        }
    }

    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
