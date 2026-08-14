using DevForge.Application.Contracts;
using DevForge.Domain.Diagnostics;
using DevForge.Domain.Privacy;

namespace DevForge.Infrastructure.Publication;

public sealed class AtomicPublicationReceiptStore : IPublicationReceiptStore
{
    public async Task<ExecutionOperationResult<PublicationReceiptWriteResult>> WriteOrVerifyAsync(
        PublicationReceiptWriteRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (await request.Workspace.FileExistsAsync(request.Path, cancellationToken)
                    .ConfigureAwait(false))
            {
                return await VerifyExistingAsync(request, cancellationToken).ConfigureAwait(false);
            }

            if (request.AccessMode == PublicationReceiptAccessMode.VerifyOnly)
            {
                return Failure("The durable publication receipt is missing.");
            }

            if (request.Workspace is not IAtomicFileWorkspaceFileSystem atomic)
            {
                return Failure("The run-artifact workspace cannot publish atomically.");
            }

            try
            {
                await atomic.WriteFileAtomicallyAsync(
                    request.Path,
                    request.Body,
                    overwrite: false,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (InfrastructureOperationException)
            {
                if (await request.Workspace.FileExistsAsync(request.Path, CancellationToken.None)
                        .ConfigureAwait(false))
                {
                    return await VerifyExistingAsync(request, CancellationToken.None)
                        .ConfigureAwait(false);
                }

                throw;
            }

            var verified = await VerifyExistingAsync(request, cancellationToken).ConfigureAwait(false);
            return verified.IsSuccessful
                ? ExecutionOperationResult.Success(new PublicationReceiptWriteResult(
                    request.Path,
                    request.BodyDigest,
                    AdoptedExisting: false))
                : verified;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return Failure("The publication receipt could not be written or verified safely.");
        }
    }

    private static async Task<ExecutionOperationResult<PublicationReceiptWriteResult>> VerifyExistingAsync(
        PublicationReceiptWriteRequest request,
        CancellationToken cancellationToken)
    {
        await using var stream = await request.Workspace.OpenReadAsync(
            request.Path,
            cancellationToken).ConfigureAwait(false);
        var buffer = new byte[PublicationReceiptWriteRequest.MaximumBodyBytes + 1];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        if (total != request.Body.Length
            || total > PublicationReceiptWriteRequest.MaximumBodyBytes
            || !buffer.AsSpan(0, total).SequenceEqual(request.Body.Span))
        {
            return Failure("The existing publication receipt does not match durable intent.");
        }

        return ExecutionOperationResult.Success(new PublicationReceiptWriteResult(
            request.Path,
            request.BodyDigest,
            AdoptedExisting: true));
    }

    private static ExecutionOperationResult<PublicationReceiptWriteResult> Failure(string summary) =>
        ExecutionOperationResult.Failure<PublicationReceiptWriteResult>(DevForgeError.Create(
            "DF-PUB-RECEIPT",
            summary,
            RedactedText.FromTrustedRedaction(summary).Value,
            "publication",
            null,
            true,
            [],
            []).Value);

    private static bool IsExpected(Exception exception) => exception is InfrastructureOperationException
        or IOException
        or UnauthorizedAccessException
        or System.Security.SecurityException;
}
