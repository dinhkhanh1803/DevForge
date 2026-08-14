using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using DevForge.Desktop.BlueprintCatalog;
using DevForge.Desktop.CreateProject;
using DevForge.Desktop.Dashboard;
using DevForge.Desktop.EnvironmentDoctor;
using DevForge.Desktop.Execution;
using DevForge.Desktop.RunHistory;
using DevForge.Desktop.Settings;

namespace DevForge.E2ETests.Desktop;

[Collection(WpfUiTestGroup.Name)]
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
                new CreateProjectView(),
                new BlueprintCatalogView(),
                new RunHistoryView(),
                new ExecutionCenterView(),
                new LocalReadyView(),
                new DevForge.Desktop.MainWindow(),
            ];

            var createProject = Assert.IsType<CreateProjectView>(views[3]);
            string[] inputTemplateKeys =
            [
                "TextInputTemplate",
                "ChoiceInputTemplate",
                "BooleanInputTemplate",
                "WholeNumberInputTemplate",
            ];
            Assert.All(
                inputTemplateKeys,
                key => Assert.IsType<DataTemplate>(createProject.Resources[key]));
            foreach (var key in inputTemplateKeys)
            {
                var template = Assert.IsType<DataTemplate>(createProject.Resources[key]);
                var input = Assert.IsAssignableFrom<Control>(template.LoadContent());
                Assert.NotNull(System.Windows.Data.BindingOperations.GetBindingExpressionBase(
                    input,
                    AutomationProperties.NameProperty));
            }

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

            foreach (var view in views.Where(view => view is CreateProjectView or SettingsView))
            {
                var inputs = Descendants<Control>(view)
                    .Where(control => control is TextBox or ComboBox)
                    .ToArray();
                Assert.NotEmpty(inputs);
                Assert.All(inputs, input =>
                    Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(input))));
            }

            foreach (var view in views.Where(view =>
                         view is CreateProjectView or BlueprintCatalogView or RunHistoryView or ExecutionCenterView or LocalReadyView))
            {
                Assert.All(
                    Descendants<ListBox>(view),
                    list => Assert.True(VirtualizingPanel.GetIsVirtualizing(list)));
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

    private static IEnumerable<T> Descendants<T>(DependencyObject owner)
        where T : DependencyObject
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(owner); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(owner, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in Descendants<T>(child))
            {
                yield return descendant;
            }
        }
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
