using System.Runtime.InteropServices;
using DevForge.Application.Planning;

namespace DevForge.Desktop.Bootstrap;

public sealed class DesktopPlanningRuntimeContextProvider : IPlanningRuntimeContextProvider
{
    private static readonly PlanningRuntimeContext _current = PlanningRuntimeContext.Create(
        "1.0.0",
        "windows",
        GetArchitecture()).Value;

    public PlanningRuntimeContext GetCurrent() => _current;

    private static string GetArchitecture() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.Arm64 => "arm64",
        Architecture.X64 => "x64",
        Architecture.X86 => "x86",
        _ => "unknown",
    };
}
