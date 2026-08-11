using System.Collections.Immutable;
using DevForge.Domain.Privacy;
using DevForge.Domain.Validation;

namespace DevForge.Application.Contracts;

public enum ExecutableTool
{
    DotNet = 1,
    Git = 2,
    GitHubCli = 3,
    Node = 4,
    Npm = 5,
    Npx = 6,
    Pnpm = 7,
    Yarn = 8,
    Bun = 9,
    VisualStudioCode = 10,
    VisualStudio = 11,
    MsBuild = 12,
}

/// <summary>
/// Identifies an executable selected from DevForge's trusted MVP tool allowlist.
/// Future handlers map typed operations to one of these identities instead of accepting raw commands.
/// </summary>
public sealed record ExecutableIdentity
{
    private static readonly ImmutableDictionary<string, ExecutableTool> _knownTools =
        new Dictionary<string, ExecutableTool>(StringComparer.Ordinal)
        {
            ["dotnet"] = ExecutableTool.DotNet,
            ["git"] = ExecutableTool.Git,
            ["gh"] = ExecutableTool.GitHubCli,
            ["node"] = ExecutableTool.Node,
            ["npm"] = ExecutableTool.Npm,
            ["npx"] = ExecutableTool.Npx,
            ["pnpm"] = ExecutableTool.Pnpm,
            ["yarn"] = ExecutableTool.Yarn,
            ["bun"] = ExecutableTool.Bun,
            ["code"] = ExecutableTool.VisualStudioCode,
            ["devenv"] = ExecutableTool.VisualStudio,
            ["msbuild"] = ExecutableTool.MsBuild,
        }.ToImmutableDictionary(StringComparer.Ordinal);

    private ExecutableIdentity(ExecutableTool tool, string executableName)
    {
        Tool = tool;
        ExecutableName = executableName;
    }

    public ExecutableTool Tool { get; }

    internal string ExecutableName { get; }

    public static ValidationResult<ExecutableIdentity> Create(string? trustedToolName)
    {
        if (string.IsNullOrWhiteSpace(trustedToolName)
            || !_knownTools.TryGetValue(trustedToolName, out var tool))
        {
            return ValidationResult.Failure<ExecutableIdentity>(
            [
                new ValidationIssue(
                    "process.executable.untrusted",
                    "The executable is not a trusted DevForge tool identity.",
                    "trustedToolName"),
            ]);
        }

        return ValidationResult.Success(new ExecutableIdentity(tool, trustedToolName));
    }

    public override string ToString()
    {
        return ExecutableName;
    }
}

public sealed class SensitiveProcessValue
{
    private const int MaxLength = 32_767;
    private readonly string _content;

    private SensitiveProcessValue(string content)
    {
        _content = content;
    }

    public static ValidationResult<SensitiveProcessValue> Create(string? content)
    {
        if (string.IsNullOrEmpty(content) || content.Length > MaxLength || content.Contains('\0'))
        {
            return ValidationResult.Failure<SensitiveProcessValue>(
            [
                new ValidationIssue(
                    "process.sensitive-value.invalid",
                    "A sensitive process value must be nonempty, bounded, and contain no null characters.",
                    "content"),
            ]);
        }

        return ValidationResult.Success(new SensitiveProcessValue(content));
    }

    internal string RevealForProcessStart()
    {
        return _content;
    }

    public override string ToString()
    {
        return "[REDACTED]";
    }
}

public enum ProcessValueSensitivity
{
    Safe = 1,
    Sensitive = 2,
}

public sealed class ProcessEnvironmentValue
{
    private const int MaxLength = 32_767;
    private readonly string? _safeContent;
    private readonly SensitiveProcessValue? _sensitiveContent;

    private ProcessEnvironmentValue(string safeContent)
    {
        Sensitivity = ProcessValueSensitivity.Safe;
        _safeContent = safeContent;
    }

    private ProcessEnvironmentValue(SensitiveProcessValue sensitiveContent)
    {
        Sensitivity = ProcessValueSensitivity.Sensitive;
        _sensitiveContent = sensitiveContent;
    }

    public ProcessValueSensitivity Sensitivity { get; }

    public static ValidationResult<ProcessEnvironmentValue> CreateSafe(string? content)
    {
        if (content is null || content.Length > MaxLength || content.Contains('\0'))
        {
            return ValidationResult.Failure<ProcessEnvironmentValue>(
            [
                new ValidationIssue(
                    "process.environment.safe-value.invalid",
                    "A safe environment value must be bounded and contain no null characters.",
                    "content"),
            ]);
        }

        return ValidationResult.Success(new ProcessEnvironmentValue(content));
    }

