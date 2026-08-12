namespace DevForge.Desktop.Theming;

public sealed class WpfThemeResourceHost : IThemeResourceHost
{
    private readonly System.Windows.Application _application;
    private System.Windows.ResourceDictionary? _currentDictionary;

    public WpfThemeResourceHost(System.Windows.Application application)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
    }

    public void Apply(EffectiveTheme theme)
    {
        if (!Enum.IsDefined(theme))
        {
            throw new ArgumentOutOfRangeException(nameof(theme));
        }

        _application.Dispatcher.Invoke(() => Replace(theme));
    }

    private void Replace(EffectiveTheme theme)
    {
        var dictionaries = _application.Resources.MergedDictionaries;
        if (_currentDictionary is not null)
        {
            dictionaries.Remove(_currentDictionary);
        }

        _currentDictionary = new System.Windows.ResourceDictionary
        {
            Source = new Uri(
                $"/DevForge.Desktop;component/Resources/Colors.{theme}.xaml",
                UriKind.Relative),
        };
        dictionaries.Insert(0, _currentDictionary);
    }
}
