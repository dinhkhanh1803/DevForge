using System.Security.Cryptography;
using System.Text;
using DevForge.Application.Contracts;
using DevForge.Infrastructure.FileSystem;
using DevForge.Infrastructure.Publication;

namespace DevForge.IntegrationTests.Infrastructure.Publication;

public sealed class PublicationInfrastructureTests : IAsyncLifetime
{
    private readonly string _rootPath = Path.Combine(
        Path.GetTempPath(),
        $"devforge-publication-{Guid.NewGuid():N}");

    [Fact]
    public async Task PublicationLeaseIsExclusiveAcrossProviderInstancesAndReacquirable()
    {
        Directory.CreateDirectory(Path.Combine(_rootPath, "runs", RunId));
        var root = WorkspaceRoot.Create(_rootPath).Value;
        var firstProvider = new WindowsPublicationLeaseProvider(new WindowsFileSystem(), root);
        var secondProvider = new WindowsPublicationLeaseProvider(new WindowsFileSystem(), root);

        var first = await firstProvider.AcquireAsync(RunId, CancellationToken.None);
        var contended = await secondProvider.AcquireAsync(RunId, CancellationToken.None);

        Assert.True(first.IsSuccessful);
        Assert.False(contended.IsSuccessful);
        Assert.Equal("DF-PUB-LEASE", contended.Error!.Code);
        await first.Value.DisposeAsync();

        var reacquired = await secondProvider.AcquireAsync(RunId, CancellationToken.None);
        Assert.True(reacquired.IsSuccessful);
        await reacquired.Value.DisposeAsync();
    }

    [Fact]
    public async Task PreCancelledLeaseAcquisitionDoesNotCreateLeaseFile()
    {
        Directory.CreateDirectory(Path.Combine(_rootPath, "runs", RunId));
        var provider = new WindowsPublicationLeaseProvider(
            new WindowsFileSystem(), WorkspaceRoot.Create(_rootPath).Value);
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.AcquireAsync(RunId, source.Token));

        Assert.False(File.Exists(Path.Combine(_rootPath, "runs", RunId, "publication.lock")));
    }

    [Fact]
    public async Task ReceiptWriteIsAtomicAdoptsExactOrphanAndRefusesMismatchWithoutOverwrite()
    {
        Directory.CreateDirectory(Path.Combine(_rootPath, "reports"));
        var workspace = await new WindowsFileSystem().OpenWorkspaceAsync(
            WorkspaceRoot.Create(_rootPath).Value,
            CancellationToken.None);
        var store = new AtomicPublicationReceiptStore();
        var path = WorkspaceRelativePath.Create("reports\\run.publication.json").Value;
        var body = Encoding.UTF8.GetBytes("{\"schemaVersion\":1}");
        var digest = $"sha256:{Convert.ToHexStringLower(SHA256.HashData(body))}";
        var request = PublicationReceiptWriteRequest.Create(workspace, path, body, digest).Value;

        var first = await store.WriteOrVerifyAsync(request, CancellationToken.None);
        var adopted = await store.WriteOrVerifyAsync(request, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(_rootPath, "reports", "run.publication.json"), "tampered");
        var refused = await store.WriteOrVerifyAsync(request, CancellationToken.None);

        Assert.True(first.IsSuccessful);
        Assert.False(first.Value.AdoptedExisting);
        Assert.True(adopted.IsSuccessful);
        Assert.True(adopted.Value.AdoptedExisting);
        Assert.False(refused.IsSuccessful);
        Assert.Equal("DF-PUB-RECEIPT", refused.Error!.Code);
        Assert.Equal("tampered", await File.ReadAllTextAsync(
            Path.Combine(_rootPath, "reports", "run.publication.json")));
    }

    [Fact]
    public async Task VerifyOnlyRefusesMissingReceiptWithoutCreatingIt()
    {
        Directory.CreateDirectory(Path.Combine(_rootPath, "reports"));
        var workspace = await new WindowsFileSystem().OpenWorkspaceAsync(
            WorkspaceRoot.Create(_rootPath).Value,
            CancellationToken.None);
        var store = new AtomicPublicationReceiptStore();
        var path = WorkspaceRelativePath.Create("reports\\missing.publication.json").Value;
        var body = Encoding.UTF8.GetBytes("{\"schemaVersion\":1}");
        var digest = $"sha256:{Convert.ToHexStringLower(SHA256.HashData(body))}";
        var request = PublicationReceiptWriteRequest.Create(
            workspace,
            path,
            body,
            digest,
            PublicationReceiptAccessMode.VerifyOnly).Value;

        var result = await store.WriteOrVerifyAsync(request, CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.False(File.Exists(Path.Combine(_rootPath, "reports", "missing.publication.json")));
    }

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_rootPath);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }

        return Task.CompletedTask;
    }

    private const string RunId = "run-0123456789abcdef0123456789abcdef";
}
