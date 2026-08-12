using DevForge.Desktop.Notifications;
using DevForge.Desktop.Settings;
using DevForge.Desktop.Theming;
using DevForge.Domain.Validation;

namespace DevForge.E2ETests.Desktop;

public sealed class SettingsViewModelTests
{
    [Fact]
    public async Task LoadPopulatesChecklistAndEditableValues()
    {
        var service = new FakeSettingsService
        {
            Loaded = new DesktopSettings(@"C:\Projects", "rider", "leader", "vi-VN", ThemePreference.Dark, true),
        };
        var sut = new SettingsViewModel(service, new FakeThemeService(), new NotificationService());

        await sut.LoadAsync(CancellationToken.None);

        Assert.Equal(@"C:\Projects", sut.DefaultProjectRoot);
        Assert.True(sut.HasProjectRoot);
        Assert.True(sut.HasIdeSelection);
        Assert.True(sut.HasTeamProfile);
        Assert.True(sut.HasLanguage);
        Assert.True(sut.OnboardingCompleted);
    }

    [Fact]
    public async Task SaveAppliesThemeOnlyAfterValidPersistence()
    {
        var service = new FakeSettingsService();
        var theme = new FakeThemeService();
        var notifications = new NotificationService();
        var sut = new SettingsViewModel(service, theme, notifications)
        {
            DefaultProjectRoot = @"C:\Projects",
            DefaultIdeId = "rider",
            DefaultTeamProfileId = "leader",
            CultureName = "en-US",
            Theme = ThemePreference.Dark,
            OnboardingCompleted = true,
        };

        await sut.SaveAsync(CancellationToken.None);

        Assert.Equal(ThemePreference.Dark, theme.Applied);
        Assert.Single(notifications.Items);
        Assert.Equal(NotificationSeverity.Information, notifications.Items[0].Severity);
    }

    [Fact]
    public async Task InvalidSaveShowsValidationAndDoesNotApplyTheme()
    {
        var service = new FakeSettingsService { RejectSave = true };
        var theme = new FakeThemeService();
        var sut = new SettingsViewModel(service, theme, new NotificationService());

        await sut.SaveAsync(CancellationToken.None);

        Assert.Null(theme.Applied);
        Assert.Single(sut.ValidationMessages);
    }

    [Fact]
    public async Task SafeModeDisablesSaveWithoutCallingService()
    {
        var service = new FakeSettingsService();
        var sut = new SettingsViewModel(service, new FakeThemeService(), new NotificationService(), isReadOnly: true);

        await sut.SaveAsync(CancellationToken.None);

        Assert.Equal(0, service.SaveCalls);
        Assert.False(sut.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void EnteringSafeModeAfterStartupDisablesSave()
    {
        var sut = new SettingsViewModel(
            new FakeSettingsService(),
            new FakeThemeService(),
            new NotificationService());
        Assert.True(sut.SaveCommand.CanExecute(null));

        sut.EnterReadOnlyMode();

        Assert.False(sut.SaveCommand.CanExecute(null));
    }

    private sealed class FakeSettingsService : IDesktopSettingsService
    {
        public DesktopSettings Loaded { get; set; } =
            new(string.Empty, "none", "none", "en-US", ThemePreference.System, false);

        public bool RejectSave { get; set; }

        public int SaveCalls { get; private set; }

        public Task<DesktopSettings> LoadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Loaded);
        }

        public Task<ValidationResult<DesktopSettings>> SaveAsync(
            DesktopSettingsDraft draft,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCalls++;
            if (RejectSave)
            {
                return Task.FromResult(ValidationResult.Failure<DesktopSettings>(
                [
                    new ValidationIssue("desktop.settings.invalid", "Choose valid settings.", "settings"),
                ]));
            }

            return Task.FromResult(ValidationResult.Success(new DesktopSettings(
                draft.DefaultProjectRoot ?? string.Empty,
                draft.DefaultIdeId ?? "none",
                draft.DefaultTeamProfileId ?? "none",
                draft.CultureName ?? "en-US",
                draft.Theme,
                draft.OnboardingCompleted)));
        }
    }

    private sealed class FakeThemeService : IThemeService
    {
        public ThemePreference? Applied { get; private set; }

        public void Apply(ThemePreference preference) => Applied = preference;
    }
}
