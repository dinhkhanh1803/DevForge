using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using DevForge.Application.Contracts;
using DevForge.Domain.Diagnostics;
using DevForge.Domain.Execution;
using DevForge.Domain.Privacy;

namespace DevForge.Infrastructure.Execution;

internal sealed class RunProcessExecutionHandler(IProcessRunner runner) :
    ProcessExecutionHandlerBase(
        "run-process",
        handlesValidator: false,
        ExecutionResumeBehavior.ReplayFromFreshStaging,
        runner)
{
    private static readonly ImmutableHashSet<string> _inputs =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "executable",
            "arguments",
            "workingDirectory",
            "allowedExitCodes");

    protected override ImmutableHashSet<string> RequiredInputNames => _inputs;

    protected override CommandSpec CreateCommand(ProcessHandlerContext context) =>
        CreateGuardedCommand(context);

    private static CommandSpec CreateGuardedCommand(ProcessHandlerContext context)
    {
        var executable = Identifier(context, "executable");
        var arguments = Arguments(context);
        if (!StringComparer.Ordinal.Equals(executable, "dotnet")
            || arguments.IsEmpty
            || arguments[0] is not ("restore" or "build" or "test" or "format"))
        {
            throw new ProcessHandlerInputException();
        }

        return BuildCommand(context, executable, arguments, ExitCodes(context));
    }
}

internal sealed class PackageInstallExecutionHandler(IProcessRunner runner) :
    ProcessExecutionHandlerBase(
        "package-install",
        handlesValidator: false,
        ExecutionResumeBehavior.ReplayFromFreshStaging,
        runner)
{
    private static readonly ImmutableHashSet<string> _inputs =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "packageManager",
            "arguments",
            "workingDirectory");
    private static readonly ImmutableHashSet<string> _packageManagers =
        ImmutableHashSet.Create(StringComparer.Ordinal, "dotnet", "npm", "pnpm", "yarn", "bun");

    protected override ImmutableHashSet<string> RequiredInputNames => _inputs;

    protected override CommandSpec CreateCommand(ProcessHandlerContext context)
    {
        var packageManager = Identifier(context, "packageManager");
        if (!_packageManagers.Contains(packageManager))
        {
            throw new ProcessHandlerInputException();
        }

        var arguments = Arguments(context);
        if (!IsSafePackageOperation(packageManager, arguments))
        {
            throw new ProcessHandlerInputException();
        }

        return BuildCommand(context, packageManager, arguments, [0]);
    }

    private static bool IsSafePackageOperation(
        string packageManager,
        ImmutableArray<string> arguments)
    {
        if (arguments.IsEmpty)
        {
            return false;
        }

        if (StringComparer.Ordinal.Equals(packageManager, "dotnet"))
        {
            return arguments[0] == "restore"
                || arguments[0] == "add"
                    && arguments.Skip(1).Contains("package", StringComparer.Ordinal);
        }

        return arguments[0] is "install" or "ci"
            && arguments.Any(argument => argument is "--ignore-scripts" or "--ignore-scripts=true")
            && arguments.All(argument =>
                !argument.StartsWith("--no-ignore-scripts", StringComparison.Ordinal)
                && (!argument.StartsWith("--ignore-scripts=", StringComparison.Ordinal)
                    || argument == "--ignore-scripts=true"));
    }
}

internal sealed class ValidateCommandExecutionHandler(IProcessRunner runner) :
    ProcessExecutionHandlerBase(
        "validate-command",
        handlesValidator: true,
        ExecutionResumeBehavior.RevalidatePostcondition,
        runner)
{
    private static readonly ImmutableHashSet<string> _inputs =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "executable",
            "arguments",
            "workingDirectory",
            "allowedExitCodes",
            "required");

    protected override ImmutableHashSet<string> RequiredInputNames => _inputs;

    protected override string CommandFailureCode => "DF-VALID-001";

    protected override CommandSpec CreateCommand(ProcessHandlerContext context)
    {
        var required = Value(context, "required");
        if (required.Kind != PlanValueKind.Boolean
            || context.Request.IsValidator && required.BooleanValue != context.Request.Required)
        {
            throw new ProcessHandlerInputException();
        }

        var executable = Identifier(context, "executable");
        var arguments = Arguments(context);
        if (!StringComparer.Ordinal.Equals(executable, "dotnet")
            || arguments.IsEmpty
            || arguments[0] is not ("build" or "test" or "format")
                && !IsWpfPublishSmoke(arguments))
        {
            throw new ProcessHandlerInputException();
        }

        return BuildCommand(context, executable, arguments, ExitCodes(context));
    }

    private static bool IsWpfPublishSmoke(ImmutableArray<string> arguments)
    {
        return arguments.SequenceEqual(
            [
                "publish",
                @"src\TeamTool.Desktop\TeamTool.Desktop.csproj",
                "--configuration",
                "Release",
                "--no-restore",
                "--property:PublishProfile=WindowsSmoke",
            ],
            StringComparer.Ordinal);
    }
}

