using DevForge.Application.Contracts;

namespace DevForge.Desktop.Navigation;

public sealed class ProjectCreationSelection
{
    public BlueprintReference? Blueprint { get; private set; }

    public void Select(BlueprintReference blueprint)
    {
        Blueprint = blueprint ?? throw new ArgumentNullException(nameof(blueprint));
    }
}
