using System.IO;
using Microsoft.Win32;

namespace DevForge.Desktop.Theming;

public sealed class WindowsSystemThemeSource : ISystemThemeSource, IDisposable
{
    private const string PersonalizeKey =
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private bool _disposed;

    public WindowsSystemThemeSource()
    {
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public EffectiveTheme Current => ReadCurrent();

    public event EventHandler? Changed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _disposed = true;
    }

    private static EffectiveTheme ReadCurrent()
    {
        try
        {
            return Registry.GetValue(PersonalizeKey, "AppsUseLightTheme", 1) is int value && value == 0
                ? EffectiveTheme.Dark
                : EffectiveTheme.Light;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return EffectiveTheme.Light;
        }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs args)
    {
        if (!_disposed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
