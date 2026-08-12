namespace DevForge.Desktop.Theming;

public sealed class ThemeService : IThemeService, IDisposable
{
    private readonly ISystemThemeSource _systemThemeSource;
    private readonly IThemeResourceHost _resourceHost;
    private ThemePreference _preference;
    private EffectiveTheme? _effectiveTheme;
    private bool _disposed;

    public ThemeService(ISystemThemeSource systemThemeSource, IThemeResourceHost resourceHost)
    {
        _systemThemeSource = systemThemeSource ?? throw new ArgumentNullException(nameof(systemThemeSource));
        _resourceHost = resourceHost ?? throw new ArgumentNullException(nameof(resourceHost));
        _systemThemeSource.Changed += OnSystemThemeChanged;
    }

    public EffectiveTheme EffectiveTheme => _effectiveTheme ?? _systemThemeSource.Current;

    public void Apply(ThemePreference preference)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Enum.IsDefined(preference))
        {
            throw new ArgumentOutOfRangeException(nameof(preference));
        }

        _preference = preference;
        ApplyEffective(Resolve(preference));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _systemThemeSource.Changed -= OnSystemThemeChanged;
        _disposed = true;
    }

    private EffectiveTheme Resolve(ThemePreference preference)
    {
        return preference switch
        {
            ThemePreference.System => _systemThemeSource.Current,
            ThemePreference.Light => EffectiveTheme.Light,
            ThemePreference.Dark => EffectiveTheme.Dark,
            _ => throw new ArgumentOutOfRangeException(nameof(preference)),
        };
    }

    private void ApplyEffective(EffectiveTheme theme)
    {
        if (_effectiveTheme == theme)
        {
            return;
        }

        _resourceHost.Apply(theme);
        _effectiveTheme = theme;
    }

    private void OnSystemThemeChanged(object? sender, EventArgs args)
    {
        if (!_disposed && _preference == ThemePreference.System)
        {
            ApplyEffective(_systemThemeSource.Current);
        }
    }
}
