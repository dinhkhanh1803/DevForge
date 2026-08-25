using System.Collections.Immutable;
using System.Runtime.InteropServices;
using DevForge.Application.Contracts;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Execution;
using DevForge.Domain.Privacy;
using DevForge.Infrastructure.Execution;
using DevForge.Infrastructure.FileSystem;
using DevForge.Infrastructure.Processes;

namespace DevForge.IntegrationTests.Infrastructure.Execution;

public sealed class ProcessExecutionHandlerTests
{
    [Fact]
    public async Task RunProcessBuildsASeparatedGuardedCommandAndRedactedEvidence()
    {
        await using var fixture = await ProcessFixture.CreateAsync();
        var runner = new RecordingRunner(Exited(0, "build complete"));
        var request = fixture.StepRequest(
            "run-process",
            ("executable", Text("dotnet")),
            ("arguments", Sequence(Text("build"), Text("--no-restore"))),
            ("workingDirectory", Text("src")),
            ("allowedExitCodes", Sequence(PlanValue.FromInteger(0))));
        var progressLines = new List<ExecutionProgressLine>();

        var result = await new RunProcessExecutionHandler(runner).ExecuteAsync(
            request,
            new CaptureProgress<ExecutionProgressLine>(progressLines.Add),
            CancellationToken.None);

        Assert.Equal(ExecutionHandlerOutcome.Succeeded, result.Outcome);
        Assert.Equal(0, result.ExitCode);
        Assert.StartsWith("sha256:", result.OutputDigest, StringComparison.Ordinal);
        Assert.Equal(ExecutableTool.DotNet, runner.Command?.Executable.Tool);
        Assert.Equal(["build", "--no-restore"], runner.Command?.ArgumentList);
        Assert.Equal("src", runner.Command?.WorkingDirectory?.Value);
        Assert.False(runner.Command?.UsesWorkspaceRoot);
        Assert.Empty(runner.Command!.EnvironmentVariables);
        Assert.Empty(runner.Command.RedactionNeedles);
        Assert.Contains(progressLines, line =>
            line.StepId == request.ItemId && line.Text.Value == "build complete");
    }

    [Fact]
    public async Task WorkspaceRootSentinelNeverBecomesAProcessArgumentOrRawPath()
    {
        await using var fixture = await ProcessFixture.CreateAsync();
        var runner = new RecordingRunner(Exited(0));
        var request = fixture.StepRequest(
            "run-process",
            ("executable", Text("dotnet")),
            ("arguments", Sequence(Text("restore"))),
            ("workingDirectory", Text(".")),
            ("allowedExitCodes", Sequence(PlanValue.FromInteger(0))));

        var result = await new RunProcessExecutionHandler(runner).ExecuteAsync(
            request,
            null,
            CancellationToken.None);

        Assert.Equal(ExecutionHandlerOutcome.Succeeded, result.Outcome);
        Assert.True(runner.Command?.UsesWorkspaceRoot);
        Assert.Null(runner.Command?.WorkingDirectory);
        Assert.DoesNotContain(".", runner.Command!.ArgumentList);
    }

    [Fact]
    public async Task DisallowedExitAndTimeoutHaveExplicitRetryClassification()
    {
        await using var fixture = await ProcessFixture.CreateAsync();
        var request = fixture.StepRequest(
            "run-process",
            ("executable", Text("dotnet")),
            ("arguments", Sequence(Text("build"))),
            ("workingDirectory", Text(".")),
            ("allowedExitCodes", Sequence(PlanValue.FromInteger(0))));

        var disallowed = await new RunProcessExecutionHandler(
            new RecordingRunner(Exited(7))).ExecuteAsync(request, null, default);
        var timedOut = await new RunProcessExecutionHandler(
            new RecordingRunner(ProcessResult.Create(
                ProcessTerminationReason.TimedOut,
                null,
                []).Value)).ExecuteAsync(request, null, default);

        Assert.Equal("DF-EXEC-001", disallowed.Error?.Code);
        Assert.False(disallowed.Error?.IsRetryable);
        Assert.Equal(7, disallowed.ExitCode);
        Assert.Equal("DF-EXEC-002", timedOut.Error?.Code);
        Assert.True(timedOut.Error?.IsRetryable);
    }

