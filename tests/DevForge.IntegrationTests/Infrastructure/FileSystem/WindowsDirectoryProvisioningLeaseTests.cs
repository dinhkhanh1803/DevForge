using DevForge.Application.Contracts;
using DevForge.Infrastructure.FileSystem;

namespace DevForge.IntegrationTests.Infrastructure.FileSystem;

public sealed class WindowsDirectoryProvisioningLeaseTests : IDisposable
{
    private readonly string _container = Path.Combine(
        Path.GetTempPath(),
        "DevForge-M10-Provision-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void HoldsVerifiedAncestorsAgainstReplacementUntilDisposed()
    {
        var target = Path.Combine(_container, "local", "data");
        var root = WorkspaceRoot.Create(target).Value;

        using (WindowsDirectoryProvisioningLease.Acquire(root))
        {
            Assert.True(Directory.Exists(target));
            Assert.ThrowsAny<IOException>(() => Directory.Move(
                Path.Combine(_container, "local"),
                Path.Combine(_container, "replaced")));
        }

        Directory.Move(
            Path.Combine(_container, "local"),
            Path.Combine(_container, "replaced"));
        Assert.True(Directory.Exists(Path.Combine(_container, "replaced", "data")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_container))
        {
            Directory.Delete(_container, recursive: true);
        }
    }
}