internal abstract class ProcessExecutionHandlerBase : IExecutionHandler
{
    private static readonly ImmutableHashSet<ExecutableTool> _executionTools =
        ImmutableHashSet.Create(
            ExecutableTool.DotNet,
            ExecutableTool.Npm,
            ExecutableTool.Pnpm,
            ExecutableTool.Yarn,
            ExecutableTool.Bun);
    private static readonly UTF8Encoding _strictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private readonly IProcessRunner _runner;

    protected ProcessExecutionHandlerBase(
        string id,
        bool handlesValidator,
        ExecutionResumeBehavior resumeBehavior,
        IProcessRunner runner)
    {
        Id = id;
        HandlesValidator = handlesValidator;
        ResumeBehavior = resumeBehavior;
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public string Id { get; }

    public ExecutionResumeBehavior ResumeBehavior { get; }

    private bool HandlesValidator { get; }

    protected virtual string CommandFailureCode => "DF-EXEC-001";

    protected abstract ImmutableHashSet<string> RequiredInputNames { get; }

    public Task<ExecutionHandlerResult> PrepareAsync(
        ExecutionHandlerRequest request,
        CancellationToken cancellationToken) => RunGuardedAsync(
            request,
            ExecutionPhase.Prepare,
            context => Task.FromResult(Success(ExecutionPhase.Prepare, null, null)),
            cancellationToken);

    public Task<ExecutionHandlerResult> CheckPreconditionsAsync(
        ExecutionHandlerRequest request,
        CancellationToken cancellationToken) => RunGuardedAsync(
            request,
            ExecutionPhase.Precondition,
            context => CheckDirectoryAsync(
                context,
                ExecutionPhase.Precondition,
                cancellationToken),
            cancellationToken);

    public Task<ExecutionHandlerResult> ExecuteAsync(
        ExecutionHandlerRequest request,
        IProgress<ExecutionProgressLine>? progress,
        CancellationToken cancellationToken) => RunGuardedAsync(
            request,
            ExecutionPhase.Execute,
            context => ExecuteCommandAsync(
                context,
                ExecutionPhase.Execute,
                progress,
                cancellationToken),
            cancellationToken);

    public Task<ExecutionHandlerResult> CheckPostconditionsAsync(
        ExecutionHandlerRequest request,
        CancellationToken cancellationToken) => RunGuardedAsync(
            request,
            ExecutionPhase.Postcondition,
            context => ResumeBehavior == ExecutionResumeBehavior.RevalidatePostcondition
                ? ExecuteCommandAsync(
                    context,
                    ExecutionPhase.Postcondition,
                    progress: null,
                    cancellationToken)
                : CheckDirectoryAsync(
                    context,
                    ExecutionPhase.Postcondition,
                    cancellationToken),
            cancellationToken);

    public Task<ExecutionHandlerResult> CleanupForRetryAsync(
        ExecutionHandlerRequest request,
        CancellationToken cancellationToken)
    {
        if (ResumeBehavior == ExecutionResumeBehavior.ReplayFromFreshStaging)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Failure(
                ExecutionPhase.Prepare,
                "DF-EXEC-003",
                "The process step requires replay from a newly owned staging workspace.",
                isRetryable: false,
                null,
                null));
        }

