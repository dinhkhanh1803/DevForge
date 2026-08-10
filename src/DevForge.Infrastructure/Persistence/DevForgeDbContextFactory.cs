using DevForge.Application.Contracts.Persistence;
using DevForge.Infrastructure.Persistence.Migrations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DevForge.Infrastructure.Persistence;

public sealed class DevForgeDbContextFactory
{
    private readonly DbContextOptions<DevForgeDbContext> _options;

    public DevForgeDbContextFactory(DatabaseLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = location.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            ForeignKeys = true,
        }.ConnectionString;
        _options = CreateOptions(connectionString);
    }

    internal DevForgeDbContextFactory(string connectionString)
    {
        _options = CreateOptions(connectionString);
    }

    public DevForgeDbContext CreateDbContext()
    {
        return new DevForgeDbContext(_options);
    }

    private static DbContextOptions<DevForgeDbContext> CreateOptions(string connectionString)
    {
        return new DbContextOptionsBuilder<DevForgeDbContext>()
            .UseSqlite(
                connectionString,
                sqlite => sqlite.MigrationsHistoryTable(PersistenceMigrationNames.HistoryTable))
            .Options;
    }
}

public sealed class DevForgeDesignTimeDbContextFactory : IDesignTimeDbContextFactory<DevForgeDbContext>
{
    public DevForgeDbContext CreateDbContext(string[] args)
    {
        return new DevForgeDbContextFactory("Data Source=:memory:;Pooling=False;Foreign Keys=True")
            .CreateDbContext();
    }
}
