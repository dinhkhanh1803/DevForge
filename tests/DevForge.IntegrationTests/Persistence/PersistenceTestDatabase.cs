using DevForge.Application.Contracts.Persistence;
using Microsoft.Data.Sqlite;

namespace DevForge.IntegrationTests.Persistence;

internal sealed class PersistenceTestDatabase : IAsyncDisposable
{
    private PersistenceTestDatabase(string rootDirectory, DatabaseLocation location)
    {
        RootDirectory = rootDirectory;
        Location = location;
    }

    public string RootDirectory { get; }

    public DatabaseLocation Location { get; }

    public static PersistenceTestDatabase Create()
    {
        var testRoot = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            "DevForge.IntegrationTests",
            Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(testRoot);
        var location = DatabaseLocation.Create(testRoot, "devforge.db");
        Assert.True(location.IsValid);
        return new PersistenceTestDatabase(testRoot, location.Value);
    }

    public SqliteConnection OpenConnection(SqliteOpenMode mode = SqliteOpenMode.ReadWrite)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = Location.DatabasePath,
            Mode = mode,
            ForeignKeys = true,
            Pooling = false,
        };
        var connection = new SqliteConnection(builder.ConnectionString);
        connection.Open();
        return connection;
    }

    public ValueTask DisposeAsync()
    {
        var tempRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "DevForge.IntegrationTests"));
        var resolvedRoot = Path.GetFullPath(RootDirectory);
        if (!resolvedRoot.StartsWith(
                tempRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to remove a directory outside the test root.");
        }

        if (Directory.Exists(resolvedRoot))
        {
            Directory.Delete(resolvedRoot, recursive: true);
        }

        return ValueTask.CompletedTask;
    }
}