    public static ValidationResult<ProcessEnvironmentValue> CreateSensitive(
        SensitiveProcessValue? content)
    {
        return content is null
            ? ValidationResult.Failure<ProcessEnvironmentValue>(
            [
                new ValidationIssue(
                    "process.environment.sensitive-value.required",
                    "A sensitive environment value is required.",
                    "content"),
            ])
            : ValidationResult.Success(new ProcessEnvironmentValue(content));
    }

    internal string RevealForProcessStart()
    {
        return Sensitivity == ProcessValueSensitivity.Safe
            ? _safeContent!
            : _sensitiveContent!.RevealForProcessStart();
    }

    internal SensitiveProcessValue? SensitiveContent => _sensitiveContent;

    public override string ToString()
    {
        return Sensitivity == ProcessValueSensitivity.Sensitive ? "[REDACTED]" : "[SAFE]";
    }
}

public enum ProcessOutputChannel
{
    StandardOutput = 1,
    StandardError = 2,
}

public sealed record ProcessOutputLine
{
    public const int MaxTextLength = 4_096;

    private ProcessOutputLine(ProcessOutputChannel channel, RedactedText text)
    {
        Channel = channel;
        Text = text;
    }

    public ProcessOutputChannel Channel { get; }

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
        else if (text.Value.Length > MaxTextLength)
        {
            issues.Add(
                new ValidationIssue(
                    "process.output.text.too-long",
                    "Process output lines exceed the retained line limit.",
                    "text"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new ProcessOutputLine(channel, text!))
            : ValidationResult.Failure<ProcessOutputLine>(issues);
    }
}

public sealed class CommandSpec
{
    public const int MaxArgumentCount = 256;
    public const int MaxArgumentLength = 8_192;
    public const int MaxTotalArgumentLength = 32_767;
    public const int MaxEnvironmentVariables = 128;
    public const int MaxAllowedExitCodes = 32;
    public const int MaxRedactionNeedles = 64;
    public static readonly TimeSpan MaxTimeout = TimeSpan.FromHours(1);

    private static readonly ImmutableDictionary<ExecutableTool, ImmutableHashSet<string>> _forbiddenRawModes =
        new Dictionary<ExecutableTool, ImmutableHashSet<string>>
        {
            [ExecutableTool.Node] = ImmutableHashSet.Create(
                StringComparer.OrdinalIgnoreCase,
                "-e",
                "--eval",
                "-p",
                "--print"),
            [ExecutableTool.DotNet] = ImmutableHashSet.Create(
                StringComparer.OrdinalIgnoreCase,
                "exec"),
            [ExecutableTool.Npx] = ImmutableHashSet.Create(
                StringComparer.OrdinalIgnoreCase,
                "-c",
                "--call"),
        }.ToImmutableDictionary();

    private CommandSpec(
        ExecutableIdentity executable,
        ImmutableArray<string> argumentList,
        IWorkspaceFileSystem workspace,
        WorkspaceRelativePath? workingDirectory,
        bool usesWorkspaceRoot,
        ImmutableDictionary<string, ProcessEnvironmentValue> environmentVariables,
        TimeSpan timeout,
        ImmutableHashSet<int> allowedExitCodes,
        ImmutableArray<SensitiveProcessValue> redactionNeedles)
    {
        Executable = executable;
        ArgumentList = argumentList;
        Workspace = workspace;
        WorkingDirectory = workingDirectory;
        UsesWorkspaceRoot = usesWorkspaceRoot;
        EnvironmentVariables = environmentVariables;
        Timeout = timeout;
        AllowedExitCodes = allowedExitCodes;
        RedactionNeedles = redactionNeedles;
    }

    public ExecutableIdentity Executable { get; }

    public ImmutableArray<string> ArgumentList { get; }

    public IWorkspaceFileSystem Workspace { get; }

    public WorkspaceRelativePath? WorkingDirectory { get; }

    public bool UsesWorkspaceRoot { get; }

    public ImmutableDictionary<string, ProcessEnvironmentValue> EnvironmentVariables { get; }

    public TimeSpan Timeout { get; }

    public ImmutableHashSet<int> AllowedExitCodes { get; }

    public ImmutableArray<SensitiveProcessValue> RedactionNeedles { get; }

