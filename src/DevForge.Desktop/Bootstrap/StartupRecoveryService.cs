using DevForge.Application.Contracts;

namespace DevForge.Desktop.Bootstrap;

public sealed class StartupRecoveryService : IStartupRecoveryService
{
    private readonly IRunRecoveryService _recoveryService;

    public StartupRecoveryService(IRunRecoveryService recoveryService)
    {
        _recoveryService = recoveryService ?? throw new ArgumentNullException(nameof(recoveryService));
    }

    public async Task<bool> RecoverAsync(CancellationToken cancellationToken)
    {
        var result = await _recoveryService.RecoverInterruptedAsync(cancellationToken).ConfigureAwait(false);
        return result.IsSuccessful;
    }
}
