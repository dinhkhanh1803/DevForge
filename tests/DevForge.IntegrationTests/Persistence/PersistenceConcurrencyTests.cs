using DevForge.Application.Contracts.Persistence;
using DevForge.Infrastructure.Persistence;
using DevForge.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DevForge.IntegrationTests.Persistence;

public sealed class PersistenceConcurrencyTests
{
    [Fact]
    public async Task MultipleStoreInstancesReadConcurrentlyWithoutSharedContext()
    {
        await using var database = PersistenceTestDatabase.Create();
        var factory = await CreateMigratedFactoryAsync(database);
        var writer = new AppSettingsStore(factory);
        await writer.UpsertAsync(CreateSetting("dark", DateTimeOffset.UnixEpoch), CancellationToken.None);
        var readers = Enumerable.Range(0, 32)
            .Select(_ => new AppSettingsStore(factory))
            .ToArray();

        var snapshots = await Task.WhenAll(
            readers.Select(store => store.ListAsync(CancellationToken.None)));

        Assert.All(snapshots, snapshot => Assert.Equal("dark", Assert.Single(snapshot).Value.StringValue));
    }

    [Fact]
    public async Task ConflictingWritesConvergeToNewestTimestamp()
    {
        await using var database = PersistenceTestDatabase.Create();
        var factory = await CreateMigratedFactoryAsync(database);
        var stores = Enumerable.Range(0, 24)
            .Select(_ => new AppSettingsStore(factory))
            .ToArray();
        var newestTimestamp = DateTimeOffset.UnixEpoch.AddDays(1);
        await stores[0].UpsertAsync(
            CreateSetting("newest", newestTimestamp),
            CancellationToken.None);
        var writes = stores.Select((store, index) => store.UpsertAsync(
            CreateSetting($"theme-{index}", DateTimeOffset.UnixEpoch.AddMinutes(index)),
            CancellationToken.None));

        await Task.WhenAll(writes);

        var loaded = await stores[0].GetAsync("ui.theme", CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal("newest", loaded.Value.StringValue);
        Assert.Equal(newestTimestamp, loaded.UpdatedAt);
    }

    [Fact]
    public async Task PreCancelledRemoveDoesNotDeleteExistingValue()
    {
        await using var database = PersistenceTestDatabase.Create();
        var factory = await CreateMigratedFactoryAsync(database);
        var store = new AppSettingsStore(factory);
        await store.UpsertAsync(CreateSetting("dark", DateTimeOffset.UnixEpoch), CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.RemoveAsync("ui.theme", cancellation.Token));

        Assert.NotNull(await store.GetAsync("ui.theme", CancellationToken.None));
    }

    private static AppSetting CreateSetting(string value, DateTimeOffset updatedAt)
    {
        return AppSetting.Create(
            "ui.theme",
            AppSettingValue.CreateString(value).Value,
            updatedAt).Value;
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
