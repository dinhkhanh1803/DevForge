using DevForge.Application.Contracts;

namespace DevForge.Infrastructure.Processes;

internal interface ITrustedExecutableResolver
{
    string Resolve(ExecutableIdentity executable);
}
