namespace DevForge.Application.Contracts;

public enum ProjectLocationStatus
{
    Available = 1,
    Unavailable = 2,
    Invalid = 3,
}

public interface IProjectLocationProbe
{
    Task<ProjectLocationStatus> InspectAsync(
        string? canonicalRoot,
        CancellationToken cancellationToken);
}
