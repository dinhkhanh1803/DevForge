using System.Collections.Immutable;
using DevForge.Application.Contracts;

namespace DevForge.Infrastructure.Processes;

internal interface ITrustedExecutableResolver
{
    TrustedExecutableLaunch Resolve(ExecutableIdentity executable);
}

internal sealed record TrustedExecutableLaunch(
    string ExecutablePath,
    ImmutableArray<string> PrefixArguments);
