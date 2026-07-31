using System.Reflection;
using System.Runtime.CompilerServices;
using DevForge.Application.Contracts;

namespace DevForge.UnitTests.Application;

public sealed class ProcessFriendBoundaryTests
{
    [Fact]
    public void SensitiveRevealIsInternalAndOnlyInfrastructureIsAFriendAssembly()
    {
        var reveal = Assert.Single(
            typeof(SensitiveProcessValue).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic),
            method => method.Name == "RevealForProcessStart");
        var friends = typeof(SensitiveProcessValue).Assembly
            .GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(attribute => attribute.AssemblyName)
            .ToArray();

        Assert.True(reveal.IsAssembly);
        Assert.False(reveal.IsPublic);
        Assert.Equal(["DevForge.Infrastructure"], friends);
    }
}
