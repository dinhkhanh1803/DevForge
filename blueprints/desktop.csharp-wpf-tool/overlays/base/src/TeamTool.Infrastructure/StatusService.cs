using TeamTool.Application;
using TeamTool.Domain;

namespace TeamTool.Infrastructure;

public sealed class StatusService(TimeProvider timeProvider) : IStatusService
{
    public ToolStatus GetCurrent()
    {
        var timestamp = timeProvider.GetLocalNow().ToString("u");
        return new ToolStatus($"TeamTool is ready at {timestamp}.");
    }
}
