using DevForge.Application.Contracts.Persistence;
using DevForge.Infrastructure.Persistence;

namespace DevForge.IntegrationTests.Persistence;

public sealed class LocalDataRootProvisionerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "DevForge-LocalDataTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CreatesOnlyTheValidatedLocalDataRoot()
    {
        var location = DatabaseLocation.Create(_root, "devforge.db").Value;

        await new LocalDataRootProvisioner().EnsureExistsAsync(location, CancellationToken.None);

        Assert.True(Directory.Exists(_root));
        Assert.False(File.Exists(location.DatabasePath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
