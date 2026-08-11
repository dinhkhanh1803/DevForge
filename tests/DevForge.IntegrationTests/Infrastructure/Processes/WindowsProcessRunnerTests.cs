using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using DevForge.Application.Contracts;
using DevForge.Infrastructure;
using DevForge.Infrastructure.FileSystem;
using DevForge.Infrastructure.Processes;

namespace DevForge.IntegrationTests.Infrastructure.Processes;

public sealed class WindowsProcessRunnerTests
{
    [Fact]
    public async Task ArgumentsWithShellMetacharactersRemainIndividualData()
    {
        await using var fixture = await ProcessFixture.CreateAsync();
        var command = fixture.CreateCommand(
            ["echo-args", "a & whoami", "$(hostname)", "x|y", "> output.txt"]);

        var result = await fixture.Runner.RunAsync(command, null, CancellationToken.None);

        Assert.Equal(ProcessTerminationReason.Exited, result.TerminationReason);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains(result.RetainedLines, line => line.Text.Value == "ARG[0]=a & whoami");
        Assert.Contains(result.RetainedLines, line => line.Text.Value == "ARG[1]=$(hostname)");
        Assert.Contains(result.RetainedLines, line => line.Text.Value == "ARG[2]=x|y");
        Assert.Contains(result.RetainedLines, line => line.Text.Value == "ARG[3]=> output.txt");
    }

    [Fact]
    public async Task TrustedNodeBackedToolPrefixRemainsSeparatedFromPackageArguments()
    {
        await using var fixture = await ProcessFixture.CreateAsync();
        var runner = new WindowsProcessRunner(new FixedExecutableResolver(
            ProcessFixture.FindDotNetHost(),
            [fixture.HelperAssemblyPath]));
        var command = CommandSpec.Create(
            ExecutableIdentity.Create("npm").Value,
            ["echo-args", "package-value"],
            fixture.Workspace,
            WorkspaceRelativePath.Create("work").Value,
            [],
            TimeSpan.FromSeconds(10),
            [0],
            []).Value;

        var result = await runner.RunAsync(command, null, default);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(result.RetainedLines, line => line.Text.Value == "ARG[0]=package-value");
    }