    public static ValidationResult<CommandSpec> Create(
        ExecutableIdentity? executable,
        IEnumerable<string?>? argumentList,
        IWorkspaceFileSystem? workspace,
        WorkspaceRelativePath? workingDirectory,
        IEnumerable<KeyValuePair<string, ProcessEnvironmentValue?>>? environmentVariables,
        TimeSpan timeout,
        IEnumerable<int>? allowedExitCodes,
        IEnumerable<SensitiveProcessValue?>? redactionNeedles)
    {
        return CreateCore(
            executable,
            argumentList,
            workspace,
            workingDirectory,
            usesWorkspaceRoot: false,
            environmentVariables,
            timeout,
            allowedExitCodes,
            redactionNeedles);
    }

    public static ValidationResult<CommandSpec> CreateAtWorkspaceRoot(
        ExecutableIdentity? executable,
        IEnumerable<string?>? argumentList,
        IWorkspaceFileSystem? workspace,
        IEnumerable<KeyValuePair<string, ProcessEnvironmentValue?>>? environmentVariables,
        TimeSpan timeout,
        IEnumerable<int>? allowedExitCodes,
        IEnumerable<SensitiveProcessValue?>? redactionNeedles)
    {
        return CreateCore(
            executable,
            argumentList,
            workspace,
            null,
            usesWorkspaceRoot: true,
            environmentVariables,
            timeout,
            allowedExitCodes,
            redactionNeedles);
    }

