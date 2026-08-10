using System.Text;
using DevForge.Application.Contracts;
using DevForge.Infrastructure;
using DevForge.Infrastructure.FileSystem;
using DevForge.Infrastructure.Security;

namespace DevForge.IntegrationTests.Infrastructure.Security;

public sealed class WorkspaceSecretScannerTests
{
    [Theory]
    [InlineData("auth.txt", "Authorization: Bearer abcdefghijklmnopqrstuvwxyz", "bearer")]
    [InlineData("token.txt", "github_pat_abcdefghijklmnopqrstuvwxyz", "service token")]
    [InlineData("openai.txt", "sk-proj-abcdefghijklmnopqrstuvwxyz", "service token")]
    [InlineData("aws.txt", "AKIAABCDEFGHIJKLMNOP", "service token")]
    [InlineData("jwt.txt", "eyJabcdefghi.eyJabcdefghi.abcdefghijkl", "JWT")]
    [InlineData("key.pem", "-----BEGIN PRIVATE KEY-----", "private key")]
    [InlineData("settings.env", "DATABASE_PASSWORD=fixture-database-password", "secret assignment")]
    [InlineData("connection.txt", "Server=local;Password=fixture-password;", "secret assignment")]
    [InlineData("settings.json", "{\"password\":\"fixture-json-secret\"}", "secret assignment")]
    [InlineData("project.csproj", "<ApiToken>fixture-xml-secret</ApiToken>", "secret assignment")]
    public async Task WholeWorkspaceScanReturnsCategoryWithoutMatchedValue(
        string path,
        string contents,
        string expectedCategory)
    {
        await using var fixture = await ScannerFixture.CreateAsync();
        await fixture.WriteTextAsync(path, contents);

        var result = await fixture.Scanner.ScanAsync(
            SecretScanRequest.WholeWorkspace(fixture.Workspace).Value,
            CancellationToken.None);

        var finding = Assert.Single(result.Findings);
        Assert.Equal(path, finding.Path.Value);
        Assert.Equal(1, finding.LineNumber);
        Assert.Contains(expectedCategory, finding.Description.Value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(contents, finding.Description.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplicitScanDoesNotReadUnrequestedFiles()
    {
        await using var fixture = await ScannerFixture.CreateAsync();
        await fixture.WriteTextAsync("requested.txt", "safe content");
        await fixture.WriteTextAsync("not-requested.txt", "token=fixture-secret-value");
        var request = SecretScanRequest.ExplicitPaths(
            fixture.Workspace,
            [Relative("requested.txt")]).Value;

        var result = await fixture.Scanner.ScanAsync(request, CancellationToken.None);

        Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData("Foreign key: FK_ProjectRun")]
    [InlineData("The .env file was not read")]
    [InlineData("monkey=value")]
    public async Task SafeDiagnosticTextDoesNotProduceFinding(string contents)
    {
        await using var fixture = await ScannerFixture.CreateAsync();
        await fixture.WriteTextAsync("safe.txt", contents);

        var result = await fixture.Scanner.ScanAsync(
            SecretScanRequest.WholeWorkspace(fixture.Workspace).Value,
            CancellationToken.None);

        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task BinaryFileIsSkippedWithoutReturningBinaryContent()
    {
        await using var fixture = await ScannerFixture.CreateAsync();
        await fixture.WriteBytesAsync("binary.txt", [0x00, 0x01, 0x02, 0x03]);

        var result = await fixture.Scanner.ScanAsync(
            SecretScanRequest.WholeWorkspace(fixture.Workspace).Value,
            CancellationToken.None);

        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task InvalidUtf8TextFailsClosed()
    {
        await using var fixture = await ScannerFixture.CreateAsync();
        await fixture.WriteBytesAsync("invalid.txt", [0xC3, 0x28]);

        var exception = await Assert.ThrowsAsync<InfrastructureOperationException>(() =>
            fixture.Scanner.ScanAsync(
                SecretScanRequest.WholeWorkspace(fixture.Workspace).Value,
                CancellationToken.None));

        Assert.Equal("DF-SCAN-001", exception.Code);
        Assert.DoesNotContain("invalid.txt", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OversizedTextFailsClosedWithScrubbedError()
    {
        await using var fixture = await ScannerFixture.CreateAsync();
        var path = "oversized.txt";
        await fixture.WriteTextAsync(path, new string('x', 1_048_577));

        var exception = await Assert.ThrowsAsync<InfrastructureOperationException>(() =>
            fixture.Scanner.ScanAsync(
                SecretScanRequest.WholeWorkspace(fixture.Workspace).Value,
                CancellationToken.None));

        Assert.Equal("DF-SCAN-001", exception.Code);
        Assert.DoesNotContain(path, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MissingExplicitFileFailsClosedWithoutLeakingPath()
    {
        await using var fixture = await ScannerFixture.CreateAsync();
        var request = SecretScanRequest.ExplicitPaths(
            fixture.Workspace,
            [Relative("missing-secret-file.txt")]).Value;

        var exception = await Assert.ThrowsAsync<InfrastructureOperationException>(() =>
            fixture.Scanner.ScanAsync(request, CancellationToken.None));

        Assert.Equal("DF-SCAN-001", exception.Code);
        Assert.DoesNotContain("missing-secret-file", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PreCancelledScanDoesNotOpenFiles()
    {
        await using var fixture = await ScannerFixture.CreateAsync();
        await fixture.WriteTextAsync("content.txt", "token=fixture-secret-value");
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Scanner.ScanAsync(
                SecretScanRequest.WholeWorkspace(fixture.Workspace).Value,
                source.Token));
    }

    private static WorkspaceRelativePath Relative(string value)
    {
        return WorkspaceRelativePath.Create(value).Value;
    }

    private sealed class ScannerFixture : IAsyncDisposable
    {
        private ScannerFixture(
            string rootPath,
            IWorkspaceFileSystem workspace,
            WorkspaceSecretScanner scanner)
        {
            RootPath = rootPath;
            Workspace = workspace;
            Scanner = scanner;
        }

        public string RootPath { get; }

        public IWorkspaceFileSystem Workspace { get; }

        public WorkspaceSecretScanner Scanner { get; }

        public static async Task<ScannerFixture> CreateAsync()
        {
            var rootPath = Path.Combine(Path.GetTempPath(), "DevForge-M3-Scan-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootPath);
            var root = WorkspaceRoot.Create(rootPath).Value;
            var workspace = await new WindowsFileSystem().OpenWorkspaceAsync(root, CancellationToken.None);
            return new ScannerFixture(rootPath, workspace, new WorkspaceSecretScanner());
        }

        public Task WriteTextAsync(string path, string contents)
        {
            return WriteBytesAsync(path, Encoding.UTF8.GetBytes(contents));
        }

        public async Task WriteBytesAsync(string path, byte[] contents)
        {
            await using var stream = await Workspace.OpenWriteAsync(
                Relative(path),
                overwrite: false,
                CancellationToken.None);
            await stream.WriteAsync(contents);
        }

        public ValueTask DisposeAsync()
        {
            var fullPath = Path.GetFullPath(RootPath);
            if (!fullPath.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(fullPath).StartsWith("DevForge-M3-Scan-", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Refusing to clean an unexpected scanner test directory.");
            }

            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
