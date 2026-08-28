using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TeamTool.Application;
using TeamTool.Infrastructure;

namespace TeamTool.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IStatusService, StatusService>();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainForm>();
        using var host = builder.Build();
        host.Start();
        try
        {
            System.Windows.Forms.Application.Run(host.Services.GetRequiredService<MainForm>());
        }
        finally
        {
            // The message loop has ended; shutdown does not block an active UI thread.
            host.StopAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        }
    }
}
