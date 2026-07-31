using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using DevForge.Application.Contracts;
using DevForge.Domain.Privacy;

namespace DevForge.UnitTests.Application;

public sealed class ProcessSecurityReviewTests
{
    [Fact]
    public void SensitiveProcessValueHasNoPublicRevealOrSerializationSurface()
    {
        const string raw = "github_pat_11AA22BB33CC44DD55EE66FF77GG88HH";

        var sensitive = SensitiveProcessValue.Create(raw).Value;

        Assert.Equal("[REDACTED]", sensitive.ToString());
        Assert.DoesNotContain(raw, JsonSerializer.Serialize(sensitive), StringComparison.Ordinal);
        Assert.DoesNotContain(
            typeof(SensitiveProcessValue).GetProperties(BindingFlags.Public | BindingFlags.Instance),
            property => property.PropertyType == typeof(string));
        Assert.DoesNotContain(
            typeof(SensitiveProcessValue).GetMethods(BindingFlags.Public | BindingFlags.Instance),
            method => method.DeclaringType == typeof(SensitiveProcessValue)
                && method.Name.Contains("value", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("dotnet")]
    [InlineData("git")]
    [InlineData("gh")]
    [InlineData("node")]
    [InlineData("npm")]
    [InlineData("npx")]
    [InlineData("pnpm")]
    [InlineData("yarn")]
    [InlineData("bun")]
    [InlineData("code")]
    [InlineData("devenv")]
    [InlineData("msbuild")]
    public void ExecutableIdentityAcceptsOnlyKnownMvpTools(string name)
    {
        var result = ExecutableIdentity.Create(name);

        Assert.True(result.IsValid);
        Assert.NotEqual(0, (int)result.Value.Tool);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("C:\\tools\\dotnet.exe")]
    [InlineData("custom-tool")]
    [InlineData("cmd")]
    [InlineData("cmd.exe")]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    [InlineData("bash")]
    [InlineData("sh")]
    [InlineData("wsl")]
    [InlineData("cscript")]
    public void ExecutableIdentityRejectsArbitraryPathsNamesAndShells(string? name)
    {
        Assert.False(ExecutableIdentity.Create(name).IsValid);
    }

    [Theory]
    [InlineData("node", "--eval")]
    [InlineData("node", "-e")]
    [InlineData("node", "--print")]
    [InlineData("dotnet", "exec")]
    [InlineData("npx", "-c")]
    public void CommandSpecRejectsRawCommandAndEvalModes(string executable, string switchValue)
    {
        var result = CommandSpec.Create(
            ExecutableIdentity.Create(executable).Value,
            [switchValue, "Write-Output unsafe"],
            new StubWorkspaceFileSystem(),
            WorkspaceRelativePath.Create("build").Value,
            [],
            TimeSpan.FromMinutes(1),
            [0],
            []);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "process.argument.raw-mode-forbidden");
    }

    [Fact]
    public void CommandSpecCarriesScopedWorkspaceAndEphemeralSensitiveValuesWithoutLeakage()
    {
        const string raw = "github_pat_11AA22BB33CC44DD55EE66FF77GG88HH";
        var sensitive = SensitiveProcessValue.Create(raw).Value;
        var workspace = new StubWorkspaceFileSystem();
        var relativeDirectory = WorkspaceRelativePath.Create("src").Value;
        var environment = new List<KeyValuePair<string, ProcessEnvironmentValue?>>
        {
            KeyValuePair.Create<string, ProcessEnvironmentValue?>(
                "CI",
                ProcessEnvironmentValue.CreateSafe("true").Value),
            KeyValuePair.Create<string, ProcessEnvironmentValue?>(
                "GITHUB_TOKEN",
                ProcessEnvironmentValue.CreateSensitive(sensitive).Value),
        };

        var result = CommandSpec.Create(
            ExecutableIdentity.Create("dotnet").Value,
            ["build"],
            workspace,
            relativeDirectory,
            environment,
            TimeSpan.FromMinutes(5),
            [0],
            [sensitive]);

        Assert.True(result.IsValid);
        environment.Clear();
        Assert.Same(workspace, result.Value.Workspace);
        Assert.Equal(relativeDirectory, result.Value.WorkingDirectory);
        Assert.Equal(ProcessValueSensitivity.Sensitive, result.Value.EnvironmentVariables["GITHUB_TOKEN"].Sensitivity);
        Assert.Equal(sensitive, Assert.Single(result.Value.RedactionNeedles));
        Assert.DoesNotContain(raw, result.Value.ToString(), StringComparison.Ordinal);
        Assert.Null(typeof(CommandSpec).GetProperty("FileName"));
        Assert.DoesNotContain(
            typeof(CommandSpec).GetProperties(),
            property => property.PropertyType == typeof(string)
                && property.Name.Contains("Directory", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SecretShapedEnvironmentNameRequiresSensitiveValue()
    {
        var result = CommandSpec.Create(
            ExecutableIdentity.Create("gh").Value,
            ["repo", "create"],
            new StubWorkspaceFileSystem(),
            WorkspaceRelativePath.Create("repo").Value,
            [
                KeyValuePair.Create<string, ProcessEnvironmentValue?>(
                    "GITHUB_TOKEN",
                    ProcessEnvironmentValue.CreateSafe("not-secret").Value),
            ],
            TimeSpan.FromMinutes(1),
            [0],
            []);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "process.environment.sensitivity.required");
    }

    [Fact]
    public void ProcessOutputLineIsStructurallyEqualAndLengthBounded()
    {
        var text = Redacted("Build succeeded");
        var first = ProcessOutputLine.Create(ProcessOutputChannel.StandardOutput, text).Value;
        var same = ProcessOutputLine.Create(ProcessOutputChannel.StandardOutput, text).Value;

        Assert.Equal(first, same);
        Assert.False(
            ProcessOutputLine.Create(
                ProcessOutputChannel.StandardOutput,
                Redacted(new string('x', ProcessOutputLine.MaxTextLength + 1))).IsValid);
    }

    [Fact]
    public void ProcessResultStopsAnInfiniteSourceAndReportsTruncation()
    {
        var line = ProcessOutputLine.Create(ProcessOutputChannel.StandardOutput, Redacted("line")).Value;
        var infinite = new InfiniteEnumerable<ProcessOutputLine?>(line);

        var result = ProcessResult.Create(ProcessTerminationReason.Exited, 0, infinite);

        Assert.True(result.IsValid);
        Assert.True(result.Value.IsOutputTruncated);
        Assert.Equal(ProcessResult.MaxRetainedOutputLines, result.Value.RetainedLines.Length);
        Assert.Equal(ProcessResult.MaxRetainedOutputLines + 1, infinite.MoveNextCount);
    }

    [Fact]
    public void ProcessResultAppliesATotalCharacterCap()
    {
        var line = ProcessOutputLine.Create(
            ProcessOutputChannel.StandardOutput,
            Redacted(new string('x', 1_000))).Value;

        var result = ProcessResult.Create(
            ProcessTerminationReason.Exited,
            0,
            Enumerable.Repeat<ProcessOutputLine?>(line, 100));

        Assert.True(result.IsValid);
        Assert.True(result.Value.IsOutputTruncated);
        Assert.True(
            result.Value.RetainedLines.Sum(item => item.Text.Value.Length)
                <= ProcessResult.MaxRetainedOutputCharacters);
    }

    [Theory]
    [InlineData(ProcessTerminationReason.Exited, null)]
    [InlineData(ProcessTerminationReason.TimedOut, 1)]
    [InlineData(ProcessTerminationReason.Cancelled, 1)]
    public void ProcessResultEnforcesTerminationExitCodeInvariants(
        ProcessTerminationReason reason,
        int? exitCode)
    {
        Assert.False(ProcessResult.Create(reason, exitCode, []).IsValid);
    }

    [Fact]
    public void ProcessTerminationReasonReservesZeroForInvalidDefault()
    {
        Assert.False(Enum.IsDefined((ProcessTerminationReason)0));
    }

    private static RedactedText Redacted(string value)
    {
        return RedactedText.FromTrustedRedaction(value).Value;
    }

    private sealed class InfiniteEnumerable<T>(T value) : IEnumerable<T>
    {
        public int MoveNextCount { get; private set; }

        public IEnumerator<T> GetEnumerator()
        {
            while (true)
            {
                MoveNextCount++;
                yield return value;
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    private sealed class StubWorkspaceFileSystem : IWorkspaceFileSystem
    {
        public WorkspaceRoot Root { get; } = WorkspaceRoot.Create("C:\\work").Value;

        public Task<bool> FileExistsAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> DirectoryExistsAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task CreateDirectoryAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Stream> OpenReadAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Stream> OpenWriteAsync(
            WorkspaceRelativePath path,
            bool overwrite,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DeleteFileAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateFilesAsync(
            WorkspaceRelativePath directory,
            bool recursive,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DeleteDirectoryAsync(
            WorkspaceRelativePath path,
            DirectoryCleanupIntent intent,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task MoveDirectoryAsync(
            WorkspaceRelativePath source,
            WorkspaceRelativePath destination,
            WorkspaceMoveIntent intent,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
