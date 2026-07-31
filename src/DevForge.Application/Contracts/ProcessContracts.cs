using System.Collections.Immutable;
using DevForge.Domain.Privacy;
using DevForge.Domain.Validation;

namespace DevForge.Application.Contracts;

public enum ProcessOutputChannel
{
    StandardOutput = 1,
    StandardError = 2,
}

public sealed class ProcessOutputLine
{
    private ProcessOutputLine(ProcessOutputChannel channel, RedactedText text)
    {
        Stream = channel;
        Text = text;
    }

    public ProcessOutputChannel Stream { get; }

    public RedactedText Text { get; }

    public static ValidationResult<ProcessOutputLine> Create(
        ProcessOutputChannel channel,
        RedactedText? text)
    {
        var issues = new List<ValidationIssue>();
        if (!Enum.IsDefined(channel))
        {
            issues.Add(
                new ValidationIssue(
                    "process.output.channel.invalid",
                    "The process output channel is not defined.",
                    "channel"));
        }

        if (text is null)
        {
            issues.Add(
                new ValidationIssue(
                    "process.output.text.required",
                    "Redacted process output text is required.",
                    "text"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new ProcessOutputLine(channel, text!))
            : ValidationResult.Failure<ProcessOutputLine>(issues);
    }
}

public sealed class CommandSpec
{
    private static readonly ImmutableHashSet<string> _forbiddenShellExecutables =
        ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "cmd",
            "cmd.exe",
            "powershell",
            "powershell.exe",
            "pwsh",
            "pwsh.exe");

    private CommandSpec(
        string fileName,
        ImmutableArray<string> argumentList,
        string workingDirectory,
        ImmutableDictionary<string, RedactedText> environmentVariables,
        TimeSpan timeout,
        ImmutableHashSet<int> allowedExitCodes,
        ImmutableArray<RedactedText> redactedValues)
    {
        FileName = fileName;
        ArgumentList = argumentList;
        WorkingDirectory = workingDirectory;
        EnvironmentVariables = environmentVariables;
        Timeout = timeout;
        AllowedExitCodes = allowedExitCodes;
        RedactedValues = redactedValues;
    }

    public string FileName { get; }

    public ImmutableArray<string> ArgumentList { get; }

    public string WorkingDirectory { get; }

    public ImmutableDictionary<string, RedactedText> EnvironmentVariables { get; }

    public TimeSpan Timeout { get; }

    public ImmutableHashSet<int> AllowedExitCodes { get; }

    public ImmutableArray<RedactedText> RedactedValues { get; }

    public static ValidationResult<CommandSpec> Create(
        string? fileName,
        IEnumerable<string?>? argumentList,
        string? workingDirectory,
        IEnumerable<KeyValuePair<string, RedactedText?>>? environmentVariables,
        TimeSpan timeout,
        IEnumerable<int>? allowedExitCodes,
        IEnumerable<RedactedText?>? redactedValues)
    {
        var argumentSnapshot = argumentList?.ToImmutableArray() ?? [];
        var environmentSnapshot = environmentVariables?.ToImmutableArray() ?? [];
        var exitCodeSnapshot = allowedExitCodes?.ToImmutableArray() ?? [];
        var redactedValueSnapshot = redactedValues?.ToImmutableArray() ?? [];
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(fileName))
        {
            issues.Add(
                new ValidationIssue(
                    "process.executable.required",
                    "A process executable is required.",
                    "fileName"));
        }
        else if (_forbiddenShellExecutables.Contains(Path.GetFileName(fileName.Trim())))
        {
            issues.Add(
                new ValidationIssue(
                    "process.executable.shell-forbidden",
                    "Command shells cannot be used as process executables.",
                    "fileName"));
        }

        for (var index = 0; index < argumentSnapshot.Length; index++)
        {
            if (argumentSnapshot[index] is null)
            {
                issues.Add(
                    new ValidationIssue(
                        "process.argument.required",
                        "Process arguments cannot contain null values.",
                        $"argumentList[{index}]"));
            }
        }

        if (string.IsNullOrWhiteSpace(workingDirectory)
            || !Path.IsPathFullyQualified(workingDirectory))
        {
            issues.Add(
                new ValidationIssue(
                    "process.working-directory.absolute",
                    "The process working directory must be absolute.",
                    "workingDirectory"));
        }

        var environmentNames = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < environmentSnapshot.Length; index++)
        {
            var variable = environmentSnapshot[index];
            if (string.IsNullOrWhiteSpace(variable.Key))
            {
                issues.Add(
                    new ValidationIssue(
                        "process.environment.name.required",
                        "An environment variable name is required.",
                        $"environmentVariables[{index}].name"));
            }
            else
            {
                var normalizedName = variable.Key.Trim();
                if (!environmentNames.Add(normalizedName))
                {
                    issues.Add(
                        new ValidationIssue(
                            "process.environment.name.duplicate",
                            "Environment variable names must be unique.",
                            $"environmentVariables[{index}].name"));
                }
                else if (RedactedText.IsSecretShapedKey(normalizedName))
                {
                    issues.Add(
                        new ValidationIssue(
                            "process.environment.name.secret-shaped",
                            "Environment variable names cannot describe secrets.",
                            $"environmentVariables[{index}].name"));
                }
            }

            if (variable.Value is null)
            {
                issues.Add(
                    new ValidationIssue(
                        "process.environment.value.required",
                        "A redacted environment variable value is required.",
                        $"environmentVariables[{index}].value"));
            }
        }

