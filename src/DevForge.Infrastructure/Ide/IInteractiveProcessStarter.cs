using System.Diagnostics;

namespace DevForge.Infrastructure.Ide;

internal interface IInteractiveProcessStarter
{
    Process Start(ProcessStartInfo startInfo);
}
