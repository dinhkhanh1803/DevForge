using DevForge.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DevForge.IntegrationTests.Persistence;

public sealed class SqliteSchemaTests
{
    private static readonly string[] _requiredTables =
    [
        "AppSettings",
        "Blueprints",
        "EnvironmentTools",
        "IdeInstallations",
        "Presets",
        "ProjectRuns",
        "RecentProjects",
        "RunSteps",
        "SchemaMigrations",
        "TeamProfiles",
    ];

    [Fact]
    public async Task LatestMigrationCreatesRequiredSchema()
    {
        await using var database = PersistenceTestDatabase.Create();
        var factory = new DevForgeDbContextFactory(database.Location);
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync(CancellationToken.None);
        }

        using var connection = database.OpenConnection(SqliteOpenMode.ReadOnly);
        var tables = ReadNames(
            connection,
            "SELECT name FROM sqlite_schema WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;");
        var indexes = ReadNames(
            connection,
            "SELECT name FROM sqlite_schema WHERE type = 'index' AND name LIKE 'IX_%' ORDER BY name;");

        Assert.All(_requiredTables, table => Assert.Contains(table, tables));
        Assert.Equal(_requiredTables.Length + 1, tables.Length);
        Assert.Contains("__EFMigrationsLock", tables);
        Assert.Contains("IX_EnvironmentTools_ExpiresAtUnixMs", indexes);
        Assert.Contains("IX_ProjectRuns_Status_UpdatedAtUnixMs", indexes);
        Assert.Contains("IX_RecentProjects_LastOpenedAtUnixMs", indexes);
        AssertRunStepCascade(connection);
    }

    private static string[] ReadNames(SqliteConnection connection, string commandText)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return [.. names];
    }

    private static void AssertRunStepCascade(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_key_list('RunSteps');";
        using var reader = command.ExecuteReader();

        Assert.True(reader.Read());
        Assert.Equal("ProjectRuns", reader.GetString(reader.GetOrdinal("table")));
        Assert.Equal("CASCADE", reader.GetString(reader.GetOrdinal("on_delete")));
    }
}
