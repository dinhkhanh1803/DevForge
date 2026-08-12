using DevForge.Application.Contracts;
using DevForge.Application.Contracts.Persistence;
using DevForge.Domain.Environment;
using DevForge.Infrastructure.Environment;

namespace DevForge.Desktop.Bootstrap;

internal sealed class DeferredEnvironmentDoctor : IEnvironmentDoctor
{
    private readonly IProcessRunner _processRunner;
    private readonly IFileSystem _fileSystem;
    private readonly DatabaseLocation _location;
    private readonly TimeProvider _timeProvider;

    public DeferredEnvironmentDoctor(
        IProcessRunner processRunner,
        IFileSystem fileSystem,
        DatabaseLocation location,
        TimeProvider timeProvider)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _location = location ?? throw new ArgumentNullException(nameof(location));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<EnvironmentSnapshot> InspectAsync(CancellationToken cancellationToken)
    {
        var root = WorkspaceRoot.Create(_location.LocalDataRoot);
        if (!root.IsValid)
        {
            throw new InvalidOperationException("The environment probe workspace is invalid.");
        }

        var workspace = await _fileSystem.OpenWorkspaceAsync(root.Value, cancellationToken)
            .ConfigureAwait(false);
        return await new WindowsEnvironmentDoctor(_processRunner, workspace, _timeProvider)
            .InspectAsync(cancellationToken).ConfigureAwait(false);
    }
}
