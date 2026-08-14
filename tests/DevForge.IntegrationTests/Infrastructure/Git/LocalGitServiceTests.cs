using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using DevForge.Application.Contracts;
using DevForge.Domain.Projects;
using DevForge.Infrastructure;
using DevForge.Infrastructure.FileSystem;
using DevForge.Infrastructure.Git;
using DevForge.Infrastructure.Processes;
using DevForge.Infrastructure.Security;

namespace DevForge.IntegrationTests.Infrastructure.Git;

[Collection(GitEnvironmentIsolationTestGroup.Name)]
public sealed class LocalGitServiceTests
{
    [Theory]
    [InlineData(GitBranchPolicy.Main)]
    [InlineData(GitBranchPolicy.MainAndDevelop)]
    public async Task RealBootstrapIsCleanIdempotentAndVerifiable(GitBranchPolicy branchPolicy)
    {
        await using var fixture = await GitFixture.CreateAsync();
        await fixture.WriteAsync("README.md", "# Sample\n");
        await fixture.WriteAsync("src\\App.cs", "namespace Sample;\n");
        var request = await fixture.CreateBootstrapRequestAsync(branchPolicy);

        var first = await fixture.Service.BootstrapAsync(request, CancellationToken.None);
        var second = await fixture.Service.BootstrapAsync(request, CancellationToken.None);
        var verified = await fixture.Service.VerifyAsync(
            GitVerificationRequest.Create(
                fixture.Workspace,
                branchPolicy,
                request.FinalTreeDigest,
                first.InitialCommitId).Value,
            CancellationToken.None);

        var expectedBranches = branchPolicy == GitBranchPolicy.Main
            ? new[] { "main" }
            : ["main", "develop"];
        Assert.Equal(expectedBranches, first.Branches.ToArray());
        Assert.Equal(first.InitialCommitId, second.InitialCommitId);
        Assert.Equal(first.InitialCommitId, verified.InitialCommitId);
        Assert.Equal(request.FinalTreeDigest, verified.FinalTreeDigest);
        Assert.Equal("main", await fixture.ReadCurrentBranchAsync());
        Assert.Equal(GitCommandFactory.BootstrapMessage, await fixture.ReadCommitSubjectAsync());
        Assert.Equal(GitCommandFactory.AuthorName, await fixture.ReadCommitAuthorNameAsync());
        Assert.Equal(GitCommandFactory.AuthorEmail, await fixture.ReadCommitAuthorEmailAsync());
    }

    [Theory]
    [InlineData("init")]
    [InlineData("add")]
    [InlineData("commit")]
    [InlineData("develop")]
    public async Task ExactKillWindowStateIsAdoptedWithoutDuplicateCommit(string phase)
    {
        await using var fixture = await GitFixture.CreateAsync();
        await fixture.WriteAsync("README.md", "# Recovery\n");
        var request = await fixture.CreateBootstrapRequestAsync(GitBranchPolicy.MainAndDevelop);
        await fixture.RunAsync(GitCommandFactory.Initialize(fixture.Workspace));
        if (phase is "add" or "commit" or "develop")
        {
            await fixture.RunAsync(GitCommandFactory.AddAll(fixture.Workspace));
        }

        string? commit = null;
        if (phase is "commit" or "develop")
        {
            await fixture.RunAsync(GitCommandFactory.Commit(fixture.Workspace));
            commit = await fixture.ReadHeadAsync();
        }

        if (phase == "develop")
        {
            await fixture.RunAsync(GitCommandFactory.CreateDevelop(fixture.Workspace, commit!));
        }

        var receipt = await fixture.Service.BootstrapAsync(request, CancellationToken.None);

        Assert.Equal(["main", "develop"], receipt.Branches.ToArray());
        Assert.Equal(commit ?? receipt.InitialCommitId, receipt.InitialCommitId);
        Assert.Equal("main", await fixture.ReadCurrentBranchAsync());
        Assert.Single(await fixture.ReadCommitIdsAsync());
    }

