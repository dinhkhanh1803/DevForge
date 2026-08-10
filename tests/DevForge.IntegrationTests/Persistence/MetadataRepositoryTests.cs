using DevForge.Application.Contracts.Persistence;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Infrastructure.Persistence;
using DevForge.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DevForge.IntegrationTests.Persistence;

public sealed class MetadataRepositoryTests
{
    [Fact]
    public async Task SettingsRoundTripUpsertAndRemove()
    {
        await using var database = PersistenceTestDatabase.Create();
        var factory = await CreateMigratedFactoryAsync(database);
        var store = new AppSettingsStore(factory);
        var original = AppSetting.Create(
            "ui.theme",
            AppSettingValue.CreateString("dark").Value,
            DateTimeOffset.UnixEpoch).Value;
        var updated = AppSetting.Create(
            "ui.theme",
            AppSettingValue.CreateString("light").Value,
            DateTimeOffset.UnixEpoch.AddMinutes(1)).Value;

        await store.UpsertAsync(original, CancellationToken.None);
        await store.UpsertAsync(updated, CancellationToken.None);

        var loaded = await store.GetAsync(" ui.theme ", CancellationToken.None);
        var listed = await store.ListAsync(CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal("light", loaded.Value.StringValue);
        Assert.Single(listed);
        Assert.True(await store.RemoveAsync("ui.theme", CancellationToken.None));
        Assert.False(await store.RemoveAsync("ui.theme", CancellationToken.None));
        Assert.Null(await store.GetAsync("ui.theme", CancellationToken.None));
    }

    [Fact]
    public async Task MetadataStoresRoundTripValidatedSnapshots()
    {
        await using var database = PersistenceTestDatabase.Create();
        var factory = await CreateMigratedFactoryAsync(database);
        var now = DateTimeOffset.UnixEpoch.AddDays(1);
        var document = PersistableJson.Create("{\"branch\":\"main\"}").Value;
        var ide = IdeInstallationRecord.Create(
            "vscode",
            IdeKind.VisualStudioCode,
            @"C:\Tools\Code.exe",
            "1.2.3",
            InstallationValidationState.Valid,
            now).Value;
        var tool = EnvironmentToolRecord.Create(
            "git",
            @"C:\Tools\git.exe",
            "2.51.0",
            EnvironmentToolStatus.Compatible,
            now,
            now.AddMinutes(30)).Value;
        var blueprint = BlueprintMetadataRecord.Create(
            "web.react-vite-ts",
            "1.0.0",
            BlueprintSource.BuiltIn,
            BlueprintTrust.BuiltIn,
            new string('a', 64),
            false,
            now).Value;
        var profile = TeamProfileRecord.Create("team.standard", "Team", 1, document, now).Value;
        var preset = PresetRecord.Create("preset.react", "React", 1, document, now).Value;
        var recent = RecentProjectRecord.Create(
            @"C:\Projects\portal",
            "Portal",
            "https://github.com/example/portal",
            "vscode",
            now).Value;

        var ideStore = new IdeInstallationStore(factory);
        var toolStore = new EnvironmentToolStore(factory);
        var blueprintStore = new BlueprintMetadataStore(factory);
        var profileStore = new TeamProfileStore(factory);
        var presetStore = new PresetStore(factory);
        var recentStore = new RecentProjectStore(factory);

        await ideStore.UpsertAsync(ide, CancellationToken.None);
        await toolStore.UpsertAsync(tool, CancellationToken.None);
        await blueprintStore.UpsertAsync(blueprint, CancellationToken.None);
        await profileStore.UpsertAsync(profile, CancellationToken.None);
        await presetStore.UpsertAsync(preset, CancellationToken.None);
        await recentStore.UpsertAsync(recent, CancellationToken.None);

        Assert.Equal(ide.Id, (await ideStore.GetAsync(ide.Id, CancellationToken.None))?.Id);
        Assert.Equal(tool.Id, (await toolStore.GetAsync(tool.Id, CancellationToken.None))?.Id);
        Assert.Equal(
            blueprint.Checksum,
            (await blueprintStore.GetAsync(blueprint.Id, blueprint.Version, CancellationToken.None))?.Checksum);
        Assert.Equal(profile.Policy, (await profileStore.GetAsync(profile.Id, CancellationToken.None))?.Policy);
        Assert.Equal(preset.Recipe, (await presetStore.GetAsync(preset.Id, CancellationToken.None))?.Recipe);
        Assert.Equal(recent.ProjectPath, (await recentStore.GetAsync(recent.ProjectPath, CancellationToken.None))?.ProjectPath);

        Assert.Single(await ideStore.ListAsync(CancellationToken.None));
        Assert.Single(await toolStore.ListAsync(CancellationToken.None));
        Assert.Single(await blueprintStore.ListAsync(CancellationToken.None));
        Assert.Single(await profileStore.ListAsync(CancellationToken.None));
        Assert.Single(await presetStore.ListAsync(CancellationToken.None));
        Assert.Single(await recentStore.ListAsync(CancellationToken.None));

        Assert.True(await ideStore.RemoveAsync(ide.Id, CancellationToken.None));
        Assert.True(await toolStore.RemoveAsync(tool.Id, CancellationToken.None));
        Assert.True(await blueprintStore.RemoveAsync(blueprint.Id, blueprint.Version, CancellationToken.None));
        Assert.True(await profileStore.RemoveAsync(profile.Id, CancellationToken.None));
        Assert.True(await presetStore.RemoveAsync(preset.Id, CancellationToken.None));
        Assert.True(await recentStore.RemoveAsync(recent.ProjectPath, CancellationToken.None));
    }

    [Fact]
    public async Task PreCancelledWriteDoesNotMutateDatabase()
    {
        await using var database = PersistenceTestDatabase.Create();
        var factory = await CreateMigratedFactoryAsync(database);
        var store = new AppSettingsStore(factory);
        var setting = AppSetting.Create(
            "ui.theme",
            AppSettingValue.CreateString("dark").Value,
            DateTimeOffset.UnixEpoch).Value;
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.UpsertAsync(setting, cancellation.Token));

        Assert.Empty(await store.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ReturnedSnapshotsRemainDetachedFromLaterUpdates()
    {
        await using var database = PersistenceTestDatabase.Create();
        var factory = await CreateMigratedFactoryAsync(database);
        var store = new TeamProfileStore(factory);
        var originalPolicy = PersistableJson.Create("{\"branch\":\"main\"}").Value;
        var updatedPolicy = PersistableJson.Create("{\"branch\":\"develop\"}").Value;
        var original = TeamProfileRecord.Create(
            "team.standard",
            "Original",
            1,
            originalPolicy,
            DateTimeOffset.UnixEpoch).Value;
        var updated = TeamProfileRecord.Create(
            "team.standard",
            "Updated",
            1,
            updatedPolicy,
            DateTimeOffset.UnixEpoch.AddMinutes(1)).Value;

        await store.UpsertAsync(original, CancellationToken.None);
        var firstSnapshot = await store.ListAsync(CancellationToken.None);
        await store.UpsertAsync(updated, CancellationToken.None);
        var secondSnapshot = await store.ListAsync(CancellationToken.None);

        Assert.Equal("Original", Assert.Single(firstSnapshot).Name);
        Assert.Equal(originalPolicy, Assert.Single(firstSnapshot).Policy);
        Assert.Equal("Updated", Assert.Single(secondSnapshot).Name);
        Assert.Equal(updatedPolicy, Assert.Single(secondSnapshot).Policy);
    }

    [Fact]
    public async Task CorruptPersistedMetadataFailsClosedWithoutEchoingStoredValue()
    {
        await using var database = PersistenceTestDatabase.Create();
        var factory = await CreateMigratedFactoryAsync(database);
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO AppSettings (Key, ValueKind, SerializedValue, UpdatedAtUnixMs) "
                + "VALUES ('ui.theme', 'Text', '<corrupt>', 9223372036854775807)");
        }

        var store = new AppSettingsStore(factory);
        var exception = await Assert.ThrowsAsync<PersistenceDataException>(
            () => store.GetAsync("ui.theme", CancellationToken.None));

        Assert.Equal("DF-DB-001", exception.Code);
        Assert.DoesNotContain("<corrupt>", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NonCanonicalPersistedEnumFailsClosed()
    {
        await using var database = PersistenceTestDatabase.Create();
        var factory = await CreateMigratedFactoryAsync(database);
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO IdeInstallations "
                + "(Id, Kind, ExecutablePath, Version, ValidationState, ScannedAtUnixMs) "
                + "VALUES ('vscode', '1', 'C:\\Tools\\Code.exe', NULL, 'Valid', 0)");
        }

        var store = new IdeInstallationStore(factory);
        await Assert.ThrowsAsync<PersistenceDataException>(
            () => store.GetAsync("vscode", CancellationToken.None));
    }

    private static async Task<DevForgeDbContextFactory> CreateMigratedFactoryAsync(
        PersistenceTestDatabase database)
    {
        var factory = new DevForgeDbContextFactory(database.Location);
        await using var context = factory.CreateDbContext();
        await context.Database.MigrateAsync(CancellationToken.None);
        return factory;
    }
}
