using DevForge.Application.Contracts;

namespace DevForge.Infrastructure.Creation;

public sealed class GuidRunIdentityGenerator : IRunIdentityGenerator
{
    public string CreateRunId()
    {
        return $"run-{Guid.NewGuid():N}";
    }

    public string CreateRecipeId()
    {
        return $"recipe-{Guid.NewGuid():N}";
    }
}
