using DevForge.Application.Contracts;
using DevForge.Application.Contracts.Persistence;

namespace DevForge.Infrastructure.Persistence;

public sealed class LocalDataRootProvisioner : ILocalDataRootProvisioner
{
    public Task EnsureExistsAsync(DatabaseLocation location, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            Directory.CreateDirectory(location.LocalDataRoot);
            return Task.CompletedTask;
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            throw new InfrastructureOperationException(
                "DF-FS-001",
                "The local application data root could not be prepared.");
        }
    }

    private static bool IsExpectedFailure(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException
            or ArgumentException
            or NotSupportedException;
}
