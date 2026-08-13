using System.Windows.Controls;
using DevForge.Desktop.CreateProject;

namespace DevForge.E2ETests.Desktop;

[Collection(WpfUiTestGroup.Name)]
public sealed class DynamicInputTemplateTests
{
    [Fact]
    public void ConfigureViewIsCompiledAsNativeWpfUserControl()
    {
        Assert.True(typeof(UserControl).IsAssignableFrom(typeof(CreateProjectView)));
        Assert.NotNull(typeof(CreateProjectView).GetMethod(nameof(CreateProjectView.InitializeComponent)));
    }
}
