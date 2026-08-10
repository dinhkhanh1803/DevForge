using DevForge.Infrastructure.Persistence;
using DevForge.Infrastructure.Persistence.Migrations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DevForge.IntegrationTests.Persistence;

public sealed class SqliteMigrationUpgradeTests
{
    [Fact]
    public async Task UpgradePreservesHistoricalData()
    {
        await using var database = PersistenceTestDatabase.Create();
        var factory = new DevForgeDbContextFactory(database.Location);
        await using (var context = factory.CreateDbContext())
        {
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(
                PersistenceMigrationNames.InitialSchema,
                CancellationToken.None);
        }

        using (var connection = database.OpenConnection())
        {
            using var insert = connection.CreateCommand();
            insert.CommandText =
                "INSERT INTO AppSettings (Key, ValueKind, SerializedValue, UpdatedAtUnixMs) " +
                "VALUES ($key, $kind, $value, $updated);";
            insert.Parameters.AddWithValue("$key", "ui.theme");
            insert.Parameters.AddWithValue("$kind", "Text");
            insert.Parameters.AddWithValue("$value", "dark");
            insert.Parameters.AddWithValue("$updated", 0L);
            Assert.Equal(1, insert.ExecuteNonQuery());
        }

        await using (var context = factory.CreateDbContext())
        {
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(cancellationToken: CancellationToken.None);
        }

        using var readConnection = database.OpenConnection(SqliteOpenMode.ReadOnly);
        using var read = readConnection.CreateCommand();
        read.CommandText = "SELECT SerializedValue FROM AppSettings WHERE Key = $key;";
        read.Parameters.AddWithValue("$key", "ui.theme");
        Assert.Equal("dark", read.ExecuteScalar());

        using var index = readConnection.CreateCommand();
        index.CommandText =
            "SELECT COUNT(*) FROM sqlite_schema " +
            "WHERE type = 'index' AND name = 'IX_ProjectRuns_Status_UpdatedAtUnixMs';";
        Assert.Equal(1L, index.ExecuteScalar());
    }
}
