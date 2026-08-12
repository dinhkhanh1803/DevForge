using System.IO;
using System.Windows;
using DevForge.Desktop.Bootstrap;
using DevForge.Desktop.Dashboard;
using DevForge.Desktop.EnvironmentDoctor;
using DevForge.Desktop.Navigation;
using DevForge.Desktop.Settings;
using DevForge.Desktop.Shell;
using DevForge.Desktop.Theming;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DevForge.Desktop;

public partial class App : System.Windows.Application, IDisposable
{
    private readonly CancellationTokenSource _shutdown = new();
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var localDataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DevForge");
            _host = DesktopHostBuilder.Create(localDataRoot, new WpfThemeResourceHost(this));
            await _host.StartAsync(_shutdown.Token).ConfigureAwait(true);

            var state = await _host.Services
                .GetRequiredService<IDesktopStartupCoordinator>()
                .InitializeAsync(_shutdown.Token)
                .ConfigureAwait(true);
            var shell = _host.Services.GetRequiredService<ShellViewModel>();
            _host.Services.GetRequiredService<SettingsViewModel>().ApplySnapshot(state.Settings);
            _host.Services.GetRequiredService<EnvironmentDoctorViewModel>()
                .ApplySnapshot(state.EnvironmentHealth);
            _host.Services.GetRequiredService<DashboardViewModel>().ApplySnapshot(state.Dashboard);
            if (state.Mode == DesktopStartupMode.SafeReadOnly)
            {
                shell.SetSafeMode(state.UserSafeMessage!);
            }

            _host.Services.GetRequiredService<NavigationService>()
                .TryNavigate(state.InitialRoute);
            var window = _host.Services.GetRequiredService<MainWindow>();
            window.DataContext = shell;
            MainWindow = window;
            window.Show();
        }
        catch (OperationCanceledException)
        {
            Shutdown();
        }
        catch (Exception)
        {
            MessageBox.Show(
                "DevForge Studio could not start safely.",
                "DevForge Studio",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _shutdown.Cancel();
        if (_host is not null)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await _host.StopAsync(timeout.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
            }

        }

        Dispose();
        base.OnExit(e);
    }

    public void Dispose()
    {
        _host?.Dispose();
        _host = null;
        _shutdown.Dispose();
        GC.SuppressFinalize(this);
    }
}