        if (timeout <= TimeSpan.Zero)
        {
            issues.Add(
                new ValidationIssue(
                    "process.timeout.invalid",
                    "The process timeout must be positive.",
                    "timeout"));
        }

        if (exitCodeSnapshot.IsEmpty)
        {
            issues.Add(
                new ValidationIssue(
                    "process.allowed-exit-codes.required",
                    "At least one allowed process exit code is required.",
                    "allowedExitCodes"));
        }

        for (var index = 0; index < redactedValueSnapshot.Length; index++)
        {
            if (redactedValueSnapshot[index] is null)
            {
                issues.Add(
                    new ValidationIssue(
                        "process.redacted-value.required",
                        "Redacted process values cannot contain null values.",
                        $"redactedValues[{index}]"));
            }
        }

        if (issues.Count != 0)
        {
            return ValidationResult.Failure<CommandSpec>(issues);
        }

        var normalizedEnvironment = environmentSnapshot.Select(
            variable => KeyValuePair.Create(variable.Key.Trim(), variable.Value!));
        return ValidationResult.Success(
            new CommandSpec(
                fileName!.Trim(),
                [.. argumentSnapshot.Select(argument => argument!)],
                Path.GetFullPath(workingDirectory!),
                normalizedEnvironment.ToImmutableDictionary(StringComparer.Ordinal),
                timeout,
                exitCodeSnapshot.ToImmutableHashSet(),
                [.. redactedValueSnapshot.Select(value => value!)]));
    }
}

public sealed class ProcessResult
{
    public const int MaxRetainedOutputLines = 200;

    private ProcessResult(
        int? exitCode,
        bool timedOut,
        bool cancelled,
        ImmutableArray<ProcessOutputLine> retainedLines)
    {
        ExitCode = exitCode;
        TimedOut = timedOut;
        Cancelled = cancelled;
        RetainedLines = retainedLines;
    }

    public int? ExitCode { get; }

    public bool TimedOut { get; }

    public bool Cancelled { get; }

    public ImmutableArray<ProcessOutputLine> RetainedLines { get; }

    public static ValidationResult<ProcessResult> Create(
        int? exitCode,
        bool timedOut,
        bool cancelled,
        IEnumerable<ProcessOutputLine?>? retainedLines)
    {
        var lineSnapshot = retainedLines?.ToImmutableArray() ?? [];
        var issues = new List<ValidationIssue>();
        if (retainedLines is null)
        {
            issues.Add(
                new ValidationIssue(
                    "process.output.required",
                    "Retained process output lines are required.",
                    "retainedLines"));
        }

        if (lineSnapshot.Length > MaxRetainedOutputLines)
        {
            issues.Add(
                new ValidationIssue(
                    "process.output.too-large",
                    $"No more than {MaxRetainedOutputLines} process output lines may be retained.",
                    "retainedLines"));
        }

        for (var index = 0; index < lineSnapshot.Length; index++)
        {
            if (lineSnapshot[index] is null)
            {
                issues.Add(
                    new ValidationIssue(
                        "process.output.line.required",
                        "Retained process output cannot contain null lines.",
                        $"retainedLines[{index}]"));
            }
        }

        if (timedOut && cancelled)
        {
            issues.Add(
                new ValidationIssue(
                    "process.result.completion.invalid",
                    "A process result cannot be both timed out and cancelled.",
                    "timedOut"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(
                new ProcessResult(
                    exitCode,
                    timedOut,
                    cancelled,
                    [.. lineSnapshot.Select(line => line!)]))
            : ValidationResult.Failure<ProcessResult>(issues);
    }
}

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        CommandSpec command,
        IProgress<ProcessOutputLine>? progress,
        CancellationToken cancellationToken);
}
