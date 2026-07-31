using DevForge.Domain.Environment;

namespace DevForge.Application.Contracts;

public interface IEnvironmentDoctor
{
    Task<EnvironmentSnapshot> InspectAsync(CancellationToken cancellationToken);
}
