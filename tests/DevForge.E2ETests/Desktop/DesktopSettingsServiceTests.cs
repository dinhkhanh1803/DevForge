using System.Collections.Immutable;
using DevForge.Application.Contracts.Persistence;
using DevForge.Desktop.Settings;
using DevForge.Desktop.Theming;

namespace DevForge.E2ETests.Desktop;

public sealed class DesktopSettingsServiceTests
{
    [Fact]
    public async Task LoadUsesSafeDefaultsWhenSettingsAreAbsent()
    {
        var settings = await new DesktopSettingsService(new FakeSettingsStore(), TimeProvider.System)
            .LoadAsync(CancellationToken.None);

        Assert.Equal(ThemePreference.System, settings.Theme);
        Assert.Equal("en-US", settings.CultureName);
        Assert.Equal(string.Empty, settings.DefaultProjectRoot);
        Assert.Equal("none", settings.DefaultIdeId);
        Assert.Equal("none", settings.DefaultTeamProfileId);
        Assert.False(settings.OnboardingCompleted);
    }

    [Theory]
    [InlineData("relative")]
    [InlineData(@"\\server\share")]
    [InlineData(@"C:\safe\..\escape")]
    [InlineData(@"C:\CON")]
    public async Task SaveRejectsInvalidProjectRootWithoutWriting(string root)
    {
        var store = new FakeSettingsStore();
        var sut = new DesktopSettingsService(store, TimeProvider.System);

        var result = await sut.SaveAsync(
            new DesktopSettingsDraft(root, "none", "none", "en-US", ThemePreference.System, false),
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Empty(store.Writes);
    }

    [Fact]
    public async Task SaveWritesCompleteValidatedSnapshotWithOneTimestamp()
    {
        var store = new FakeSettingsStore();
        var now = new DateTimeOffset(2026, 8, 12, 7, 30, 0, TimeSpan.Zero);
        var sut = new DesktopSettingsService(store, new FixedTimeProvider(now));

        var result = await sut.SaveAsync(
            new DesktopSettingsDraft(@"C:\Projects", "rider", "leader", "vi-VN", ThemePreference.Dark, true),
            CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal(6, store.Writes.Count);
        Assert.All(store.Writes, item => Assert.Equal(now, item.UpdatedAt));
        Assert.Equal(6, store.Writes.Select(item => item.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("fr-FR", "rider", "leader", 1)]
    [InlineData("en-US", "githubToken", "leader", 1)]
    [InlineData("en-US", "rider", "databasePassword", 1)]
    [InlineData("en-US", "rider", "leader", 99)]
    public async Task SaveRejectsUnsupportedOrSecretShapedValuesWithoutWriting(
        string culture,
        string ideId,
        string teamId,
        int theme)
    {
        var store = new FakeSettingsStore();
        var sut = new DesktopSettingsService(store, TimeProvider.System);

        var result = await sut.SaveAsync(
            new DesktopSettingsDraft(@"C:\Projects", ideId, teamId, culture, (ThemePreference)theme, false),
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Empty(store.Writes);
    }

    private sealed class FakeSettingsStore : IAppSettingsStore
    {
        private readonly Dictionary<string, AppSetting> _settings = new(StringComparer.Ordinal);

        public List<AppSetting> Writes { get; } = [];

        public Task<AppSetting?> GetAsync(string key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _settings.TryGetValue(key, out var value);
            return Task.FromResult(value);
        }

        public Task<ImmutableArray<AppSetting>> ListAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_settings.Values.ToImmutableArray());
        }

        public Task UpsertAsync(AppSetting setting, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _settings[setting.Key] = setting;
            Writes.Add(setting);
            return Task.CompletedTask;
        }

        public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_settings.Remove(key));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
