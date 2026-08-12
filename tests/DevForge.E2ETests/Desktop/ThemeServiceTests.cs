using DevForge.Desktop.Theming;

namespace DevForge.E2ETests.Desktop;

public sealed class ThemeServiceTests
{
    [Fact]
    public void SystemPreferenceFollowsSystemChanges()
    {
        var source = new FakeSystemThemeSource(EffectiveTheme.Light);
        var host = new FakeThemeResourceHost();
        using var sut = new ThemeService(source, host);

        sut.Apply(ThemePreference.System);
        source.ChangeTo(EffectiveTheme.Dark);

        Assert.Equal([EffectiveTheme.Light, EffectiveTheme.Dark], host.Applied);
        Assert.Equal(EffectiveTheme.Dark, sut.EffectiveTheme);
    }

    [Theory]
    [InlineData(ThemePreference.Light, EffectiveTheme.Light)]
    [InlineData(ThemePreference.Dark, EffectiveTheme.Dark)]
    public void ExplicitPreferenceIgnoresSystemChanges(
        ThemePreference preference,
        EffectiveTheme expected)
    {
        var source = new FakeSystemThemeSource(EffectiveTheme.Light);
        var host = new FakeThemeResourceHost();
        using var sut = new ThemeService(source, host);

        sut.Apply(preference);
        source.ChangeTo(expected == EffectiveTheme.Light ? EffectiveTheme.Dark : EffectiveTheme.Light);

        Assert.Equal([expected], host.Applied);
    }

    [Fact]
    public void ReapplyingSameEffectiveThemeDoesNotDuplicateResources()
    {
        var host = new FakeThemeResourceHost();
        using var sut = new ThemeService(new FakeSystemThemeSource(EffectiveTheme.Light), host);

        sut.Apply(ThemePreference.Light);
        sut.Apply(ThemePreference.Light);
        sut.Apply(ThemePreference.System);

        Assert.Single(host.Applied);
    }

    [Fact]
    public void DisposeStopsSystemObservation()
    {
        var source = new FakeSystemThemeSource(EffectiveTheme.Light);
        var host = new FakeThemeResourceHost();
        var sut = new ThemeService(source, host);
        sut.Apply(ThemePreference.System);

        sut.Dispose();
        source.ChangeTo(EffectiveTheme.Dark);

        Assert.Equal([EffectiveTheme.Light], host.Applied);
    }

    private sealed class FakeSystemThemeSource(EffectiveTheme current) : ISystemThemeSource
    {
        public EffectiveTheme Current { get; private set; } = current;

        public event EventHandler? Changed;

        public void ChangeTo(EffectiveTheme theme)
        {
            Current = theme;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class FakeThemeResourceHost : IThemeResourceHost
    {
        public List<EffectiveTheme> Applied { get; } = [];

        public void Apply(EffectiveTheme theme) => Applied.Add(theme);
    }
}
