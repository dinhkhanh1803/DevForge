using System.Collections.Immutable;
using System.Reflection;
using DevForge.Application.Contracts;
using DevForge.Domain.Privacy;

namespace DevForge.UnitTests.Application;

public sealed class ProcessContractTests
{
    [Fact]
    public void CommandSpecSnapshotsImmutableArgumentsEnvironmentExitCodesAndNeedles()
    {
        var sensitive = SensitiveProcessValue.Create("representative-sensitive-value").Value;
        var arguments = new List<string?> { "build", "--configuration", "Release" };
        var environment = new List<KeyValuePair<string, ProcessEnvironmentValue?>>
        {
            KeyValuePair.Create<string, ProcessEnvironmentValue?>(
                "CI",
                ProcessEnvironmentValue.CreateSafe("true").Value),
            KeyValuePair.Create<string, ProcessEnvironmentValue?>(
                "GITHUB_TOKEN",
                ProcessEnvironmentValue.CreateSensitive(sensitive).Value),
        };
        var allowedExitCodes = new List<int> { 0 };
        var needles = new List<SensitiveProcessValue?> { sensitive };
        var workspace = new StubWorkspaceFileSystem();
        var workingDirectory = WorkspaceRelativePath.Create("src").Value;

        var result = CommandSpec.Create(
            ExecutableIdentity.Create("dotnet").Value,
            arguments,
            workspace,
            workingDirectory,
            environment,
            TimeSpan.FromMinutes(5),
            allowedExitCodes,
            needles);

        Assert.True(result.IsValid);
        arguments[0] = "changed";
        environment.Clear();
        allowedExitCodes.Clear();
        needles.Clear();
        Assert.Equal(ExecutableTool.DotNet, result.Value.Executable.Tool);
        Assert.Equal(["build", "--configuration", "Release"], result.Value.ArgumentList.ToArray());
        Assert.Equal(ProcessValueSensitivity.Safe, result.Value.EnvironmentVariables["CI"].Sensitivity);
        Assert.Equal([0], result.Value.AllowedExitCodes.ToArray());
        Assert.Equal(sensitive, Assert.Single(result.Value.RedactionNeedles));
        Assert.Same(workspace, result.Value.Workspace);
        Assert.Equal(workingDirectory, result.Value.WorkingDirectory);
        Assert.IsType<ImmutableArray<string>>(result.Value.ArgumentList);
        Assert.Null(typeof(CommandSpec).GetProperty("CommandLine"));
        Assert.Null(typeof(CommandSpec).GetProperty("ShellCommand"));
    }

    [Fact]
    public void CommandSpecSnapshotsEverySequenceExactlyOnce()
    {
        var arguments = new SingleUseEnumerable<string?>(["build"]);
        var environment = new SingleUseEnumerable<KeyValuePair<string, ProcessEnvironmentValue?>>(
            [
                KeyValuePair.Create<string, ProcessEnvironmentValue?>(
                    "CI",
                    ProcessEnvironmentValue.CreateSafe("true").Value),
            ]);
        var allowedExitCodes = new SingleUseEnumerable<int>([0]);
        var needles = new SingleUseEnumerable<SensitiveProcessValue?>([]);

        var result = CommandSpec.Create(
            ExecutableIdentity.Create("dotnet").Value,
            arguments,
            new StubWorkspaceFileSystem(),
            WorkspaceRelativePath.Create("src").Value,
            environment,
            TimeSpan.FromMinutes(1),
            allowedExitCodes,
            needles);

        Assert.True(result.IsValid);
        Assert.Equal(1, arguments.EnumerationCount);
        Assert.Equal(1, environment.EnumerationCount);
        Assert.Equal(1, allowedExitCodes.EnumerationCount);
        Assert.Equal(1, needles.EnumerationCount);
    }

    [Fact]
    public void CommandSpecAggregatesUnsafeInputIssues()
    {
        var result = CommandSpec.Create(
            null,
            ["/c", null],
            null,
            null,
            [
                KeyValuePair.Create<string, ProcessEnvironmentValue?>(
                    "api_token",
                    ProcessEnvironmentValue.CreateSafe("safe").Value),
            ],
            TimeSpan.Zero,
            [],
            [null]);

        Assert.False(result.IsValid);
        Assert.Equal(
            [
                "process.executable.required",
                "process.argument.required",
                "process.workspace.required",
                "process.working-directory.required",
                "process.environment.sensitivity.required",
                "process.timeout.invalid",
                "process.allowed-exit-codes.required",
                "process.redaction-needle.required",
            ],
            result.Issues.Select(issue => issue.Code));
    }

    [Fact]
    public void ProcessOutputAndRetainedResultAreRedactedAndBounded()
    {
        var line = ProcessOutputLine.Create(
            ProcessOutputChannel.StandardOutput,
            Redacted("Build succeeded")).Value;
        var result = ProcessResult.Create(ProcessTerminationReason.Exited, 0, [line]);

        Assert.True(result.IsValid);
        Assert.Equal(
            typeof(RedactedText),
            typeof(ProcessOutputLine).GetProperty(nameof(ProcessOutputLine.Text))?.PropertyType);
        Assert.DoesNotContain(
            typeof(ProcessResult).GetProperties(BindingFlags.Public | BindingFlags.Instance),
            property => property.Name is "Output" or "StandardOutput" or "StandardError");

        var excessiveLines = Enumerable.Repeat<ProcessOutputLine?>(
            line,
            ProcessResult.MaxRetainedOutputLines + 1);
        var excessive = ProcessResult.Create(ProcessTerminationReason.Exited, 0, excessiveLines);
        Assert.True(excessive.IsValid);
        Assert.True(excessive.Value.IsOutputTruncated);
        Assert.Equal(ProcessResult.MaxRetainedOutputLines, excessive.Value.RetainedLines.Length);
    }

    [Fact]
    public void ProcessEnumsReserveZeroForInvalidDefault()
    {
        Assert.False(Enum.IsDefined((ProcessOutputChannel)0));
        Assert.False(Enum.IsDefined((ExecutableTool)0));
        Assert.False(Enum.IsDefined((ProcessValueSensitivity)0));
        Assert.False(Enum.IsDefined((ProcessTerminationReason)0));
    }

    private static RedactedText Redacted(string value)
    {
        return RedactedText.FromTrustedRedaction(value).Value;
    }

    private sealed class SingleUseEnumerable<T>(IEnumerable<T> values) : IEnumerable<T>
    {
        private readonly IEnumerable<T> _values = values;

        public int EnumerationCount { get; private set; }

        public IEnumerator<T> GetEnumerator()
        {
            EnumerationCount++;
            if (EnumerationCount > 1)
            {
                throw new InvalidOperationException("The sequence was enumerated more than once.");
            }

            return _values.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    private sealed class StubWorkspaceFileSystem : IWorkspaceFileSystem
    {
        public WorkspaceRoot Root { get; } = WorkspaceRoot.Create("C:\\work").Value;

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

        public Task MoveAsync(
            WorkspaceRelativePath source,
            WorkspaceRelativePath destination,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}