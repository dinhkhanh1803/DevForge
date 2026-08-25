using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TeamTool.Application;

namespace TeamTool.Desktop;

public sealed partial class MainViewModel(IStatusService statusService) : ObservableObject
{
    [ObservableProperty]
    private string _status = statusService.GetCurrent().Message;

    [RelayCommand]
    private void Refresh()
    {
        Status = statusService.GetCurrent().Message;
    }
}
