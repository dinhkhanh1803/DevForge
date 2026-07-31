using System.Collections.Immutable;
using System.Reflection;
using DevForge.Application.Contracts;
using DevForge.Domain.Privacy;

namespace DevForge.UnitTests.Application;

public sealed class ProcessContractTests
{
    [Fact]
    public void CommandSpecSeparatesExecutableFromImmutableArgumentsAndRedactedValues()
    {
        var arguments = new List<string?> { "build", "--configuration", "Release" };
        var environment = new List<KeyValuePair<string, RedactedText?>>
        {
            KeyValuePair.Create<string, RedactedText?>("DOTNET_NOLOGO", Redacted("1")),
        };
        var allowedExitCodes = new List<int> { 0 };
        var redactedValues = new List<RedactedText?> { Redacted("[REDACTED]") };

        var result = CommandSpec.Create(
            "dotnet",
            arguments,
            "C:\\work",
            environment,
            TimeSpan.FromMinutes(5),
            allowedExitCodes,
            redactedValues);

        Assert.True(result.IsValid);
        arguments[0] = "changed";
        environment.Clear();
        allowedExitCodes.Clear();
        redactedValues.Clear();
        Assert.Equal("dotnet", result.Value.FileName);
        Assert.Equal(["build", "--configuration", "Release"], result.Value.ArgumentList.ToArray());
        Assert.Equal("1", result.Value.EnvironmentVariables["DOTNET_NOLOGO"].Value);
        Assert.Equal([0], result.Value.AllowedExitCodes.ToArray());
        Assert.Equal("[REDACTED]", Assert.Single(result.Value.RedactedValues).Value);
        Assert.IsType<ImmutableArray<string>>(result.Value.ArgumentList);
        Assert.Null(typeof(CommandSpec).GetProperty("CommandLine"));
        Assert.Null(typeof(CommandSpec).GetProperty("ShellCommand"));
    }

    [Fact]
    public void CommandSpecSnapshotsEverySequenceExactlyOnce()
    {
        var arguments = new SingleUseEnumerable<string?>(["build"]);
        var environment = new SingleUseEnumerable<KeyValuePair<string, RedactedText?>>(
            [KeyValuePair.Create<string, RedactedText?>("CI", Redacted("true"))]);
        var allowedExitCodes = new SingleUseEnumerable<int>([0]);
        var redactedValues = new SingleUseEnumerable<RedactedText?>([Redacted("[REDACTED]")]);

        var result = CommandSpec.Create(
            "dotnet",
            arguments,
            "C:\\work",
            environment,
            TimeSpan.FromMinutes(1),
            allowedExitCodes,
            redactedValues);

        Assert.True(result.IsValid);
        Assert.Equal(1, arguments.EnumerationCount);
        Assert.Equal(1, environment.EnumerationCount);
        Assert.Equal(1, allowedExitCodes.EnumerationCount);
        Assert.Equal(1, redactedValues.EnumerationCount);
    }

    [Fact]
    public void CommandSpecAggregatesUnsafeInputIssues()
    {
        var result = CommandSpec.Create(
            "cmd.exe",
            ["/c", null],
            "relative",
            [KeyValuePair.Create<string, RedactedText?>("api_token", null)],
            TimeSpan.Zero,
            [],
            [null]);

        Assert.False(result.IsValid);
        Assert.Equal(
            [
                "process.executable.shell-forbidden",
                "process.argument.required",
                "process.working-directory.absolute",
                "process.environment.name.secret-shaped",
                "process.environment.value.required",
                "process.timeout.invalid",
                "process.allowed-exit-codes.required",
                "process.redacted-value.required",
            ],
            result.Issues.Select(issue => issue.Code));
    }

    [Fact]
    public void ProcessOutputAndRetainedResultAreRedactedAndBounded()
    {
        var line = ProcessOutputLine.Create(ProcessOutputChannel.StandardOutput, Redacted("Build succeeded")).Value;
        var result = ProcessResult.Create(0, timedOut: false, cancelled: false, [line]);

        Assert.True(result.IsValid);
        Assert.Equal(typeof(RedactedText), typeof(ProcessOutputLine).GetProperty(nameof(ProcessOutputLine.Text))?.PropertyType);
        Assert.DoesNotContain(
            typeof(ProcessResult).GetProperties(BindingFlags.Public | BindingFlags.Instance),
            property => property.Name is "Output" or "StandardOutput" or "StandardError");

        var excessiveLines = Enumerable.Repeat(line, ProcessResult.MaxRetainedOutputLines + 1);
        var excessive = ProcessResult.Create(0, timedOut: false, cancelled: false, excessiveLines);
        Assert.False(excessive.IsValid);
        Assert.Equal("process.output.too-large", Assert.Single(excessive.Issues).Code);
    }

    [Fact]
    public void ProcessEnumsReserveZeroForInvalidDefault()
    {
        Assert.False(Enum.IsDefined((ProcessOutputChannel)0));
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
}
