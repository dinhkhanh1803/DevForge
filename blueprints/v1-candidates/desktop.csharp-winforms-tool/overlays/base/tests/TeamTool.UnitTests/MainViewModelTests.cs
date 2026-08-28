using TeamTool.Application;
using TeamTool.Desktop;
using TeamTool.Domain;
using Xunit;

namespace TeamTool.UnitTests;

public sealed class MainViewModelTests
{
    [Fact]
    public void InitialStatusComesFromApplicationService()
    {
        var model = new MainViewModel(new MutableStatusService());
        Assert.Equal("ready", model.Status);
    }

    [Fact]
    public void RefreshUpdatesStatusAndNotifiesBinding()
    {
        var service = new MutableStatusService();
        var model = new MainViewModel(service);
        var notifications = new List<string?>();
        model.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        service.Message = "updated";

        model.RefreshCommand.Execute(null);

        Assert.Equal("updated", model.Status);
        Assert.Equal([nameof(MainViewModel.Status)], notifications);
    }

    private sealed class MutableStatusService : IStatusService
    {
        public string Message { get; set; } = "ready";
        public ToolStatus GetCurrent() => new(Message);
    }
}
