using TeamTool.Domain;

namespace TeamTool.Application;

public interface IStatusService
{
    ToolStatus GetCurrent();
}