    private static ValidationResult<CommandSpec> CreateCore(
        ExecutableIdentity? executable,
        IEnumerable<string?>? argumentList,
        IWorkspaceFileSystem? workspace,
        WorkspaceRelativePath? workingDirectory,
        bool usesWorkspaceRoot,
        IEnumerable<KeyValuePair<string, ProcessEnvironmentValue?>>? environmentVariables,
        TimeSpan timeout,
        IEnumerable<int>? allowedExitCodes,
        IEnumerable<SensitiveProcessValue?>? redactionNeedles)
    {
        var argumentSnapshot = SnapshotBounded(argumentList, MaxArgumentCount);
        var environmentSnapshot = SnapshotBounded(environmentVariables, MaxEnvironmentVariables);
        var exitCodeSnapshot = SnapshotBounded(allowedExitCodes, MaxAllowedExitCodes);
        var needleSnapshot = SnapshotBounded(redactionNeedles, MaxRedactionNeedles);
        var issues = new List<ValidationIssue>();

        if (argumentSnapshot.Length > MaxArgumentCount)
        {
            issues.Add(new ValidationIssue(
                "process.argument.too-many",
                "The process argument list exceeds the supported bound.",
                "argumentList"));
        }

        if (environmentSnapshot.Length > MaxEnvironmentVariables)
        {
            issues.Add(new ValidationIssue(
                "process.environment.too-many",
                "The process environment exceeds the supported entry bound.",
                "environmentVariables"));
        }

        if (exitCodeSnapshot.Length > MaxAllowedExitCodes)
        {
            issues.Add(new ValidationIssue(
                "process.allowed-exit-codes.too-many",
                "The allowed exit-code set exceeds the supported bound.",
                "allowedExitCodes"));
        }

        if (needleSnapshot.Length > MaxRedactionNeedles)
        {
            issues.Add(new ValidationIssue(
                "process.redaction-needles.too-many",
                "The redaction needle set exceeds the supported bound.",
                "redactionNeedles"));
        }

        if (executable is null)
        {
            issues.Add(
                new ValidationIssue(
                    "process.executable.required",
                    "A trusted executable identity is required.",
                    "executable"));
        }

        long totalArgumentLength = 0;
        for (var index = 0; index < argumentSnapshot.Length; index++)
        {
            var argument = argumentSnapshot[index];
            if (argument is null)
            {
                issues.Add(
                    new ValidationIssue(
                        "process.argument.required",
                        "Process arguments cannot contain null values.",
                        "argumentList[" + index + "]"));
            }
            else if (argument.Length > MaxArgumentLength)
            {
                issues.Add(new ValidationIssue(
                    "process.argument.too-large",
                    "A process argument exceeds the supported length.",
                    "argumentList[" + index + "]"));
            }
            else if (!RedactedText.FromTrustedRedaction(argument).IsValid)
            {
                issues.Add(
                    new ValidationIssue(
                        "process.argument.secret-shaped",
                        "Process arguments cannot carry secret-shaped values.",
                    "argumentList[" + index + "]"));
            }

            totalArgumentLength += argument?.Length ?? 0;
        }

        if (totalArgumentLength > MaxTotalArgumentLength)
        {
            issues.Add(new ValidationIssue(
                "process.argument.total-too-large",
                "The combined process argument text exceeds the supported length.",
                "argumentList"));
        }

        if (executable is not null && ContainsForbiddenRawMode(executable.Tool, argumentSnapshot))
        {
            issues.Add(
                new ValidationIssue(
                    "process.argument.raw-mode-forbidden",
                    "The executable cannot be invoked in a raw command or evaluation mode.",
                    "argumentList[0]"));
        }

        if (workspace is null)
        {
            issues.Add(
                new ValidationIssue(
                    "process.workspace.required",
                    "A scoped workspace is required.",
                    "workspace"));
        }

        if (!usesWorkspaceRoot && workingDirectory is null)
        {
            issues.Add(
                new ValidationIssue(
                    "process.working-directory.required",
                    "A guarded workspace-relative working directory is required.",
                    "workingDirectory"));
        }

        var environmentNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < environmentSnapshot.Length; index++)
        {
            var variable = environmentSnapshot[index];
            if (string.IsNullOrWhiteSpace(variable.Key))
            {
                issues.Add(
                    new ValidationIssue(
                        "process.environment.name.required",
                        "An environment variable name is required.",
                        "environmentVariables[" + index + "].name"));
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
                            "environmentVariables[" + index + "].name"));
                }
                else if (RedactedText.IsSecretShapedKey(normalizedName)
                    && variable.Value?.Sensitivity != ProcessValueSensitivity.Sensitive)
                {
                    issues.Add(
                        new ValidationIssue(
                            "process.environment.sensitivity.required",
                            "Secret-shaped environment variables require an ephemeral sensitive value.",
                            "environmentVariables[" + index + "].value"));
                }
            }

            if (variable.Value is null)
            {
                issues.Add(
                    new ValidationIssue(
                        "process.environment.value.required",
                        "An environment variable value is required.",
                        "environmentVariables[" + index + "].value"));
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
        else if (timeout > MaxTimeout)
        {
            issues.Add(new ValidationIssue(
                "process.timeout.too-large",
                "The process timeout exceeds the supported bound.",
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

        for (var index = 0; index < needleSnapshot.Length; index++)
        {
            if (needleSnapshot[index] is null)
            {
                issues.Add(
                    new ValidationIssue(
                        "process.redaction-needle.required",
                        "Redaction needles cannot contain null values.",
                        "redactionNeedles[" + index + "]"));
            }
        }

        var sensitiveEnvironmentValues = environmentSnapshot
            .Where(item => item.Value?.Sensitivity == ProcessValueSensitivity.Sensitive)
            .Select(item => item.Value!.SensitiveContent!)
            .ToImmutableArray();
        if (sensitiveEnvironmentValues.Any(
            sensitive => !needleSnapshot.Any(needle => ReferenceEquals(needle, sensitive))))
        {
            issues.Add(
                new ValidationIssue(
                    "process.redaction-needle.missing",
                    "Every sensitive environment value must also be supplied as a redaction needle.",
                    "redactionNeedles"));
        }

        if (issues.Count != 0)
        {
            return ValidationResult.Failure<CommandSpec>(issues);
        }

        var normalizedEnvironment = environmentSnapshot.Select(
            variable => KeyValuePair.Create(variable.Key.Trim(), variable.Value!));
        return ValidationResult.Success(
            new CommandSpec(
                executable!,
                [.. argumentSnapshot.Select(argument => argument!)],
                workspace!,
                workingDirectory,
                usesWorkspaceRoot,
                normalizedEnvironment.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase),
                timeout,
                exitCodeSnapshot.ToImmutableHashSet(),
                [.. needleSnapshot.Select(needle => needle!)]));
    }

    private static ImmutableArray<T> SnapshotBounded<T>(IEnumerable<T>? source, int maximum)
    {
        if (source is null)
        {
            return [];
        }

        var snapshot = ImmutableArray.CreateBuilder<T>(maximum + 1);
        using var enumerator = source.GetEnumerator();
        while (snapshot.Count <= maximum && enumerator.MoveNext())
        {
            snapshot.Add(enumerator.Current);
        }

        return snapshot.ToImmutable();
    }

    private static bool ContainsForbiddenRawMode(
        ExecutableTool tool,
        ImmutableArray<string?> arguments)
    {
        if (!_forbiddenRawModes.TryGetValue(tool, out var forbiddenModes))
        {
            return false;
        }

        return arguments.Where(argument => argument is not null).Any(argument =>
            forbiddenModes.Contains(argument!)
            || tool == ExecutableTool.Node
                && (argument!.StartsWith("--eval=", StringComparison.OrdinalIgnoreCase)
                    || argument.StartsWith("--print=", StringComparison.OrdinalIgnoreCase)
                    || argument.StartsWith("-e", StringComparison.OrdinalIgnoreCase)
                        && argument.Length > 2
                    || argument.StartsWith("-p", StringComparison.OrdinalIgnoreCase)
                        && argument.Length > 2)
            || tool == ExecutableTool.Npx
                && (argument!.StartsWith("--call=", StringComparison.OrdinalIgnoreCase)
                    || argument.StartsWith("-c", StringComparison.OrdinalIgnoreCase)
                        && argument.Length > 2));
    }

    public override string ToString()
    {
        return "[COMMAND:" + Executable + "]";
    }
}

public enum ProcessTerminationReason
{
    Exited = 1,
    TimedOut = 2,
    Cancelled = 3,
}

public sealed class ProcessResult
{
    public const int MaxRetainedOutputLines = 200;
    public const int MaxRetainedOutputCharacters = 65_536;

    private ProcessResult(
        ProcessTerminationReason terminationReason,
        int? exitCode,
        ImmutableArray<ProcessOutputLine> retainedLines,
        int retainedCharacterCount,
        bool isOutputTruncated)
    {
        TerminationReason = terminationReason;
        ExitCode = exitCode;
        RetainedLines = retainedLines;
        RetainedCharacterCount = retainedCharacterCount;
        IsOutputTruncated = isOutputTruncated;
    }

    public ProcessTerminationReason TerminationReason { get; }

    public int? ExitCode { get; }

    public ImmutableArray<ProcessOutputLine> RetainedLines { get; }

    public int RetainedCharacterCount { get; }

    public bool IsOutputTruncated { get; }

    public static ValidationResult<ProcessResult> Create(
        ProcessTerminationReason terminationReason,
        int? exitCode,
        IEnumerable<ProcessOutputLine?>? retainedLines,
        bool wasOutputTruncated = false)
    {
        var issues = new List<ValidationIssue>();
        if (!Enum.IsDefined(terminationReason))
        {
            issues.Add(
                new ValidationIssue(
                    "process.termination-reason.invalid",
                    "The process termination reason is not defined.",
                    "terminationReason"));
        }

        if (terminationReason == ProcessTerminationReason.Exited && exitCode is null)
        {
            issues.Add(
                new ValidationIssue(
                    "process.exit-code.required",
                    "An exited process requires an exit code.",
                    "exitCode"));
        }
        else if (terminationReason is ProcessTerminationReason.TimedOut or ProcessTerminationReason.Cancelled
            && exitCode is not null)
        {
            issues.Add(
                new ValidationIssue(
                    "process.exit-code.unexpected",
                    "A timed out or cancelled process cannot carry an exit code.",
                    "exitCode"));
        }

        if (retainedLines is null)
        {
            issues.Add(
                new ValidationIssue(
                    "process.output.required",
                    "Retained process output lines are required.",
                    "retainedLines"));
        }

        if (issues.Count != 0)
        {
            return ValidationResult.Failure<ProcessResult>(issues);
        }

        var lines = ImmutableArray.CreateBuilder<ProcessOutputLine>();
        var retainedCharacterCount = 0;
        var isTruncated = wasOutputTruncated;
        var observedLineCount = 0;
        using var enumerator = retainedLines!.GetEnumerator();
        while (observedLineCount <= MaxRetainedOutputLines && enumerator.MoveNext())
        {
            observedLineCount++;
            if (observedLineCount > MaxRetainedOutputLines)
            {
                isTruncated = true;
                break;
            }

            var line = enumerator.Current;
            if (line is null)
            {
                issues.Add(
                    new ValidationIssue(
                        "process.output.line.required",
                        "Retained process output cannot contain null lines.",
                        "retainedLines[" + (observedLineCount - 1) + "]"));
                continue;
            }

            if (retainedCharacterCount + line.Text.Value.Length > MaxRetainedOutputCharacters)
            {
                isTruncated = true;
                break;
            }

            lines.Add(line);
            retainedCharacterCount += line.Text.Value.Length;
        }

        return issues.Count == 0
            ? ValidationResult.Success(
                new ProcessResult(
                    terminationReason,
                    exitCode,
                    lines.ToImmutable(),
                    retainedCharacterCount,
                    isTruncated))
            : ValidationResult.Failure<ProcessResult>(issues);
    }
}

public interface IProcessRunner
{
    Task CheckPreconditionsAsync(
        CommandSpec command,
        CancellationToken cancellationToken);

    Task<ProcessResult> RunAsync(
        CommandSpec command,
        IProgress<ProcessOutputLine>? progress,
        CancellationToken cancellationToken);
}
