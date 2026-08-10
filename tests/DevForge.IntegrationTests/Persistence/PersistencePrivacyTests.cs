using DevForge.Application.Contracts;
using DevForge.Application.Contracts.Persistence;
using DevForge.Domain.Diagnostics;
using DevForge.Domain.Privacy;
using DevForge.Domain.Runs;
using DevForge.Infrastructure.Persistence;
using DevForge.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DevForge.IntegrationTests.Persistence;

public sealed class PersistencePrivacyTests
{
    [Fact]
    public async Task PublicWritesRejectCredentialAndEnvironmentContentFixtures()
    {
        const string credentialFixture = "sk-proj-abcdefghijklmnop";
        const string environmentFixture = "contents of .env: SAMPLE=value";
        await using var database = PersistenceTestDatabase.Create();
        var factory = await CreateMigratedFactoryAsync(database);

        var settingValue = AppSettingValue.CreateString(credentialFixture);
        var document = PersistableJson.Create($"{{\"note\":\"{environmentFixture}\"}}");
        Assert.False(settingValue.IsValid);
        Assert.False(document.IsValid);

        var error = DevForgeError.Create(
            "DF-TEST-001",
            "Bearer abcdefghijk",
            RedactedText.FromTrustedRedaction("A scrubbed diagnostic.").Value,
            "generation",
            "generate",
            false,
            ["Review the report."],
            []).Value;
        var run = ProjectRun.Create("run-private", "recipe.private").Value
            .TransitionTo(RunStatus.Planning).Value
            .TransitionTo(RunStatus.Executing).Value
            .StartAttempt("generate", DateTimeOffset.UnixEpoch).Value
            .CompleteAttempt(
                "generate",
                1,
                StepAttemptOutcome.Failed,
                DateTimeOffset.UnixEpoch.AddSeconds(1),
                1,
                error).Value
            .TransitionTo(RunStatus.Failed).Value;
        var journal = new SqliteRunJournalStore(factory);
        await Assert.ThrowsAsync<PersistenceDataException>(
            () => journal.SaveAsync(run, CancellationToken.None));
        Assert.Empty(await journal.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RawDatabaseContainsNoForbiddenFixtureOrConnectionString()
    {
        const string credentialFixture = "sk-proj-abcdefghijklmnop";
        const string environmentFixture = "contents of .env: SAMPLE=value";
        const string sourceFixture = "SOURCE_CODE_FIXTURE_DoNotPersist";
        const string rawLogFixture = "RAW_PROCESS_OUTPUT_FIXTURE_DoNotPersist";
        await using var database = PersistenceTestDatabase.Create();
        var factory = await CreateMigratedFactoryAsync(database);
        var settings = new AppSettingsStore(factory);
        var setting = AppSetting.Create(
            "ui.theme",
            AppSettingValue.CreateString("dark").Value,
            DateTimeOffset.UnixEpoch).Value;
        await settings.UpsertAsync(setting, CancellationToken.None);
        var journal = new SqliteRunJournalStore(factory);
        await journal.SaveAsync(
            ProjectRun.Create("run-safe", "recipe.safe").Value,
            CancellationToken.None);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = database.Location.DatabasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ConnectionString;

        var values = ReadAllTextAndBlobValues(database);

        Assert.DoesNotContain(values, value => value.Contains(credentialFixture, StringComparison.Ordinal));
        Assert.DoesNotContain(values, value => value.Contains(environmentFixture, StringComparison.Ordinal));
        Assert.DoesNotContain(values, value => value.Contains(sourceFixture, StringComparison.Ordinal));
        Assert.DoesNotContain(values, value => value.Contains(rawLogFixture, StringComparison.Ordinal));
        Assert.DoesNotContain(values, value => value.Contains(connectionString, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            values,
            value => value.Contains(database.Location.DatabasePath, StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> ReadAllTextAndBlobValues(PersistenceTestDatabase database)
    {
        using var connection = database.OpenConnection(SqliteOpenMode.ReadOnly);
        var tables = new List<string>();
        using (var tableCommand = connection.CreateCommand())
        {
            tableCommand.CommandText =
                "SELECT name FROM sqlite_schema "
                + "WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
            using var reader = tableCommand.ExecuteReader();
            while (reader.Read())
            {
                tables.Add(reader.GetString(0));
            }
        }

        var values = new List<string>();
        foreach (var table in tables)
        {
            var columns = ReadTextAndBlobColumns(connection, table);
            foreach (var column in columns)
            {
                using var valueCommand = connection.CreateCommand();
                valueCommand.CommandText =
                    $"SELECT \"{EscapeIdentifier(column)}\" FROM \"{EscapeIdentifier(table)}\" "
                    + $"WHERE \"{EscapeIdentifier(column)}\" IS NOT NULL;";
                using var reader = valueCommand.ExecuteReader();
                while (reader.Read())
                {
                    values.Add(reader.GetValue(0) switch
                    {
                        byte[] bytes => Convert.ToHexString(bytes),
                        var value => Convert.ToString(
                            value,
                            System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                    });
                }
            }
        }

        return values;
    }

    private static List<string> ReadTextAndBlobColumns(
        SqliteConnection connection,
        string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{EscapeIdentifier(table)}\");";
        using var reader = command.ExecuteReader();
        var columns = new List<string>();
        while (reader.Read())
        {
            var type = reader.GetString(2);
            if (type.Contains("TEXT", StringComparison.OrdinalIgnoreCase)
                || type.Contains("BLOB", StringComparison.OrdinalIgnoreCase))
            {
                columns.Add(reader.GetString(1));
            }
        }

        return columns;
    }

    private static string EscapeIdentifier(string identifier) => identifier.Replace("\"", "\"\"");

    private static async Task<DevForgeDbContextFactory> CreateMigratedFactoryAsync(
        PersistenceTestDatabase database)
    {
        var factory = new DevForgeDbContextFactory(database.Location);
        await using var context = factory.CreateDbContext();
        await context.Database.MigrateAsync(CancellationToken.None);
        return factory;
    }
}
