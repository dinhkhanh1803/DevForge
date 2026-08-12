using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using DevForge.Desktop.Dashboard;
using DevForge.Desktop.EnvironmentDoctor;
using DevForge.Desktop.Settings;

namespace DevForge.E2ETests.Desktop;

public sealed class WpfResourceSmokeTests
{
    [Fact]
    public void FunctionalViewsAndResourcesLoadAtMinimumShellSize()
    {
        RunSta(() =>
        {
            var application = new DevForge.Desktop.App();
            application.InitializeComponent();
            application.Resources.MergedDictionaries.Insert(
                0,
                new ResourceDictionary
                {
                    Source = new Uri(
                        "/DevForge.Desktop;component/Resources/Colors.Light.xaml",
                        UriKind.Relative),
                });
            FrameworkElement[] views =
            [
                new DashboardView(),
                new SettingsView(),
                new EnvironmentDoctorView(),
                new DevForge.Desktop.MainWindow(),
            ];

            Size[] dpiEquivalentConstraints =
            {
                new(960, 640),
                new(1200, 800),
                new(1440, 960),
            };

            foreach (var constraint in dpiEquivalentConstraints)
            {
                foreach (var view in views)
                {
                    view.Measure(constraint);
                    view.Arrange(new Rect(new Point(), constraint));
                    Assert.InRange(view.DesiredSize.Width, 0, constraint.Width);
                    Assert.InRange(view.DesiredSize.Height, 0, constraint.Height);
                }
            }

            string[] sources =
            [
                "/DevForge.Desktop;component/Resources/Colors.Light.xaml",
                "/DevForge.Desktop;component/Resources/Colors.Dark.xaml",
                "/DevForge.Desktop;component/Resources/Tokens.xaml",
                "/DevForge.Desktop;component/Resources/Controls.xaml",
            ];

            foreach (var source in sources)
            {
                var dictionary = new ResourceDictionary { Source = new Uri(source, UriKind.Relative) };
                Assert.NotEmpty(dictionary.Keys.Cast<object>());
            }

            application.Dispose();
        });
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
