using DevForge.Application.Contracts;
using DevForge.Domain.Validation;

namespace DevForge.UnitTests.Application.Publication;

public sealed class PublicationWorkflowContractTests
{
    [Fact]
    public void RequestRequiresCanonicalRunIdAndDefinedMutationMode()
    {
        var valid = PublicationRequest.Create("run-0123456789abcdef0123456789abcdef", PublicationMutationMode.Normal);
        var invalid = PublicationRequest.Create("../run", (PublicationMutationMode)999);

        Assert.True(valid.IsValid);
        Assert.Equal("run-0123456789abcdef0123456789abcdef", valid.Value.RunId);
        Assert.Equal(PublicationMutationMode.Normal, valid.Value.MutationMode);
        Assert.False(invalid.IsValid);
        Assert.Contains(invalid.Issues, issue => issue.Code == "publication.request.run-id.invalid");
        Assert.Contains(invalid.Issues, issue => issue.Code == "publication.request.mode.invalid");
    }

    [Fact]
    public void PublicationPortsExposeOnlyTypedGuardedOperations()
    {
        Assert.Single(typeof(IProjectPublicationCoordinator).GetMethods());
        Assert.Single(typeof(IPublicationLeaseProvider).GetMethods());
        Assert.Single(typeof(IProjectPublicationWorkspaceFactory).GetMethods());
        Assert.Single(typeof(IPublicationReceiptStore).GetMethods());
        Assert.Single(typeof(IPublicationNonceGenerator).GetMethods());
        Assert.Equal(typeof(IAsyncDisposable), typeof(IPublicationLease).GetInterfaces().Single());
    }

    [Fact]
    public void GitAndGitHubServicesExposeDurablePhaseObserverOverloads()
    {
        Assert.Equal(2, typeof(IPublicationGitService).GetMethods().Length);
        Assert.Contains(
            typeof(IPublicationGitService).GetMethods().Single(method =>
                method.GetParameters().Length == 3).GetParameters(),
            parameter => parameter.ParameterType == typeof(IGitPublicationProgress));
        Assert.Equal(2, typeof(IPublicationGitHubService).GetMethods().Length);
        Assert.Contains(
            typeof(IPublicationGitHubService).GetMethods().Single(method =>
                method.GetParameters().Length == 3).GetParameters(),
            parameter => parameter.ParameterType == typeof(IGitHubPublicationProgress));
    }

    [Theory]
    [InlineData(PublicationMutationMode.Normal)]
    [InlineData(PublicationMutationMode.SafeReadOnly)]
    public void MutationModesUseStableNonzeroValues(PublicationMutationMode mode)
    {
        Assert.True((int)mode > 0);
        Assert.True(Enum.IsDefined(mode));
    }

    [Fact]
    public void ReceiptAccessModesUseStableNonzeroValues()
    {
        Assert.Equal([1, 2], Enum.GetValues<PublicationReceiptAccessMode>().Select(value => (int)value));
    }
}