    [Fact]
    public async Task CancellationPropagatesAfterTheRunnerReportsTreeTermination()
    {
        await using var fixture = await ProcessFixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        var runner = new CancellingRunner(cancellation);
        var request = fixture.StepRequest(
            "run-process",
            ("executable", Text("dotnet")),
            ("arguments", Sequence(Text("build"))),
            ("workingDirectory", Text(".")),
            ("allowedExitCodes", Sequence(PlanValue.FromInteger(0))));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new RunProcessExecutionHandler(runner).ExecuteAsync(
                request,
                null,
                cancellation.Token));

        Assert.True(runner.ReturnedAfterCancellation);
    }

    [Fact]
    public async Task PackageInstallUsesOnlyTheClosedPackageManagerSetAndExitZero()
    {
        await using var fixture = await ProcessFixture.CreateAsync();
        var runner = new RecordingRunner(Exited(0));
        var valid = fixture.StepRequest(
            "package-install",
            ("packageManager", Text("dotnet")),
            ("arguments", Sequence(Text("restore"))),
            ("workingDirectory", Text(".")));
        var invalid = fixture.StepRequest(
            "package-install",
            ("packageManager", Text("git")),
            ("arguments", Sequence(Text("status"))),
            ("workingDirectory", Text(".")));

        var success = await new PackageInstallExecutionHandler(runner).ExecuteAsync(
            valid,
            null,
            default);
        var failure = await new PackageInstallExecutionHandler(runner).ExecuteAsync(
            invalid,
            null,
            default);

        Assert.Equal(ExecutionHandlerOutcome.Succeeded, success.Outcome);
        Assert.Equal(ExecutableTool.DotNet, runner.Commands[0].Executable.Tool);
        Assert.Equal([0], runner.Commands[0].AllowedExitCodes);
        Assert.Equal(ExecutionHandlerOutcome.Failed, failure.Outcome);
        Assert.False(failure.Error?.IsRetryable);
        Assert.Single(runner.Commands);
        Assert.Equal(
            ExecutionResumeBehavior.ReplayFromFreshStaging,
            new PackageInstallExecutionHandler(runner).ResumeBehavior);
    }

    [Theory]
    [InlineData("git")]
    [InlineData("gh")]
    [InlineData("code")]
    [InlineData("devenv")]
    public async Task RunProcessCannotBypassDeferredIntegrationOrIdeBoundaries(string executable)
    {
        await using var fixture = await ProcessFixture.CreateAsync();
        var runner = new RecordingRunner(Exited(0));
        var request = fixture.StepRequest(
            "run-process",
            ("executable", Text(executable)),
            ("arguments", Sequence(Text("status"))),
            ("workingDirectory", Text(".")),
            ("allowedExitCodes", Sequence(PlanValue.FromInteger(0))));

        var result = await new RunProcessExecutionHandler(runner).ExecuteAsync(
            request,
            null,
            default);

        Assert.Equal(ExecutionHandlerOutcome.Failed, result.Outcome);
        Assert.False(result.Error?.IsRetryable);
        Assert.Empty(runner.Commands);
    }

    [Fact]
    public async Task ValidateCommandAcceptsAPlanOwnedValidatorWithoutTreatingRequiredAsAnArgument()
    {
        await using var fixture = await ProcessFixture.CreateAsync();
        var runner = new RecordingRunner(Exited(0));
        var request = fixture.ValidatorRequest(
            required: false,
            ("executable", Text("dotnet")),
            ("arguments", Sequence(Text("test"))),
            ("workingDirectory", Text(".")),
            ("allowedExitCodes", Sequence(PlanValue.FromInteger(0))),
            ("required", PlanValue.FromBoolean(false)));

        var result = await new ValidateCommandExecutionHandler(runner).ExecuteAsync(
            request,
            null,
            default);
        var postcondition = await new ValidateCommandExecutionHandler(runner).CheckPostconditionsAsync(
            request,
            default);

        Assert.Equal(ExecutionHandlerOutcome.Succeeded, result.Outcome);
        Assert.True(request.IsValidator);
        Assert.False(request.Required);
        Assert.Equal(["test"], runner.Command?.ArgumentList);
        Assert.Equal(ExecutionHandlerOutcome.Succeeded, postcondition.Outcome);
        Assert.Equal(2, runner.Commands.Count);
        Assert.Equal(
            ExecutionResumeBehavior.RevalidatePostcondition,
            new ValidateCommandExecutionHandler(runner).ResumeBehavior);
    }

    [Fact]
    public async Task ValidateCommandAcceptsOnlyTheFixedWpfPublishSmokeShape()
    {
        await using var fixture = await ProcessFixture.CreateAsync();
        var runner = new RecordingRunner(Exited(0));
        var request = fixture.ValidatorRequest(
            required: true,
            ("executable", Text("dotnet")),
            ("arguments", Sequence(
                Text("publish"),
                Text("src\\TeamTool.Desktop\\TeamTool.Desktop.csproj"),
                Text("--configuration"),
                Text("Release"),
                Text("--no-restore"),
                Text("--property:PublishProfile=WindowsSmoke"))),
            ("workingDirectory", Text(".")),
            ("allowedExitCodes", Sequence(PlanValue.FromInteger(0))),
            ("required", PlanValue.FromBoolean(true)));

        var result = await new ValidateCommandExecutionHandler(runner).ExecuteAsync(
            request,
            null,
            CancellationToken.None);

        Assert.Equal(ExecutionHandlerOutcome.Succeeded, result.Outcome);
        Assert.Equal(
            [
                "publish",
                "src\\TeamTool.Desktop\\TeamTool.Desktop.csproj",
                "--configuration",
                "Release",
                "--no-restore",
                "--property:PublishProfile=WindowsSmoke",
            ],
            Assert.Single(runner.Commands).ArgumentList.ToArray());
    }

    [Theory]
    [InlineData("src\\Other.Desktop\\Other.Desktop.csproj", "--property:PublishProfile=WindowsSmoke")]
    [InlineData("src\\TeamTool.Desktop\\TeamTool.Desktop.csproj", "--property:PublishProfile=Unexpected")]
    public async Task ValidateCommandRejectsMutatedWpfPublishSmokeShapes(
        string projectPath,
        string publishProfile)
    {
        await using var fixture = await ProcessFixture.CreateAsync();
        var runner = new RecordingRunner(Exited(0));
        var request = fixture.ValidatorRequest(
            required: true,
            ("executable", Text("dotnet")),
            ("arguments", Sequence(
                Text("publish"),
                Text(projectPath),
                Text("--configuration"),
                Text("Release"),
                Text("--no-restore"),
                Text(publishProfile))),
            ("workingDirectory", Text(".")),
            ("allowedExitCodes", Sequence(PlanValue.FromInteger(0))),
            ("required", PlanValue.FromBoolean(true)));

        var result = await new ValidateCommandExecutionHandler(runner).ExecuteAsync(
            request,
            null,
            CancellationToken.None);

        Assert.Equal(ExecutionHandlerOutcome.Failed, result.Outcome);
        Assert.Empty(runner.Commands);
    }

    [Fact]
    public async Task RunProcessCannotUseTheWpfPublishValidatorVocabulary()
    {
        await using var fixture = await ProcessFixture.CreateAsync();
        var runner = new RecordingRunner(Exited(0));
        var request = fixture.StepRequest(
            "run-process",
            ("executable", Text("dotnet")),
            ("arguments", Sequence(
                Text("publish"),
                Text("src\\TeamTool.Desktop\\TeamTool.Desktop.csproj"),
                Text("--configuration"),
                Text("Release"),
                Text("--no-restore"),
                Text("--property:PublishProfile=WindowsSmoke"))),
            ("workingDirectory", Text(".")),
            ("allowedExitCodes", Sequence(PlanValue.FromInteger(0))));

        var result = await new RunProcessExecutionHandler(runner).ExecuteAsync(
            request,
            null,
            CancellationToken.None);

        Assert.Equal(ExecutionHandlerOutcome.Failed, result.Outcome);
        Assert.Empty(runner.Commands);
    }

    [Fact]
    public async Task HandlerKindCannotCrossTheGenerationValidatorBoundary()
    {
        await using var fixture = await ProcessFixture.CreateAsync();
        var runner = new RecordingRunner(Exited(0));
        var step = fixture.StepRequest(
            "validate-command",
            ("executable", Text("dotnet")),
            ("arguments", Sequence(Text("test"))),
            ("workingDirectory", Text(".")),
            ("allowedExitCodes", Sequence(PlanValue.FromInteger(0))),
            ("required", PlanValue.FromBoolean(true)));
        var validator = fixture.ValidatorRequest(
            "run-process",
            required: true,
            ("executable", Text("dotnet")),
            ("arguments", Sequence(Text("build"))),
            ("workingDirectory", Text(".")),
            ("allowedExitCodes", Sequence(PlanValue.FromInteger(0))));

        var stepResult = await new ValidateCommandExecutionHandler(runner).ExecuteAsync(
            step,
            null,
            default);
        var validatorResult = await new RunProcessExecutionHandler(runner).ExecuteAsync(
            validator,
            null,
            default);

        Assert.Equal(ExecutionHandlerOutcome.Failed, stepResult.Outcome);
        Assert.Equal(ExecutionHandlerOutcome.Failed, validatorResult.Outcome);
        Assert.Empty(runner.Commands);
    }

    [Fact]
    public async Task PreconditionsProbeTheTrustedRunnerWithoutStartingTheCommand()
    {
        await using var fixture = await ProcessFixture.CreateAsync();
        var runner = new RecordingRunner(Exited(0));
        var request = fixture.StepRequest(
            "run-process",
            ("executable", Text("dotnet")),
            ("arguments", Sequence(Text("build"))),
            ("workingDirectory", Text("src")),
            ("allowedExitCodes", Sequence(PlanValue.FromInteger(0))));

        var result = await new RunProcessExecutionHandler(runner).CheckPreconditionsAsync(
            request,
            default);

        Assert.Equal(ExecutionHandlerOutcome.Succeeded, result.Outcome);
        Assert.Equal(1, runner.PreflightCount);
        Assert.Empty(runner.Commands);
        Assert.Equal(
            ExecutionResumeBehavior.ReplayFromFreshStaging,
            new RunProcessExecutionHandler(runner).ResumeBehavior);
    }

    [Fact]
    public async Task MutatingProcessRetryRefusesDirtyStagingAndRequiresWholePlanReplay()
    {
        await using var fixture = await ProcessFixture.CreateAsync();
        var runner = new RecordingRunner(Exited(0));
        var request = fixture.StepRequest(
            "run-process",
            ("executable", Text("dotnet")),
            ("arguments", Sequence(Text("restore"))),
            ("workingDirectory", Text(".")),
            ("allowedExitCodes", Sequence(PlanValue.FromInteger(0))));

        var result = await new RunProcessExecutionHandler(runner).CleanupForRetryAsync(
            request,
            default);

        Assert.Equal(ExecutionHandlerOutcome.Failed, result.Outcome);
        Assert.Equal("DF-EXEC-003", result.Error?.Code);
        Assert.False(result.Error?.IsRetryable);
        Assert.Empty(runner.Commands);
    }

    [Theory]
    [InlineData("node", "app.js")]
    [InlineData("npx", "package-name")]
    [InlineData("dotnet", "run")]
    [InlineData("dotnet", "exec")]
    [InlineData("dotnet", "tool")]
    public async Task RunProcessRejectsGeneralPurposeRuntimeAndScriptModes(
        string executable,
        string firstArgument)
    {
        await using var fixture = await ProcessFixture.CreateAsync();
        var runner = new RecordingRunner(Exited(0));
        var request = fixture.StepRequest(
            "run-process",
            ("executable", Text(executable)),
            ("arguments", Sequence(Text(firstArgument))),
            ("workingDirectory", Text(".")),
            ("allowedExitCodes", Sequence(PlanValue.FromInteger(0))));

        var result = await new RunProcessExecutionHandler(runner).ExecuteAsync(
            request,
            null,
            default);

        Assert.Equal(ExecutionHandlerOutcome.Failed, result.Outcome);
        Assert.Empty(runner.Commands);
    }

    [Theory]
    [InlineData("npx", "install", "--ignore-scripts")]
    [InlineData("npm", "exec", "--ignore-scripts")]
    [InlineData("npm", "install", "package-name")]
    [InlineData("dotnet", "tool", "restore")]
    public async Task PackageInstallRejectsExecutableOrLifecycleScriptModes(
        string manager,
        string verb,
        string argument)
    {
        await using var fixture = await ProcessFixture.CreateAsync();
        var runner = new RecordingRunner(Exited(0));
        var request = fixture.StepRequest(
            "package-install",
            ("packageManager", Text(manager)),
            ("arguments", Sequence(Text(verb), Text(argument))),
            ("workingDirectory", Text(".")));

        var result = await new PackageInstallExecutionHandler(runner).ExecuteAsync(
            request,
            null,
            default);

        Assert.Equal(ExecutionHandlerOutcome.Failed, result.Outcome);
        Assert.Empty(runner.Commands);
    }

    [Fact]
    public async Task PackageInstallAllowsNodeDependencyRestoreOnlyWithScriptsDisabled()
    {
        await using var fixture = await ProcessFixture.CreateAsync();
        var runner = new RecordingRunner(Exited(0));
        var request = fixture.StepRequest(
            "package-install",
            ("packageManager", Text("npm")),
            ("arguments", Sequence(Text("ci"), Text("--ignore-scripts"))),
            ("workingDirectory", Text(".")));

        var result = await new PackageInstallExecutionHandler(runner).ExecuteAsync(
            request,
            null,
            default);

        Assert.Equal(ExecutionHandlerOutcome.Succeeded, result.Outcome);
        Assert.Equal(ExecutableTool.Npm, Assert.Single(runner.Commands).Executable.Tool);
    }

    [Fact]
    public async Task PackageInstallRejectsLifecycleScriptFlagOverrides()
    {
        await using var fixture = await ProcessFixture.CreateAsync();
        var runner = new RecordingRunner(Exited(0));
        var request = fixture.StepRequest(
            "package-install",
            ("packageManager", Text("npm")),
            ("arguments", Sequence(
                Text("install"),
                Text("--ignore-scripts"),
                Text("--no-ignore-scripts"))),
            ("workingDirectory", Text(".")));

        var result = await new PackageInstallExecutionHandler(runner).ExecuteAsync(
            request,
            null,
            default);

        Assert.Equal(ExecutionHandlerOutcome.Failed, result.Outcome);
        Assert.Empty(runner.Commands);
    }

    [Fact]
    public async Task MissingWorkingDirectoryFailsBeforeStartingTheProcess()
    {
        await using var fixture = await ProcessFixture.CreateAsync();
        var runner = new RecordingRunner(Exited(0));
        var request = fixture.StepRequest(
            "run-process",
            ("executable", Text("dotnet")),
            ("arguments", Sequence(Text("build"))),
            ("workingDirectory", Text("missing")),
            ("allowedExitCodes", Sequence(PlanValue.FromInteger(0))));

        var result = await new RunProcessExecutionHandler(runner).CheckPreconditionsAsync(
            request,
            default);
        var postcondition = await new RunProcessExecutionHandler(runner).CheckPostconditionsAsync(
            request,
            default);

        Assert.Equal(ExecutionHandlerOutcome.Failed, result.Outcome);
        Assert.False(result.Error?.IsRetryable);
        Assert.Equal(ExecutionHandlerOutcome.Failed, postcondition.Outcome);
        Assert.Empty(runner.Commands);
    }

    [Fact]
    public async Task EnvironmentWorkingDirectoryIsRejectedBeforeStartingTheProcess()
    {
        await using var fixture = await ProcessFixture.CreateAsync();
        var runner = new RecordingRunner(Exited(0));
        var request = fixture.StepRequest(
            "run-process",
            ("executable", Text("dotnet")),
            ("arguments", Sequence(Text("build"))),
            ("workingDirectory", Text("config\\.env")),
            ("allowedExitCodes", Sequence(PlanValue.FromInteger(0))));

        var result = await new RunProcessExecutionHandler(runner).ExecuteAsync(
            request,
            null,
            default);

        Assert.Equal(ExecutionHandlerOutcome.Failed, result.Outcome);
        Assert.Empty(runner.Commands);
    }

    [Fact]
    public async Task WorkspaceRootPostconditionFailsWhenTheOwnedPayloadRootIsGone()
    {
        await using var fixture = await ProcessFixture.CreateAsync();
        var request = fixture.StepRequest(
            "run-process",
            ("executable", Text("dotnet")),
            ("arguments", Sequence(Text("build"))),
            ("workingDirectory", Text(".")),
            ("allowedExitCodes", Sequence(PlanValue.FromInteger(0))));
        fixture.DeleteOwnedPayloadRoot();
        var runner = new WindowsProcessRunner(new FixedExecutableResolver(FindDotNetHost()));

        var result = await new RunProcessExecutionHandler(runner).CheckPostconditionsAsync(
            request,
            default);

        Assert.Equal(ExecutionHandlerOutcome.Failed, result.Outcome);
    }

    [Fact]
    public void ProcessHandlersUseOnlyTheTypedRunnerBoundary()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "DevForge.Infrastructure",
            "Execution",
            "ProcessExecutionHandlers.cs"));

        Assert.Contains("IProcessRunner", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.Start", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ProcessStartInfo", source, StringComparison.Ordinal);
        Assert.DoesNotContain("cmd /c", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("powershell", source, StringComparison.OrdinalIgnoreCase);
    }

    private static ProcessResult Exited(int exitCode, params string[] output)
    {
        var lines = output.Select(text => ProcessOutputLine.Create(
            ProcessOutputChannel.StandardOutput,
            RedactedText.FromTrustedRedaction(text).Value).Value);
        return ProcessResult.Create(ProcessTerminationReason.Exited, exitCode, lines).Value;
    }

    private static PlanValue Text(string value) => PlanValue.FromString(value).Value;

    private static PlanValue Sequence(params PlanValue[] values) =>
        PlanValue.FromArray(values).Value;

    private static string FindDotNetHost()
    {
        var configured = System.Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return configured;
        }

        var runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
        var candidate = Path.GetFullPath(Path.Combine(runtimeDirectory, "..", "..", "..", "dotnet.exe"));
        return File.Exists(candidate) ? candidate : throw new FileNotFoundException();
    }

    private static string FindRepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "DevForge.sln")))
        {
            root = root.Parent;
        }

        return root?.FullName ?? throw new DirectoryNotFoundException();
    }

    private sealed class FixedExecutableResolver(string path) : ITrustedExecutableResolver
    {
        public TrustedExecutableLaunch Resolve(ExecutableIdentity executable) => new(path, []);
    }

    private sealed class RecordingRunner(ProcessResult result) : IProcessRunner
    {
        public List<CommandSpec> Commands { get; } = [];

        public CommandSpec? Command => Commands.LastOrDefault();

        public int PreflightCount { get; private set; }

        public Task CheckPreconditionsAsync(
            CommandSpec command,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PreflightCount++;
            return Task.CompletedTask;
        }

        public Task<ProcessResult> RunAsync(
            CommandSpec command,
            IProgress<ProcessOutputLine>? progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add(command);
            foreach (var line in result.RetainedLines)
            {
                progress?.Report(line);
            }

            return Task.FromResult(result);
        }
    }

    private sealed class CancellingRunner(CancellationTokenSource cancellation) : IProcessRunner
    {
        public bool ReturnedAfterCancellation { get; private set; }

        public Task CheckPreconditionsAsync(
            CommandSpec command,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<ProcessResult> RunAsync(
            CommandSpec command,
            IProgress<ProcessOutputLine>? progress,
            CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            ReturnedAfterCancellation = cancellationToken.IsCancellationRequested;
            return Task.FromResult(ProcessResult.Create(
                ProcessTerminationReason.Cancelled,
                null,
                []).Value);
        }
    }

    private sealed class CaptureProgress<T>(Action<T> capture) : IProgress<T>
    {
        public void Report(T value) => capture(value);
    }

    private sealed class ProcessFixture : IAsyncDisposable
    {
        private ProcessFixture(
            string rootPath,
            IWorkspaceFileSystem payload,
            BlueprintExecutionPackage package)
        {
            RootPath = rootPath;
            Payload = payload;
            Package = package;
        }

        private string RootPath { get; }

        private IWorkspaceFileSystem Payload { get; }

        private BlueprintExecutionPackage Package { get; }

        public static async Task<ProcessFixture> CreateAsync()
        {
            var rootPath = Path.GetFullPath(Path.Combine(
                Path.GetTempPath(),
                "DevForge-M5-ProcessHandlers-" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(Path.Combine(rootPath, "src"));
            var payload = await new WindowsFileSystem().OpenWorkspaceAsync(
                WorkspaceRoot.Create(rootPath).Value,
                default);
            var packageWorkspace = VerifiedBlueprintWorkspace.Create(
                $"sha256:{new string('a', 64)}",
                ImmutableDictionary<string, ImmutableArray<byte>>.Empty,
                default);
            var manifest = BlueprintManifest.Create(
                new BlueprintManifestDraft(
                    "sample.blueprint",
                    "1.0.0",
                    ">=1.0.0 <2.0.0",
                    [],
                    [],
                    [],
                    [],
                    []),
                new BlueprintTrustAssignment(BlueprintTrust.BuiltIn)).Value;
            var fingerprint = BlueprintFingerprint.Create(
                "built-in",
                WorkspaceRelativePath.Create("sample.blueprint\\1.0.0").Value,
                BlueprintTrust.BuiltIn,
                $"sha256:{new string('a', 64)}").Value;
            var resolved = ResolvedBlueprint.Create(manifest, [], fingerprint).Value;
            var package = BlueprintExecutionPackage.Create(resolved, packageWorkspace).Value;
            return new ProcessFixture(rootPath, payload, package);
        }

        public ExecutionHandlerRequest StepRequest(
            string handler,
            params (string Key, PlanValue Value)[] inputs)
        {
            return StepRequest(handler, TimeSpan.FromSeconds(30), inputs);
        }

        public ExecutionHandlerRequest StepRequest(
            string handler,
            TimeSpan timeout,
            params (string Key, PlanValue Value)[] inputs)
        {
            var step = ExecutionStep.Create(
                "step",
                "Step",
                handler,
                inputs.Select(item => KeyValuePair.Create<string, PlanValue?>(item.Key, item.Value)),
                timeout,
                RetryPolicy.None).Value;
            var plan = ExecutionPlan.Create(
                $"sha256:{new string('b', 64)}",
                [step],
                [],
                []).Value;
            return ExecutionHandlerRequest.Create(
                "run-1",
                step,
                Staging(),
                Package,
                plan).Value;
        }

        public ExecutionHandlerRequest ValidatorRequest(
            bool required,
            params (string Key, PlanValue Value)[] inputs)
        {
            return ValidatorRequest("validate-command", required, inputs);
        }

        public ExecutionHandlerRequest ValidatorRequest(
            string handler,
            bool required,
            params (string Key, PlanValue Value)[] inputs)
        {
            var validator = ExecutionValidator.Create(
                "validator",
                handler,
                inputs.Select(item => KeyValuePair.Create<string, PlanValue?>(item.Key, item.Value)),
                TimeSpan.FromSeconds(30),
                required).Value;
            var plan = ExecutionPlan.Create(
                $"sha256:{new string('b', 64)}",
                [],
                [validator],
                []).Value;
            return ExecutionHandlerRequest.Create(
                "run-1",
                validator,
                Staging(),
                Package,
                plan).Value;
        }

        private StagingWorkspace Staging()
        {
            var descriptor = StagingDescriptor.Create(
                WorkspaceRelativePath.Create(".devforge-staging\\run-1").Value,
                WorkspaceRelativePath.Create(".devforge-staging\\run-1\\payload").Value,
                WorkspaceRelativePath.Create(".devforge-staging\\run-1\\ownership.json").Value,
                "marker-1").Value;
            return StagingWorkspace.Create(descriptor, Payload).Value;
        }

        public void DeleteOwnedPayloadRoot()
        {
            if (!Path.GetFileName(RootPath).StartsWith(
                    "DevForge-M5-ProcessHandlers-",
                    StringComparison.Ordinal)
                || !RootPath.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException();
            }

            Directory.Delete(RootPath, recursive: true);
        }

        public ValueTask DisposeAsync()
        {
            if (!Path.GetFileName(RootPath).StartsWith(
                    "DevForge-M5-ProcessHandlers-",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException();
            }

            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