    [Fact]
    public void PackageManagerShimsResolveThroughNodeWithoutShellExecution()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"DevForge-TrustedResolver-{Guid.NewGuid():N}");
        var npmBin = Path.Combine(root, "node_modules", "npm", "bin");
        var corepackBin = Path.Combine(root, "node_modules", "corepack", "dist");

        try
        {
            Directory.CreateDirectory(npmBin);
            Directory.CreateDirectory(corepackBin);
            var node = Path.Combine(root, "node.exe");
            var npm = Path.Combine(npmBin, "npm-cli.js");
            var corepack = Path.Combine(corepackBin, "corepack.js");
            File.WriteAllBytes(node, [0]);
            File.WriteAllText(npm, string.Empty);
            File.WriteAllText(corepack, string.Empty);
            var resolver = new TrustedExecutableResolver(root, dotNetHostPath: null);

            var npmLaunch = resolver.Resolve(ExecutableIdentity.Create("npm").Value);
            var pnpmLaunch = resolver.Resolve(ExecutableIdentity.Create("pnpm").Value);

            Assert.Equal(Path.GetFullPath(node), npmLaunch.ExecutablePath);
            Assert.Equal([Path.GetFullPath(npm)], npmLaunch.PrefixArguments.ToArray());
            Assert.Equal(Path.GetFullPath(node), pnpmLaunch.ExecutablePath);
            Assert.Equal(
                [Path.GetFullPath(corepack), "pnpm"],
                pnpmLaunch.PrefixArguments.ToArray());
        }
        finally
        {
            if (Directory.Exists(root)
                && Path.GetFileName(root).StartsWith(
                    "DevForge-TrustedResolver-",
                    StringComparison.Ordinal))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task StandardOutputAndErrorAreRedirectedAndReported()
    {
        await using var fixture = await ProcessFixture.CreateAsync();
        var progress = new RecordingProgress();

        var result = await fixture.Runner.RunAsync(
            fixture.CreateCommand(["write-streams"]),
            progress,
            CancellationToken.None);

        Assert.Contains(result.RetainedLines, line =>
            line.Channel == ProcessOutputChannel.StandardOutput
            && line.Text.Value == "stdout-line");
        Assert.Contains(result.RetainedLines, line =>
            line.Channel == ProcessOutputChannel.StandardError
            && line.Text.Value == "stderr-line");
        Assert.Equal(2, progress.Lines.Count);
    }

    [Fact]
    public async Task ThrowingProgressObserverCannotStopOutputDrain()
    {
        await using var fixture = await ProcessFixture.CreateAsync();

        var result = await fixture.Runner.RunAsync(
            fixture.CreateCommand(["write-streams"]),
            new ThrowingProgress(),
            CancellationToken.None);

        Assert.Equal(ProcessTerminationReason.Exited, result.TerminationReason);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(2, result.RetainedLines.Length);
    }

    [Fact]
    public async Task SensitiveEnvironmentOutputIsRedactedBeforeObservation()
    {
        await using var fixture = await ProcessFixture.CreateAsync();
        var secretText = "fixture-ephemeral-sensitive-123456";
        var sensitive = SensitiveProcessValue.Create(secretText).Value;
        var environmentValue = ProcessEnvironmentValue.CreateSensitive(sensitive).Value;
        var environment = new[]
        {
            KeyValuePair.Create<string, ProcessEnvironmentValue?>(
                "DEVFORGE_TEST_SECRET",
                environmentValue),
        };

        var result = await fixture.Runner.RunAsync(
            fixture.CreateCommand(
                ["echo-env", "DEVFORGE_TEST_SECRET"],
                environmentVariables: environment,
                redactionNeedles: [sensitive]),
            null,
            CancellationToken.None);

        Assert.DoesNotContain(
            result.RetainedLines,
            line => line.Text.Value.Contains(secretText, StringComparison.Ordinal));
        Assert.Contains(result.RetainedLines, line => line.Text.Value.Contains("[REDACTED]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LargeOutputIsDrainedButRetentionRemainsBounded()
    {
        await using var fixture = await ProcessFixture.CreateAsync();
        var progress = new RecordingProgress();

        var result = await fixture.Runner.RunAsync(
            fixture.CreateCommand(["large-output", "400", "500"]),
            progress,
            CancellationToken.None);

        Assert.Equal(ProcessTerminationReason.Exited, result.TerminationReason);
        Assert.True(result.IsOutputTruncated);
        Assert.True(result.RetainedLines.Length <= ProcessResult.MaxRetainedOutputLines);
        Assert.True(result.RetainedCharacterCount <= ProcessResult.MaxRetainedOutputCharacters);
        Assert.True(progress.Lines.Count <= ProcessResult.MaxRetainedOutputLines);
        Assert.True(
            progress.Lines.Sum(line => line.Text.Value.Length)
                <= ProcessResult.MaxRetainedOutputCharacters);
    }

    [Fact]
    public async Task OversizedPhysicalLineIsDiscardedWithoutUnboundedRetention()
    {
        await using var fixture = await ProcessFixture.CreateAsync();

        var result = await fixture.Runner.RunAsync(
            fixture.CreateCommand(["large-output", "1", "20000"]),
            null,
            CancellationToken.None);

        Assert.True(result.IsOutputTruncated);
        Assert.Equal("[OUTPUT LINE TRUNCATED]", Assert.Single(result.RetainedLines).Text.Value);
    }

    [Fact]
    public async Task DisallowedExitCodeRemainsAProcessTransportResult()
    {
        await using var fixture = await ProcessFixture.CreateAsync();

        var result = await fixture.Runner.RunAsync(
            fixture.CreateCommand(["unsupported-verb"]),
            null,
            CancellationToken.None);

        Assert.Equal(ProcessTerminationReason.Exited, result.TerminationReason);
        Assert.Equal(64, result.ExitCode);
    }

    [Fact]
    public async Task TimeoutTerminatesTheProcess()
    {
        await using var fixture = await ProcessFixture.CreateAsync();

        var result = await fixture.Runner.RunAsync(
            fixture.CreateCommand(["sleep", "30000"], timeout: TimeSpan.FromMilliseconds(300)),
            null,
            CancellationToken.None);

        Assert.Equal(ProcessTerminationReason.TimedOut, result.TerminationReason);
        Assert.Null(result.ExitCode);
    }

    [Fact]
    public async Task CancellationTerminatesTheProcessAndReturnsCancelled()
    {
        await using var fixture = await ProcessFixture.CreateAsync();
        using var source = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var result = await fixture.Runner.RunAsync(
            fixture.CreateCommand(["sleep", "30000"], timeout: TimeSpan.FromSeconds(30)),
            null,
            source.Token);

        Assert.Equal(ProcessTerminationReason.Cancelled, result.TerminationReason);
        Assert.Null(result.ExitCode);
    }

    [Fact]
    public async Task CancellationDuringContinuousOutputStillDrainsAndTerminates()
    {
        await using var fixture = await ProcessFixture.CreateAsync();
        using var source = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var result = await fixture.Runner.RunAsync(
            fixture.CreateCommand(["write-until-killed"], timeout: TimeSpan.FromSeconds(30)),
            null,
            source.Token);

        Assert.Equal(ProcessTerminationReason.Cancelled, result.TerminationReason);
        Assert.NotEmpty(result.RetainedLines);
    }

    [Fact]
    public async Task TimeoutTerminatesDescendantProcessTree()
    {
        await using var fixture = await ProcessFixture.CreateAsync();

        var result = await fixture.Runner.RunAsync(
            fixture.CreateCommand(
                ["spawn-child-and-wait"],
                timeout: TimeSpan.FromMilliseconds(800)),
            null,
            CancellationToken.None);

        var childLine = Assert.Single(
            result.RetainedLines.Where(line =>
                line.Text.Value.StartsWith("CHILD_PID=", StringComparison.Ordinal)));
        var childId = int.Parse(
            childLine.Text.Value["CHILD_PID=".Length..],
            System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(ProcessTerminationReason.TimedOut, result.TerminationReason);
        Assert.False(IsProcessAlive(childId));
    }

    [Fact]
    public async Task MissingResolvedExecutableReturnsScrubbedStableFailure()
    {
        await using var fixture = await ProcessFixture.CreateAsync(
            resolvedExecutable: Path.Combine(Path.GetTempPath(), "missing-devforge-tool.exe"));

        var exception = await Assert.ThrowsAsync<InfrastructureOperationException>(() =>
            fixture.Runner.RunAsync(
                fixture.CreateCommand(["echo-args", "value"]),
                null,
                CancellationToken.None));

        Assert.Equal("DF-PROC-001", exception.Code);
        Assert.DoesNotContain("missing-devforge-tool", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PreCancelledRunDoesNotResolveOrStartExecutable()
    {
        await using var fixture = await ProcessFixture.CreateAsync(
            resolvedExecutable: Path.Combine(Path.GetTempPath(), "missing-devforge-tool.exe"));
        using var source = new CancellationTokenSource();
        source.Cancel();

        var result = await fixture.Runner.RunAsync(
            fixture.CreateCommand(["echo-args", "value"]),
            null,
            source.Token);

        Assert.Equal(ProcessTerminationReason.Cancelled, result.TerminationReason);
        Assert.Null(result.ExitCode);
    }

    private static bool IsProcessAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private sealed class RecordingProgress : IProgress<ProcessOutputLine>
    {
        public List<ProcessOutputLine> Lines { get; } = [];

        public void Report(ProcessOutputLine value)
        {
            Lines.Add(value);
        }
    }

    private sealed class ThrowingProgress : IProgress<ProcessOutputLine>
    {
        public void Report(ProcessOutputLine value)
        {
            throw new InvalidOperationException("Synthetic observer failure with sensitive-looking detail.");
        }
    }

    private sealed class FixedExecutableResolver(
        string executablePath,
        ImmutableArray<string> prefixArguments = default) : ITrustedExecutableResolver
    {
        public TrustedExecutableLaunch Resolve(ExecutableIdentity executable)
        {
            return new TrustedExecutableLaunch(
                executablePath,
                prefixArguments.IsDefault ? [] : prefixArguments);
        }
    }

    private sealed class ProcessFixture : IAsyncDisposable
    {
        private ProcessFixture(
            string rootPath,
            IWorkspaceFileSystem workspace,
            WindowsProcessRunner runner,
            string helperAssemblyPath)
        {
            RootPath = rootPath;
            Workspace = workspace;
            Runner = runner;
            HelperAssemblyPath = helperAssemblyPath;
        }

        public string RootPath { get; }

        public IWorkspaceFileSystem Workspace { get; }

        public WindowsProcessRunner Runner { get; }

        public string HelperAssemblyPath { get; }

        public static async Task<ProcessFixture> CreateAsync(string? resolvedExecutable = null)
        {
            var rootPath = Path.Combine(Path.GetTempPath(), "DevForge-M3-Process-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootPath);
            var root = WorkspaceRoot.Create(rootPath).Value;
            var workspace = await new WindowsFileSystem().OpenWorkspaceAsync(root, CancellationToken.None);
            await workspace.CreateDirectoryAsync(Relative("work"), CancellationToken.None);

            var dotnetHost = resolvedExecutable ?? FindDotNetHost();
            var helperAssembly = FindHelperAssembly();
            return new ProcessFixture(
                rootPath,
                workspace,
                new WindowsProcessRunner(new FixedExecutableResolver(dotnetHost)),
                helperAssembly);
        }

        public CommandSpec CreateCommand(
            IEnumerable<string?> helperArguments,
            TimeSpan? timeout = null,
            IEnumerable<KeyValuePair<string, ProcessEnvironmentValue?>>? environmentVariables = null,
            IEnumerable<SensitiveProcessValue?>? redactionNeedles = null)
        {
            var arguments = new List<string?> { HelperAssemblyPath };
            arguments.AddRange(helperArguments);
            return CommandSpec.Create(
                ExecutableIdentity.Create("dotnet").Value,
                arguments,
                Workspace,
                Relative("work"),
                environmentVariables ?? [],
                timeout ?? TimeSpan.FromSeconds(10),
                [0],
                redactionNeedles ?? []).Value;
        }

        public ValueTask DisposeAsync()
        {
            var fullPath = Path.GetFullPath(RootPath);
            if (!fullPath.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(fullPath).StartsWith("DevForge-M3-Process-", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Refusing to clean an unexpected process test directory.");
            }

            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, recursive: true);
            }

            return ValueTask.CompletedTask;
        }

        private static WorkspaceRelativePath Relative(string value)
        {
            return WorkspaceRelativePath.Create(value).Value;
        }

        public static string FindDotNetHost()
        {
            var configured = System.Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            {
                return configured;
            }

            var runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
            var candidate = Path.GetFullPath(
                Path.Combine(runtimeDirectory, "..", "..", "..", "dotnet.exe"));
            return File.Exists(candidate)
                ? candidate
                : throw new FileNotFoundException("The test dotnet host was not found.");
        }

        private static string FindHelperAssembly()
        {
            var root = FindRepositoryRoot(AppContext.BaseDirectory);
            var path = Path.Combine(
                root,
                "tests",
                "DevForge.ProcessTestHelper",
                "bin",
                "Release",
                "net10.0",
                "DevForge.ProcessTestHelper.dll");
            return File.Exists(path)
                ? path
                : throw new FileNotFoundException("The process test helper was not built.");
        }

        private static string FindRepositoryRoot(string startDirectory)
        {
            for (var directory = new DirectoryInfo(Path.GetFullPath(startDirectory));
                 directory is not null;
                 directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "DevForge.sln")))
                {
                    return directory.FullName;
                }
            }

            throw new DirectoryNotFoundException("Could not locate the repository root.");
        }
    }
}
