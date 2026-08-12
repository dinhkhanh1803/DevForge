namespace DevForge.Desktop.Theming;

public interface ISystemThemeSource
{
    EffectiveTheme Current { get; }

    event EventHandler? Changed;
}

public interface IThemeResourceHost
{
    void Apply(EffectiveTheme theme);
}
