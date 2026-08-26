using DevForge.Application.Contracts;
using DevForge.Domain.Diagnostics;
using DevForge.Domain.Privacy;

namespace DevForge.Application.Diagnostics;

public sealed class SupportBundleCoordinator(
    IRunCheckpointStore checkpoints,
    ISupportBundleWriter writer) : ISupportBundleCoordinator
{
    private readonly IRunCheckpointStore _checkpoints =
        checkpoints ?? throw new ArgumentNullException(nameof(checkpoints));
    private readonly ISupportBundleWriter _writer =
        writer ?? throw new ArgumentNullException(nameof(writer));

    public async Task<ExecutionOperationResult<SupportBundleReceipt>> ExportAsync(
        SupportBundleRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var checkpoint = await _checkpoints.FindAsync(request.RunId, cancellationToken)
                .ConfigureAwait(false);
            if (checkpoint is null
                || !StringComparer.Ordinal.Equals(checkpoint.Run.Id, request.RunId))
            {
                return Failure();
            }

            return await _writer.WriteAsync(
                checkpoint,
                request.IncludeEnvironmentSnapshot,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Failure();
        }
    }

    private static ExecutionOperationResult<SupportBundleReceipt> Failure() =>
        ExecutionOperationResult.Failure<SupportBundleReceipt>(
            DevForgeError.Create(
                "DF-SUPPORT-001",
                "The support bundle could not be created safely.",
                RedactedText.FromTrustedRedaction(
                    "The selected run or its owned diagnostics are unavailable.").Value,
                "support-export",
                null,
                isRetryable: true,
                ["Retry the export or review local diagnostics."],
                []).Value);
}