    [Fact]
    public async Task TreeDriftAndUnexpectedBranchFailClosedWithoutASecondCommit()
    {
        await using var fixture = await GitFixture.CreateAsync();
        await fixture.WriteAsync("README.md", "# Stable\n");
        var request = await fixture.CreateBootstrapRequestAsync(GitBranchPolicy.Main);
        var receipt = await fixture.Service.BootstrapAsync(request, CancellationToken.None);
        await fixture.WriteAsync("README.md", "# Drifted\n", overwrite: true);

        var drift = await Assert.ThrowsAsync<InfrastructureOperationException>(() =>
            fixture.Service.VerifyAsync(
                GitVerificationRequest.Create(
                    fixture.Workspace,
                    GitBranchPolicy.Main,
                    request.FinalTreeDigest,
                    receipt.InitialCommitId).Value,
                CancellationToken.None));

        Assert.Equal("DF-GIT-004", drift.Code);
        Assert.Single(await fixture.ReadCommitIdsAsync());
    }

    [Fact]
    public async Task FreshSecretFindingBlocksRepositoryCreation()
    {
        await using var fixture = await GitFixture.CreateAsync();
        await fixture.WriteAsync("config.json", "{\"access_token\":\"ghp_abcdefghijklmnopqrstuvwxyz123456\"}");
        var request = await fixture.CreateBootstrapRequestAsync(GitBranchPolicy.Main);

        var exception = await Assert.ThrowsAsync<InfrastructureOperationException>(() =>
            fixture.Service.BootstrapAsync(request, CancellationToken.None));

        Assert.Equal("DF-GIT-003", exception.Code);
        Assert.False(Directory.Exists(Path.Combine(fixture.RootPath, ".git")));
        Assert.DoesNotContain(fixture.RootPath, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AmbientGlobalConfigAndTemplateAreIgnoredByRealGit()
    {
        var ambientRoot = Path.Combine(
            Path.GetTempPath(),
            "DevForge-M8-AmbientGit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(ambientRoot, "template", "hooks"));
        var configPath = Path.Combine(ambientRoot, "global.gitconfig");
        var sentinelPath = Path.Combine(ambientRoot, "filter-invoked.txt");
        var dotnetHost = FindDotNetHost().Replace('\\', '/');
        var helperAssembly = FindHelperAssembly().Replace('\\', '/');
        var sentinelArgument = sentinelPath.Replace('\\', '/');
        var filterCommand = $"\\\"{dotnetHost}\\\" \\\"{helperAssembly}\\\" create-sentinel \\\"{sentinelArgument}\\\"";
        await File.WriteAllTextAsync(
            configPath,
            "[user]\n\tname = Ambient Attacker\n\temail = attacker@example.invalid\n"
            + "[init]\n\tdefaultBranch = attacker\n"
            + "[core]\n\thooksPath = hostile-hooks\n\tautocrlf = true\n");
        await File.AppendAllTextAsync(
            configPath,
            $"[filter \\\"devforge-hostile\\\"]\n\tclean = {filterCommand}\n"
            + $"\tsmudge = {filterCommand}\n\tprocess = {filterCommand}\n\trequired = true\n");
        await File.WriteAllTextAsync(
            Path.Combine(ambientRoot, "template", "hooks", "pre-commit"),
            "hostile template content\n");
        var originalConfig = System.Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");
        var originalTemplate = System.Environment.GetEnvironmentVariable("GIT_TEMPLATE_DIR");
        var originalSystem = System.Environment.GetEnvironmentVariable("GIT_CONFIG_SYSTEM");
        try
        {
            System.Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", configPath);
            System.Environment.SetEnvironmentVariable("GIT_CONFIG_SYSTEM", configPath);
            System.Environment.SetEnvironmentVariable(
                "GIT_TEMPLATE_DIR",
                Path.Combine(ambientRoot, "template"));
            await using var fixture = await GitFixture.CreateAsync();
            await fixture.WriteAsync(".gitattributes", "*.txt filter=devforge-hostile\n");
            await fixture.WriteAsync("filtered.txt", "must remain exact\r\n");
            await fixture.WriteAsync("README.md", "# Isolated\r\n");
            var request = await fixture.CreateBootstrapRequestAsync(GitBranchPolicy.Main);

            await fixture.Service.BootstrapAsync(request, CancellationToken.None);

            Assert.Equal("main", await fixture.ReadCurrentBranchAsync());
            Assert.Equal(GitCommandFactory.AuthorName, await fixture.ReadCommitAuthorNameAsync());
            Assert.Equal(GitCommandFactory.AuthorEmail, await fixture.ReadCommitAuthorEmailAsync());
            Assert.False(Directory.Exists(Path.Combine(fixture.RootPath, ".git", "hooks")));
            Assert.False(File.Exists(sentinelPath));
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", originalConfig);
            System.Environment.SetEnvironmentVariable("GIT_TEMPLATE_DIR", originalTemplate);
            System.Environment.SetEnvironmentVariable("GIT_CONFIG_SYSTEM", originalSystem);
            Directory.Delete(ambientRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData("ignored")]
    [InlineData("normalized")]
    public async Task CommittedTreeMustMatchEveryFinalizedPathAndByte(string mutation)
    {
        await using var fixture = await GitFixture.CreateAsync();
        if (mutation == "ignored")
        {
            await fixture.WriteAsync(".gitignore", "ignored.txt\n");
            await fixture.WriteAsync("ignored.txt", "must be committed\n");
        }
        else
        {
            await fixture.WriteAsync(".gitattributes", "* text eol=lf\n");
            await fixture.WriteAsync("normalized.txt", "first\r\nsecond\r\n");
        }

        var request = await fixture.CreateBootstrapRequestAsync(GitBranchPolicy.Main);

        var exception = await Assert.ThrowsAsync<InfrastructureOperationException>(() =>
            fixture.Service.BootstrapAsync(request, CancellationToken.None));

        Assert.Equal("DF-GIT-004", exception.Code);
    }

    [Theory]
    [InlineData("branch")]
    [InlineData("tag")]
    [InlineData("hook")]
    [InlineData("remote")]
    [InlineData("remote-ref")]
    [InlineData("commondir")]
    [InlineData("shallow")]
    [InlineData("reflog")]
    [InlineData("current-branch")]
    [InlineData("extra-object")]
    [InlineData("commit-message")]
    [InlineData("reflog-text")]
    [InlineData("index-extension")]
    public async Task UnexpectedRepositoryEvidenceIsRejectedWithoutMutation(string mutation)
    {
        await using var fixture = await GitFixture.CreateAsync();
        await fixture.WriteAsync("README.md", "# Exact repository\n");
        var bootstrap = await fixture.CreateBootstrapRequestAsync(GitBranchPolicy.Main);
        var receipt = await fixture.Service.BootstrapAsync(bootstrap, CancellationToken.None);
        switch (mutation)
        {
            case "branch":
                await fixture.WriteAsync(
                    ".git\\refs\\heads\\unexpected",
                    receipt.InitialCommitId + "\n");
                break;
            case "tag":
                await fixture.WriteAsync(
                    ".git\\refs\\tags\\v1",
                    receipt.InitialCommitId + "\n");
                break;
            case "hook":
                await fixture.WriteAsync(".git\\hooks\\pre-commit", "unexpected\n");
                break;
            case "remote":
                var config = await fixture.ReadAsync(".git\\config");
                await fixture.WriteAsync(
                    ".git\\config",
                    config + "[remote \"origin\"]\n\turl = https://example.invalid/repo.git\n",
                    overwrite: true);
                break;
            case "remote-ref":
                await fixture.WriteAsync(
                    ".git\\refs\\remotes\\origin\\main",
                    receipt.InitialCommitId + "\n");
                break;
            case "commondir":
                await fixture.WriteAsync(".git\\commondir", ".\n");
                break;
            case "shallow":
                await fixture.WriteAsync(".git\\shallow", receipt.InitialCommitId + "\n");
                break;
            case "reflog":
                var headLog = await fixture.ReadAsync(".git\\logs\\HEAD");
                await fixture.WriteAsync(
                    ".git\\logs\\HEAD",
                    headLog + headLog,
                    overwrite: true);
                break;
            case "current-branch":
                await fixture.WriteAsync(
                    ".git\\refs\\heads\\develop",
                    receipt.InitialCommitId + "\n");
                await fixture.WriteAsync(
                    ".git\\HEAD",
                    "ref: refs/heads/develop\n",
                    overwrite: true);
                break;
            case "extra-object":
                await fixture.WriteAsync(
                    ".git\\objects\\aa\\aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                    "unreachable object\n");
                break;
            case "commit-message":
                await fixture.WriteAsync(
                    ".git\\COMMIT_EDITMSG",
                    GitCommandFactory.BootstrapMessage + "\ncredential-shaped trailing text\n",
                    overwrite: true);
                break;
            case "reflog-text":
                var mainLog = await fixture.ReadAsync(".git\\logs\\refs\\heads\\main");
                await fixture.WriteAsync(
                    ".git\\logs\\refs\\heads\\main",
                    mainLog.TrimEnd('\r', '\n') + " credential-shaped trailing text\n",
                    overwrite: true);
                break;
            case "index-extension":
                await fixture.AppendIndexExtensionAsync();
                break;
            default:
                throw new InvalidOperationException();
        }

        var exception = await Assert.ThrowsAsync<InfrastructureOperationException>(() =>
            fixture.Service.VerifyAsync(
                GitVerificationRequest.Create(
                    fixture.Workspace,
                    GitBranchPolicy.Main,
                    bootstrap.FinalTreeDigest,
                    receipt.InitialCommitId).Value,
                CancellationToken.None));

        Assert.Equal("DF-GIT-001", exception.Code);
        Assert.Equal(receipt.InitialCommitId, await fixture.ReadHeadAsync());
    }

    [Theory]
    [InlineData(ProcessTerminationReason.TimedOut, "DF-GIT-002")]
    [InlineData(ProcessTerminationReason.Cancelled, null)]
    public async Task ProcessTimeoutAndCancellationAreMappedWithoutCreatingRepository(
        ProcessTerminationReason reason,
        string? expectedCode)
    {
        await using var fixture = await GitFixture.CreateAsync();
        await fixture.WriteAsync("README.md", "# Terminal state\n");
        var request = await fixture.CreateBootstrapRequestAsync(GitBranchPolicy.Main);
        var service = new LocalGitService(new TerminalRunner(reason), new EmptyScanner());

        if (reason == ProcessTerminationReason.Cancelled)
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                service.BootstrapAsync(request, CancellationToken.None));
        }
        else
        {
            var exception = await Assert.ThrowsAsync<InfrastructureOperationException>(() =>
                service.BootstrapAsync(request, CancellationToken.None));
            Assert.Equal(expectedCode, exception.Code);
        }

        Assert.False(Directory.Exists(Path.Combine(fixture.RootPath, ".git")));
    }

    private sealed class GitFixture : IAsyncDisposable
    {
        private readonly WindowsProcessRunner _runner = new();

        private GitFixture(string rootPath, IWorkspaceFileSystem workspace)
        {
            RootPath = rootPath;
            Workspace = workspace;
            Service = new LocalGitService(_runner, new WorkspaceSecretScanner());
        }

        public string RootPath { get; }
        public IWorkspaceFileSystem Workspace { get; }
        public LocalGitService Service { get; }

        public static async Task<GitFixture> CreateAsync()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "DevForge-M8-LocalGit-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            var workspace = await new WindowsFileSystem().OpenWorkspaceAsync(
                WorkspaceRoot.Create(path).Value,
                CancellationToken.None);
            return new GitFixture(path, workspace);
        }

        public async Task<GitBootstrapRequest> CreateBootstrapRequestAsync(
            GitBranchPolicy branchPolicy)
        {
            var tree = await CanonicalProjectTree.CaptureAsync(
                Workspace,
                allowOwnedRootGit: false,
                CancellationToken.None);
            return GitBootstrapRequest.Create(Workspace, branchPolicy, tree.Digest).Value;
        }

        public async Task WriteAsync(string path, string content, bool overwrite = false)
        {
            var parent = Path.GetDirectoryName(path)?.Replace('/', '\\');
            if (!string.IsNullOrEmpty(parent))
            {
                await Workspace.CreateDirectoryAsync(
                    WorkspaceRelativePath.Create(parent).Value,
                    CancellationToken.None);
            }

            await using var output = await Workspace.OpenWriteAsync(
                WorkspaceRelativePath.Create(path).Value,
                overwrite,
                CancellationToken.None);
            await output.WriteAsync(Encoding.UTF8.GetBytes(content));
        }

        public async Task<string> ReadAsync(string path)
        {
            await using var input = await Workspace.OpenReadAsync(
                WorkspaceRelativePath.Create(path).Value,
                CancellationToken.None);
            using var reader = new StreamReader(input, Encoding.UTF8);
            return await reader.ReadToEndAsync(CancellationToken.None);
        }

        public async Task AppendIndexExtensionAsync()
        {
            var path = WorkspaceRelativePath.Create(".git\\index").Value;
            byte[] original;
            await using (var input = await Workspace.OpenReadAsync(path, CancellationToken.None))
            {
                original = new byte[checked((int)input.Length)];
                await input.ReadExactlyAsync(original, CancellationToken.None);
            }
            const int checksumBytes = 20;
            var content = original.AsSpan(0, original.Length - checksumBytes);
            var extension = "TEST\0\0\0\u0004data"u8;
            var tampered = new byte[content.Length + extension.Length + checksumBytes];
            content.CopyTo(tampered);
            extension.CopyTo(tampered.AsSpan(content.Length));
#pragma warning disable CA5350 // The test must produce a protocol-valid Git SHA-1 index checksum.
            SHA1.HashData(tampered.AsSpan(0, tampered.Length - checksumBytes))
                .CopyTo(tampered, tampered.Length - checksumBytes);
#pragma warning restore CA5350
            await using var output = await Workspace.OpenWriteAsync(
                path,
                overwrite: true,
                CancellationToken.None);
            await output.WriteAsync(tampered);
        }

        public async Task RunAsync(CommandSpec command)
        {
            var result = await _runner.RunAsync(command, null, CancellationToken.None);
            Assert.Equal(ProcessTerminationReason.Exited, result.TerminationReason);
            Assert.Equal(0, result.ExitCode);
        }

        public async Task<string> ReadHeadAsync() =>
            Assert.Single(await ReadLinesAsync(GitCommandFactory.Head(Workspace)));

        public async Task<string> ReadCurrentBranchAsync() =>
            Assert.Single(await ReadLinesAsync(GitCommandFactory.CurrentBranch(Workspace)));

        public async Task<string> ReadCommitSubjectAsync() =>
            (await ReadMetadataAsync()).Subject;

        public async Task<string> ReadCommitAuthorNameAsync() =>
            (await ReadMetadataAsync()).AuthorName;

        public async Task<string> ReadCommitAuthorEmailAsync() =>
            (await ReadMetadataAsync()).AuthorEmail;

        public async Task<string[]> ReadCommitIdsAsync()
        {
            var metadata = await ReadMetadataAsync();
            Assert.Null(metadata.ParentCommitId);
            return [metadata.CommitId];
        }

        private async Task<GitCommitEvidence> ReadMetadataAsync() =>
            await GitCommitObjectReader.ReadAsync(
                Workspace,
                await ReadHeadAsync(),
                CancellationToken.None);

        private async Task<string[]> ReadLinesAsync(CommandSpec command)
        {
            var result = await _runner.RunAsync(command, null, CancellationToken.None);
            Assert.Equal(0, result.ExitCode);
            return
            [
                .. result.RetainedLines
                    .Where(line => line.Channel == ProcessOutputChannel.StandardOutput)
                    .Select(line => line.Text.Value),
            ];
        }

        public ValueTask DisposeAsync()
        {
            var fullPath = Path.GetFullPath(RootPath);
            if (!fullPath.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(fullPath).StartsWith(
                    "DevForge-M8-LocalGit-",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException();
            }

            foreach (var file in Directory.EnumerateFiles(
                fullPath,
                "*",
                SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(fullPath, recursive: true);
            return ValueTask.CompletedTask;
        }
    }

    private static string FindDotNetHost()
    {
        var configured = System.Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return configured;
        }

        var runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
        var candidate = Path.GetFullPath(
            Path.Combine(runtimeDirectory, "..", "..", "..", "dotnet.exe"));
        return File.Exists(candidate) ? candidate : throw new FileNotFoundException();
    }

    private static string FindHelperAssembly()
    {
        for (var directory = new DirectoryInfo(Path.GetFullPath(AppContext.BaseDirectory));
             directory is not null;
             directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, "DevForge.sln")))
            {
                continue;
            }

            var candidate = Path.Combine(
                directory.FullName,
                "tests",
                "DevForge.ProcessTestHelper",
                "bin",
                "Release",
                "net10.0",
                "DevForge.ProcessTestHelper.dll");
            return File.Exists(candidate) ? candidate : throw new FileNotFoundException();
        }

        throw new DirectoryNotFoundException();
    }

    private sealed class TerminalRunner(ProcessTerminationReason reason) : IProcessRunner
    {
        public Task CheckPreconditionsAsync(
            CommandSpec command,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<ProcessResult> RunAsync(
            CommandSpec command,
            IProgress<ProcessOutputLine>? progress,
            CancellationToken cancellationToken) => Task.FromResult(ProcessResult.Create(
                reason,
                reason == ProcessTerminationReason.Exited ? 0 : null,
                []).Value);
    }

    private sealed class EmptyScanner : ISecretScanner
    {
        public Task<SecretScanResult> ScanAsync(
            SecretScanRequest request,
            CancellationToken cancellationToken) => Task.FromResult(
                SecretScanResult.Create([]).Value);
    }
}
