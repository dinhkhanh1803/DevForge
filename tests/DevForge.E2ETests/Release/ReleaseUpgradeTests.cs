using System.Diagnostics;
using System.IO;
using System.Windows.Automation;
using DevForge.Application.Contracts.Persistence;
using DevForge.Infrastructure.Persistence;
using DevForge.Infrastructure.Persistence.Migrations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DevForge.E2ETests.Release;

public sealed class ReleaseUpgradeTests : IDisposable
{
    private readonly string _testRoot = Path.Combine(
        Path.GetTempPath(),
        "DevForge-ReleasePackageTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task PackagedDesktopStartsWithFreshIsolatedLocalData()
    {
        var localData = Path.Combine(_testRoot, "fresh");

        await using var desktop = await StartResponsiveDesktopAsync(localData);

        Assert.True(File.Exists(Path.Combine(localData, "devforge.db")));
        Assert.True(Directory.Exists(Path.Combine(localData, "blueprints", "local")));
        Assert.Null(FindSafeModeMessage(desktop.Process));
    }

    [Fact]
    public async Task PackagedDesktopUpgradesAndPreservesPriorSchemaData()
    {
        var localData = Path.Combine(_testRoot, "upgrade");
        Directory.CreateDirectory(localData);
        var location = DatabaseLocation.Create(localData, "devforge.db").Value;
        await CreatePriorSchemaAsync(location, injectMigrationFailure: false);

        await using var desktop = await StartResponsiveDesktopAsync(localData);

        Assert.Null(FindSafeModeMessage(desktop.Process));
        Assert.Equal("dark", ReadSetting(location, "ui.theme"));
        Assert.True(HasLatestMigration(location));
        Assert.Single(Directory.GetFiles(localData, "devforge.backup-upgrade-*.db"));
    }

    [Fact]
    public async Task PackagedDesktopRestoresFailedUpgradeAndEntersSafeMode()
    {
        var localData = Path.Combine(_testRoot, "failed-upgrade");
        Directory.CreateDirectory(localData);
        var location = DatabaseLocation.Create(localData, "devforge.db").Value;
        await CreatePriorSchemaAsync(location, injectMigrationFailure: true);

        await using var desktop = await StartResponsiveDesktopAsync(localData);

        Assert.NotNull(FindSafeModeMessage(desktop.Process));
        Assert.Equal("dark", ReadSetting(location, "ui.theme"));
        Assert.False(HasLatestMigration(location));
        Assert.Single(Directory.GetFiles(localData, "devforge.backup-upgrade-*.db"));
    }

    public void Dispose()
    {
        var ownerRoot = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            "DevForge-ReleasePackageTests"));
        var resolved = Path.GetFullPath(_testRoot);
        if (resolved.StartsWith(
            ownerRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(resolved))
        {
            Directory.Delete(resolved, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private static async Task<RunningDesktop> StartResponsiveDesktopAsync(string localData)
    {
        var executable = Path.Combine(ResolvePackageRoot(), "DevForge.Desktop.exe");
        Assert.True(File.Exists(executable), $"Packaged Desktop executable not found: {executable}");

        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = false,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
        };
        startInfo.ArgumentList.Add("--local-data-root");
        startInfo.ArgumentList.Add(localData);
        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Packaged Desktop did not start.");
        var desktop = new RunningDesktop(process);
        try
        {
            Assert.True(process.WaitForInputIdle(20_000), "Packaged Desktop did not become input-idle.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (process.MainWindowHandle == IntPtr.Zero && !process.HasExited)
            {
                await Task.Delay(100, timeout.Token);
                process.Refresh();
            }

            Assert.False(process.HasExited, "Packaged Desktop exited before presenting its main window.");
            Assert.NotEqual(IntPtr.Zero, process.MainWindowHandle);
            Assert.True(process.Responding);
            return desktop;
        }
        catch
        {
            await desktop.DisposeAsync();
            throw;
        }
    }

    private static AutomationElement? FindSafeModeMessage(Process process)
    {
        process.Refresh();
        var root = AutomationElement.FromHandle(process.MainWindowHandle);
        return root.FindFirst(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.NameProperty, "Safe mode message"));
    }

    private static async Task CreatePriorSchemaAsync(
        DatabaseLocation location,
        bool injectMigrationFailure)
    {
        var factory = new DevForgeDbContextFactory(location);
        await using (var context = factory.CreateDbContext())
        {
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(
                PersistenceMigrationNames.InitialSchema,
                CancellationToken.None);
        }

        using var connection = Open(location, SqliteOpenMode.ReadWrite);
        using (var insert = connection.CreateCommand())
        {
            insert.CommandText =
                "INSERT INTO AppSettings (Key, ValueKind, SerializedValue, UpdatedAtUnixMs) " +
                "VALUES ($key, $kind, $value, $updated);";
            insert.Parameters.AddWithValue("$key", "ui.theme");
            insert.Parameters.AddWithValue("$kind", "Text");
            insert.Parameters.AddWithValue("$value", "dark");
            insert.Parameters.AddWithValue("$updated", 0L);
            Assert.Equal(1, insert.ExecuteNonQuery());
        }

        if (injectMigrationFailure)
        {
            using var poison = connection.CreateCommand();
            poison.CommandText =
                "CREATE INDEX IX_RecentProjects_LastOpenedAtUnixMs " +
                "ON RecentProjects (LastOpenedAtUnixMs);";
            poison.ExecuteNonQuery();
        }
    }

    private static string? ReadSetting(DatabaseLocation location, string key)
    {
        using var connection = Open(location, SqliteOpenMode.ReadOnly);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT SerializedValue FROM AppSettings WHERE Key = $key;";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    private static bool HasLatestMigration(DatabaseLocation location)
    {
        using var connection = Open(location, SqliteOpenMode.ReadOnly);
        using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT COUNT(*) FROM {PersistenceMigrationNames.HistoryTable} " +
            "WHERE MigrationId = $migration;";
        command.Parameters.AddWithValue("$migration", PersistenceMigrationNames.PublicationCheckpoints);
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) == 1;
    }

    private static SqliteConnection Open(DatabaseLocation location, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = location.DatabasePath,
            Mode = mode,
            Pooling = false,
        }.ConnectionString);
        connection.Open();
        return connection;
    }

    private static string ResolvePackageRoot()
    {
        var configured = Environment.GetEnvironmentVariable("DEVFORGE_RELEASE_PACKAGE_ROOT");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        var repositoryPackage = Path.Combine(
            FindRepositoryRoot(),
            "artifacts",
            "release",
            "win-x64");
        return Directory.Exists(repositoryPackage)
            ? repositoryPackage
            : AppContext.BaseDirectory;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "DevForge.sln")))
        {
            current = current.Parent;
        }

        return current?.FullName
            ?? throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class RunningDesktop(Process process) : IAsyncDisposable
    {
        public Process Process { get; } = process;

        public async ValueTask DisposeAsync()
        {
            if (!Process.HasExited)
            {
                Process.CloseMainWindow();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try
                {
                    await Process.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    Process.Kill(entireProcessTree: true);
                    await Process.WaitForExitAsync(CancellationToken.None);
                }
            }

            Process.Dispose();
        }
    }
}
