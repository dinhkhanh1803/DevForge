using System.Collections.Immutable;
using DevForge.Application.Contracts;

namespace DevForge.Infrastructure.Ide;

internal static class IdeCatalog
{
    private static readonly ImmutableDictionary<string, ExecutableIdentity> _identities =
        new Dictionary<string, ExecutableIdentity>(StringComparer.OrdinalIgnoreCase)
        {
            ["vscode"] = ExecutableIdentity.Create("code").Value,
            ["visual-studio"] = ExecutableIdentity.Create("devenv").Value,
        }.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);

    public static ExecutableIdentity Resolve(string ideId)
    {
        if (!_identities.TryGetValue(ideId, out var executable))
        {
            throw new InfrastructureOperationException(
                "DF-IDE-001",
                "The requested IDE is not trusted or available.");
        }

        return executable;
    }
}