        return RunGuardedAsync(
            request,
            ExecutionPhase.Prepare,
            context => Task.FromResult(Success(ExecutionPhase.Prepare, null, null)),
            cancellationToken);
    }

    protected abstract CommandSpec CreateCommand(ProcessHandlerContext context);

    private async Task<ExecutionHandlerResult> CheckDirectoryAsync(
        ProcessHandlerContext context,
        ExecutionPhase phase,
        CancellationToken cancellationToken)
    {
        var command = CreateCommand(context);
        if (!command.UsesWorkspaceRoot
            && !await context.Payload.DirectoryExistsAsync(
                command.WorkingDirectory!,
                cancellationToken).ConfigureAwait(false))
        {
            throw new ProcessHandlerInputException();
        }

        await _runner.CheckPreconditionsAsync(command, cancellationToken).ConfigureAwait(false);

        return Success(phase, null, null);
    }

    private async Task<ExecutionHandlerResult> ExecuteCommandAsync(
        ProcessHandlerContext context,
        ExecutionPhase phase,
        IProgress<ExecutionProgressLine>? progress,
        CancellationToken cancellationToken)
    {
        var command = CreateCommand(context);
        var adapter = progress is null
            ? null
            : new ProcessProgressAdapter(context.Request.ItemId, progress);
        var result = await _runner.RunAsync(command, adapter, cancellationToken).ConfigureAwait(false);
        if (result.TerminationReason == ProcessTerminationReason.Cancelled)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        var digest = Digest(result);
        if (result.TerminationReason == ProcessTerminationReason.TimedOut)
        {
            return Failure(
                phase,
                "DF-EXEC-002",
                "The project command exceeded its bounded timeout.",
                isRetryable: true,
                null,
                digest);
        }

        var exitCode = phase == ExecutionPhase.Execute ? result.ExitCode : null;
        return command.AllowedExitCodes.Contains(result.ExitCode!.Value)
            ? Success(phase, exitCode, digest)
            : Failure(
                phase,
                CommandFailureCode,
                "The project command did not satisfy its allowed exit-code policy.",
                isRetryable: false,
                exitCode,
                digest);
    }

    protected static CommandSpec BuildCommand(
        ProcessHandlerContext context,
        string executableName,
        ImmutableArray<string> arguments,
        ImmutableArray<int> allowedExitCodes)
    {
        var executable = ExecutableIdentity.Create(executableName);
        if (!executable.IsValid || !_executionTools.Contains(executable.Value.Tool))
        {
            throw new ProcessHandlerInputException();
        }

        var workingDirectory = Value(context, "workingDirectory");
        if (workingDirectory.Kind != PlanValueKind.Text)
        {
            throw new ProcessHandlerInputException();
        }

        var result = workingDirectory.StringValue == "."
            ? CommandSpec.CreateAtWorkspaceRoot(
                executable.Value,
                arguments,
                context.Payload,
                [],
                context.Request.Timeout,
                allowedExitCodes,
                [])
            : CommandSpec.Create(
                executable.Value,
                arguments,
                context.Payload,
                GuardedPath(workingDirectory.StringValue),
                [],
                context.Request.Timeout,
                allowedExitCodes,
                []);
        return result.IsValid ? result.Value : throw new ProcessHandlerInputException();
    }

    protected static string Identifier(ProcessHandlerContext context, string name)
    {
        var value = Value(context, name);
        return value.Kind == PlanValueKind.Text
            && value.StringValue == value.StringValue!.Trim()
            ? value.StringValue
            : throw new ProcessHandlerInputException();
    }

    protected static ImmutableArray<string> Arguments(ProcessHandlerContext context)
    {
        var value = Value(context, "arguments");
        return value.Kind == PlanValueKind.Sequence
            && value.ArrayValue.All(item => item.Kind == PlanValueKind.Text)
            ? [.. value.ArrayValue.Select(item => item.StringValue!)]
            : throw new ProcessHandlerInputException();
    }

    protected static ImmutableArray<int> ExitCodes(ProcessHandlerContext context)
    {
        var value = Value(context, "allowedExitCodes");
        if (value.Kind != PlanValueKind.Sequence
            || value.ArrayValue.IsEmpty
            || value.ArrayValue.Any(item => item.Kind != PlanValueKind.WholeNumber
                || item.IntegerValue is < int.MinValue or > int.MaxValue))
        {
            throw new ProcessHandlerInputException();
        }

        return [.. value.ArrayValue.Select(item => checked((int)item.IntegerValue))];
    }

    protected static PlanValue Value(ProcessHandlerContext context, string name) =>
        context.Inputs.TryGetValue(name, out var value)
            ? value
            : throw new ProcessHandlerInputException();

    private static WorkspaceRelativePath GuardedPath(string? value)
    {
        var path = WorkspaceRelativePath.Create(value);
        return path.IsValid
            && !path.Value.Value.Split('\\').Any(segment => segment.Equals(
                ".env",
                StringComparison.OrdinalIgnoreCase))
                ? path.Value
                : throw new ProcessHandlerInputException();
    }

    private async Task<ExecutionHandlerResult> RunGuardedAsync(
        ExecutionHandlerRequest request,
        ExecutionPhase phase,
        Func<ProcessHandlerContext, Task<ExecutionHandlerResult>> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var context = CreateContext(request, cancellationToken);
            _ = CreateCommand(context);
            var result = await action(context).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ProcessHandlerInputException
            or InfrastructureOperationException
            or IOException
            or ArgumentException
            or OverflowException)
        {
            return Failure(
                phase,
                "DF-EXEC-001",
                "The trusted project command could not be executed safely.",
                isRetryable: false,
                null,
                null);
        }
    }

    private ProcessHandlerContext CreateContext(
        ExecutionHandlerRequest request,
        CancellationToken cancellationToken)
    {
        if (!StringComparer.Ordinal.Equals(request.HandlerId, Id)
            || request.IsValidator != HandlesValidator)
        {
            throw new ProcessHandlerInputException();
        }

        var runtime = RuntimePlanValueContext.Create(
            request.RunId,
            request.Staging.PayloadWorkspace.Root,
            null,
            RuntimeValueAvailability.PreFinalization,
            request.BlueprintPackage.Blueprint.Manifest.Trust);
        if (!runtime.IsValid)
        {
            throw new ProcessHandlerInputException();
        }

        var inputs = RuntimePlanValueMaterializer.Materialize(
            request.Inputs.Select(item =>
                KeyValuePair.Create<string, PlanValue?>(item.Key, item.Value)),
            runtime.Value,
            cancellationToken);
        if (!inputs.IsValid || !RequiredInputNames.SetEquals(inputs.Value.Keys))
        {
            throw new ProcessHandlerInputException();
        }

        return new ProcessHandlerContext(request, inputs.Value);
    }

    private static ExecutionHandlerResult Success(
        ExecutionPhase phase,
        int? exitCode,
        string? digest) => ExecutionHandlerResult.Create(
            phase,
            ExecutionHandlerOutcome.Succeeded,
            exitCode,
            digest,
            null,
            []).Value;

    private static ExecutionHandlerResult Failure(
        ExecutionPhase phase,
        string code,
        string summary,
        bool isRetryable,
        int? exitCode,
        string? digest)
    {
        var detail = RedactedText.FromTrustedRedaction(
            "A trusted executable, guarded working directory, bounded result, or command policy failed.");
        var error = DevForgeError.Create(
            code,
            summary,
            detail.Value,
            "execution-handler",
            null,
            isRetryable,
            ["Review the trusted tool preflight and the blueprint command policy."],
            []);
        return ExecutionHandlerResult.Create(
            phase,
            ExecutionHandlerOutcome.Failed,
            exitCode,
            digest,
            error.Value,
            []).Value;
    }

    private static string Digest(ProcessResult result)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> number = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(number, (int)result.TerminationReason);
        hash.AppendData(number);
        BinaryPrimitives.WriteInt32LittleEndian(number, result.ExitCode ?? int.MinValue);
        hash.AppendData(number);
        hash.AppendData([result.IsOutputTruncated ? (byte)1 : (byte)0]);
        foreach (var line in result.RetainedLines)
        {
            BinaryPrimitives.WriteInt32LittleEndian(number, (int)line.Channel);
            hash.AppendData(number);
            var text = _strictUtf8.GetBytes(line.Text.Value);
            BinaryPrimitives.WriteInt32LittleEndian(number, text.Length);
            hash.AppendData(number);
            hash.AppendData(text);
        }

        return $"sha256:{Convert.ToHexStringLower(hash.GetHashAndReset())}";
    }

    protected sealed record ProcessHandlerContext(
        ExecutionHandlerRequest Request,
        ImmutableDictionary<string, PlanValue> Inputs)
    {
        public IWorkspaceFileSystem Payload => Request.Staging.PayloadWorkspace;
    }

    protected sealed class ProcessHandlerInputException : Exception;

    private sealed class ProcessProgressAdapter(
        string itemId,
        IProgress<ExecutionProgressLine> progress) : IProgress<ProcessOutputLine>
    {
        public void Report(ProcessOutputLine value)
        {
            var line = ExecutionProgressLine.Create(itemId, value.Text);
            if (!line.IsValid)
            {
                return;
            }

            try
            {
                progress.Report(line.Value);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException
                and not StackOverflowException
                and not AccessViolationException)
            {
            }
        }
    }
}
