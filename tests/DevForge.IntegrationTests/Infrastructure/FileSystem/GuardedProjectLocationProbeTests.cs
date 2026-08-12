using DevForge.Application.Contracts;
using DevForge.Infrastructure.FileSystem;

namespace DevForge.IntegrationTests.Infrastructure.FileSystem;

public sealed class GuardedProjectLocationProbeTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "DevForge-ProjectProbeTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ReportsExistingCanonicalDirectoryAvailable()
    {
        Directory.CreateDirectory(_root);
        var sut = new GuardedProjectLocationProbe(new WindowsFileSystem());

        var result = await sut.InspectAsync(_root, CancellationToken.None);

        Assert.Equal(ProjectLocationStatus.Available, result);
    }

    [Fact]
    public async Task ReportsMissingDirectoryUnavailableAndMalformedRootInvalid()
    {
        var sut = new GuardedProjectLocationProbe(new WindowsFileSystem());

        Assert.Equal(
            ProjectLocationStatus.Unavailable,
            await sut.InspectAsync(_root, CancellationToken.None));
        Assert.Equal(
            ProjectLocationStatus.Invalid,
            await sut.InspectAsync("relative", CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
