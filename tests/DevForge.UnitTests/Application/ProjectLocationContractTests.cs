using DevForge.Application.Contracts;

namespace DevForge.UnitTests.Application;

public sealed class ProjectLocationContractTests
{
    [Fact]
    public void StatusValuesAreExplicitAndPortRequiresCancellation()
    {
        Assert.Equal(1, (int)ProjectLocationStatus.Available);
        Assert.Equal(2, (int)ProjectLocationStatus.Unavailable);
        Assert.Equal(3, (int)ProjectLocationStatus.Invalid);

        var method = typeof(IProjectLocationProbe).GetMethod(nameof(IProjectLocationProbe.InspectAsync));
        Assert.NotNull(method);
        Assert.Equal(typeof(CancellationToken), method.GetParameters()[^1].ParameterType);
    }
}
