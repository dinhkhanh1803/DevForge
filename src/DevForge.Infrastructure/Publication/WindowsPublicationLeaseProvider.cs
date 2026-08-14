using DevForge.Application.Contracts;
using DevForge.Domain.Diagnostics;
using DevForge.Domain.Privacy;

namespace DevForge.Infrastructure.Publication;

public sealed class WindowsPublicationLeaseProvider(
    IFileSystem fileSystem,
    WorkspaceRoot localDataRoot) : IPublicationLeaseProvider
{
    private readonly IFileSystem _fileSystem = fileSystem
        ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly WorkspaceRoot _localDataRoot = localDataRoot
        ?? throw new ArgumentNullException(nameof(localDataRoot));

    public async Task<ExecutionOperationResult<IPublicationLease>> AcquireAsync(
        string runId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!PublicationRequest.Create(runId, PublicationMutationMode.Normal).IsValid)
        {
            return Failure("DF-PUB-LEASE", "The publication lease identity is invalid.");
        }

        try
        {
            var workspace = await _fileSystem.OpenWorkspaceAsync(
                _localDataRoot,
                cancellationToken).ConfigureAwait(false);
            if (workspace is not IExclusiveLeaseWorkspaceFileSystem exclusive)
            {
                return Failure("DF-PUB-LEASE", "The workspace cannot provide an exclusive publication lease.");
            }

            var path = WorkspaceRelativePath.Create($"runs\\{runId}\\publication.lock").Value;
            var lease = await exclusive.TryAcquireExclusiveLeaseAsync(path, cancellationToken)
                .ConfigureAwait(false);
            return lease is null
                ? Failure("DF-PUB-LEASE", "Another process owns the publication lease.")
                : ExecutionOperationResult.Success<IPublicationLease>(new PublicationLease(lease));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return Failure("DF-PUB-LEASE", "The publication lease could not be acquired safely.");
        }
    }

    private static ExecutionOperationResult<IPublicationLease> Failure(string code, string summary) =>
        ExecutionOperationResult.Failure<IPublicationLease>(DevForgeError.Create(
            code,
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

    private sealed class PublicationLease(IWorkspaceExclusiveLease lease) : IPublicationLease
    {
        public ValueTask DisposeAsync() => lease.DisposeAsync();
    }
}
