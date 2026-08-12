using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevForge.Desktop.Notifications;
using DevForge.Desktop.Theming;

namespace DevForge.Desktop.Settings;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IDesktopSettingsService _settingsService;
    private readonly IThemeService _themeService;
    private readonly NotificationService _notifications;
    private readonly ObservableCollection<string> _validationMessages = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProjectRoot))]
    private string _defaultProjectRoot = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIdeSelection))]
    private string _defaultIdeId = "none";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTeamProfile))]
    private string _defaultTeamProfileId = "none";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLanguage))]
    private string _cultureName = "en-US";

    [ObservableProperty]
    private ThemePreference _theme = ThemePreference.System;

    [ObservableProperty]
    private bool _onboardingCompleted;

    [ObservableProperty]
    private bool _isBusy;

    public SettingsViewModel(
        IDesktopSettingsService settingsService,
        IThemeService themeService,
        NotificationService notifications,
        bool isReadOnly = false)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        IsReadOnly = isReadOnly;
        ValidationMessages = new ReadOnlyObservableCollection<string>(_validationMessages);
        SaveCommand = new AsyncRelayCommand(SaveFromCommandAsync, CanSave);
        ResetCommand = new AsyncRelayCommand(LoadFromCommandAsync, CanReset);
    }

    public bool IsReadOnly { get; }

    public bool HasProjectRoot => !string.IsNullOrWhiteSpace(DefaultProjectRoot);

    public bool HasIdeSelection => !string.Equals(DefaultIdeId, "none", StringComparison.Ordinal);

    public bool HasTeamProfile => !string.Equals(DefaultTeamProfileId, "none", StringComparison.Ordinal);

    public bool HasLanguage => CultureName is "en-US" or "vi-VN";

    public ReadOnlyObservableCollection<string> ValidationMessages { get; }

    public IAsyncRelayCommand SaveCommand { get; }

    public IAsyncRelayCommand ResetCommand { get; }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        SetBusy(true);
        try
        {
            ApplySnapshot(await _settingsService.LoadAsync(cancellationToken).ConfigureAwait(true));
            _validationMessages.Clear();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            _notifications.TryPublish(
                NotificationSeverity.Error,
                "Settings could not be loaded. Safe defaults remain active.");
        }
        finally
        {
            SetBusy(false);
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (!CanSave())
        {
            return;
        }

        SetBusy(true);
        _validationMessages.Clear();
        try
        {
            var result = await _settingsService.SaveAsync(
                new DesktopSettingsDraft(
                    DefaultProjectRoot,
                    DefaultIdeId,
                    DefaultTeamProfileId,
                    CultureName,
                    Theme,
                    OnboardingCompleted),
                cancellationToken).ConfigureAwait(true);
            if (!result.IsValid)
            {
                foreach (var issue in result.Issues)
                {
                    _validationMessages.Add(issue.Message);
                }

                return;
            }

            ApplySnapshot(result.Value);
            _themeService.Apply(result.Value.Theme);
            _notifications.TryPublish(NotificationSeverity.Information, "Settings saved.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            _notifications.TryPublish(
                NotificationSeverity.Error,
                "Settings could not be saved. Existing settings were kept.");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private bool CanSave() => !IsReadOnly && !IsBusy;

    private bool CanReset() => !IsBusy;

    private Task SaveFromCommandAsync(CancellationToken cancellationToken) => SaveAsync(cancellationToken);

    private Task LoadFromCommandAsync(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    private void ApplySnapshot(DesktopSettings settings)
    {
        DefaultProjectRoot = settings.DefaultProjectRoot;
        DefaultIdeId = settings.DefaultIdeId;
        DefaultTeamProfileId = settings.DefaultTeamProfileId;
        CultureName = settings.CultureName;
        Theme = settings.Theme;
        OnboardingCompleted = settings.OnboardingCompleted;
    }

    private void SetBusy(bool value)
    {
        IsBusy = value;
        SaveCommand.NotifyCanExecuteChanged();
        ResetCommand.NotifyCanExecuteChanged();
    }
}
