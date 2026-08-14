using DevForge.Application.Contracts;

namespace DevForge.Application.Creation;

public sealed class BlueprintRecoveryInspector(IBlueprintExecutionSource source)
    : IBlueprintRecoveryInspector
{
    private readonly IBlueprintExecutionSource _source =
        source ?? throw new ArgumentNullException(nameof(source));

    public async Task<bool> IsCurrentAsync(
        BlueprintReference blueprint,
        BlueprintFingerprint fingerprint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        ArgumentNullException.ThrowIfNull(fingerprint);
        var result = await _source.OpenAsync(
            blueprint,
            fingerprint,
            cancellationToken).ConfigureAwait(false);
        return result.IsSuccessful;
    }
}
