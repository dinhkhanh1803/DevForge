using System.Security.Cryptography;
using System.Text;
using DevForge.Application.Contracts;
using DevForge.Infrastructure;
using DevForge.Infrastructure.FileSystem;
using DevForge.Infrastructure.Security;

namespace DevForge.IntegrationTests.Infrastructure.Security;

public sealed class WorkspaceSecretScannerTests
{
    [Theory]
    [InlineData("password=fixture-appended-secret", "secret assignment")]
    [InlineData("Authorization: Bearer abcdefghijklmnopqrstuvwxyz", "bearer-style credential")]
    [InlineData("github_pat_abcdefghijklmnopqrstuvwxyz", "service token")]
    [InlineData("eyJabcdefghi.eyJabcdefghi.abcdefghijkl", "JWT credential")]
    [InlineData("-----BEGIN PRIVATE KEY-----", "private key")]
    [InlineData("<ApiToken>fixture-xml-secret</ApiToken>", "secret assignment")]
    public async Task AppendedCredentialCategoryIsDetectedSeparatelyFromThePublicMap(string secret, string category)
    {
        await using var fixture = await ScannerFixture.CreateAsync();
        var bytes = Convert.FromBase64String(await File.ReadAllTextAsync(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "react-19.2.8-vite-8.2.2.base64")));
        var appendedLine = Encoding.UTF8.GetString(bytes).Count(character => character == '\n') + 2;
        await fixture.WriteBytesAsync("bundle.js", [.. bytes, .. Encoding.UTF8.GetBytes("\n" + secret)]);
        var scan = await fixture.Scanner.ScanAsync(SecretScanRequest.WholeWorkspace(fixture.Workspace).Value, default);
        Assert.Contains(scan.Findings, finding => finding.LineNumber == appendedLine
            && finding.Description.Value.Contains(category, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("original", false)]
    [InlineData("space", true)]
    [InlineData("byte", true)]
    [InlineData("bom", true)]
    [InlineData("line-endings", true)]
    [InlineData("concatenated", true)]
    [InlineData("text-extension", true)]
    [InlineData("credential", true)]
    public async Task ReviewedArtifactExceptionRequiresExactCompleteBytes(string mutation, bool expectedFinding)
    {
        await using var fixture = await ScannerFixture.CreateAsync();
        var bytes = Convert.FromBase64String(await File.ReadAllTextAsync(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "react-19.2.8-vite-8.2.2.base64")));
        Assert.Equal(190720, bytes.Length);
        Assert.Equal("0dc53246ec934df87e6acfa00a2471debd43f04b14226866942282655cb5236d",
            Convert.ToHexStringLower(SHA256.HashData(bytes)));
        Assert.Equal("password:!0", Encoding.UTF8.GetString(bytes).Split('\n')[7].Substring(21275, 11));
        bytes = mutation switch
        {
            "space" => [.. bytes, 32],
            "byte" => [(byte)(bytes[0] ^ 1), .. bytes[1..]],
            "bom" => [0xef, 0xbb, 0xbf, .. bytes],
            "line-endings" => Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("\n", "\r\n", StringComparison.Ordinal)),
            "concatenated" => [.. bytes, .. "\nconst extra = true;"u8.ToArray()],
            "credential" => [.. bytes, .. "\npassword=fixture-appended-secret"u8.ToArray()],
            _ => bytes,
        };
        var path = mutation == "text-extension" ? "bundle.txt" : "bundle.js";
        await fixture.WriteBytesAsync(path, bytes);
        var scan = await fixture.Scanner.ScanAsync(SecretScanRequest.WholeWorkspace(fixture.Workspace).Value, default);
        Assert.Equal(expectedFinding, !scan.Findings.IsEmpty);
    }

    [Theory]
    [InlineData("bundle.js", "const fields={email:!0,password:!0,search:!0};", true)]
    [InlineData("bundle.js", "const fields={password:!1};", true)]
    [InlineData("bundle.js", "const text=\"{password:!0}\";", true)]
    [InlineData("bundle.js", "const text='{password:!1}';", true)]
    [InlineData("bundle.js", "const text=`{password:!0}`;", true)]
    [InlineData("bundle.js", "// {password:!0}", true)]
    [InlineData("bundle.js", "/* {password:!1} */", true)]
    [InlineData("bundle.js", "const fields={password:\"!0\"};", true)]
    [InlineData("bundle.js", "const fields={password:!0+\"fixture-secret\"};", true)]
    [InlineData("bundle.js", "const fields={password:!0,token:\"fixture-secret\"};", true)]
    [InlineData("bundle.js", "password=!0", true)]
    [InlineData("settings.json", "{password:!0}", true)]
    public async Task UnreviewedJavascriptBooleanMarkersAndEmbeddedCredentialTextRemainScanned(
        string path, string text, bool expectedFinding)
    {
        await using var fixture = await ScannerFixture.CreateAsync();
        await fixture.WriteTextAsync(path, text);
        var result = await fixture.Scanner.ScanAsync(SecretScanRequest.WholeWorkspace(fixture.Workspace).Value, default);
        Assert.Equal(expectedFinding, !result.Findings.IsEmpty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BoundedMinifiedJavascriptIsScannedWithoutSkippingLongLines(bool containsSecret)
    {
        await using var fixture = await ScannerFixture.CreateAsync();
        var text = string.Concat(Enumerable.Repeat("constValue(123);", 15_000));
        if (containsSecret) { text += " password=fixture-scanner-secret;"; }
        await fixture.WriteTextAsync("bundle.js", text);
        var result = await fixture.Scanner.ScanAsync(SecretScanRequest.WholeWorkspace(fixture.Workspace).Value, default);
        Assert.Equal(containsSecret, !result.Findings.IsEmpty);
    }

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
    [InlineData("smoke.mjs", "password=fixture-module-secret", "secret assignment")]
    [InlineData("config.cjs", "password=fixture-commonjs-secret", "secret assignment")]
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

    [Theory]
    [InlineData("bundle.js")]
    [InlineData("smoke.mjs")]
    [InlineData("config.cjs")]
    public async Task JavascriptWithBinaryPrefixFailsClosed(string path)
    {
        await using var fixture = await ScannerFixture.CreateAsync();
        await fixture.WriteBytesAsync(path, [0, .. "password=fixture-secret"u8.ToArray()]);
        var error = await Assert.ThrowsAsync<InfrastructureOperationException>(() => fixture.Scanner.ScanAsync(
            SecretScanRequest.WholeWorkspace(fixture.Workspace).Value, default));
        Assert.Equal("DF-SCAN-001", error.Code);
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
