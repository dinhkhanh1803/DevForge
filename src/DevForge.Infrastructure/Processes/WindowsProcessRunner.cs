using System.ComponentModel;
using System.Diagnostics;
using DevForge.Application.Contracts;
using DevForge.Infrastructure.FileSystem;

namespace DevForge.Infrastructure.Processes;

public sealed class WindowsProcessRunner : IProcessRunner
{
    private readonly ITrustedExecutableResolver _executableResolver;

    public WindowsProcessRunner()
        : this(new TrustedExecutableResolver())
    {
    }

    internal WindowsProcessRunner(ITrustedExecutableResolver executableResolver)
    {
        _executableResolver = executableResolver
            ?? throw new ArgumentNullException(nameof(executableResolver));
    }

    public Task CheckPreconditionsAsync(
        CommandSpec command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        _ = ResolveExecutable(command.Executable);
        _ = ResolveWorkingDirectory(command);
        return Task.CompletedTask;
    }

    public async Task<ProcessResult> RunAsync(
        CommandSpec command,
        IProgress<ProcessOutputLine>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var output = new BoundedProcessOutput(command.RedactionNeedles, progress);
        if (cancellationToken.IsCancellationRequested)
        {
            return output.CreateResult(ProcessTerminationReason.Cancelled, null);
        }

        var executable = ResolveExecutable(command.Executable);
        var workingDirectory = ResolveWorkingDirectory(command);
        using var process = new Process
        {
            StartInfo = CreateStartInfo(command, executable, workingDirectory),
        };

        StartProcess(process);
        var standardOutputTask = BoundedTextLinePump.PumpAsync(
            process.StandardOutput,
            ProcessOutputChannel.StandardOutput,
            output);
        var standardErrorTask = BoundedTextLinePump.PumpAsync(
            process.StandardError,
            ProcessOutputChannel.StandardError,
            output);
        var exitTask = process.WaitForExitAsync(CancellationToken.None);
        using var timeoutSource = new CancellationTokenSource();
        var timeoutTask = Task.Delay(command.Timeout, timeoutSource.Token);
        var cancellationTask = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        var completed = await Task.WhenAny(exitTask, timeoutTask, cancellationTask)
            .ConfigureAwait(false);
        if (completed == exitTask || process.HasExited)
        {
            timeoutSource.Cancel();
            await CompleteOutputAsync(exitTask, standardOutputTask, standardErrorTask)
                .ConfigureAwait(false);
            return output.CreateResult(ProcessTerminationReason.Exited, process.ExitCode);
        }

        var terminationReason = cancellationToken.IsCancellationRequested
            ? ProcessTerminationReason.Cancelled
            : ProcessTerminationReason.TimedOut;
        TerminateProcessTree(process);
        timeoutSource.Cancel();
        await CompleteOutputAsync(exitTask, standardOutputTask, standardErrorTask)
            .ConfigureAwait(false);
        return output.CreateResult(terminationReason, null);
    }

    private static ProcessStartInfo CreateStartInfo(
        CommandSpec command,
        TrustedExecutableLaunch executable,
        string workingDirectory)
    {
        var startInfo = new ProcessStartInfo(executable.ExecutablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory,
        };
        foreach (var prefixArgument in executable.PrefixArguments)
        {
            startInfo.ArgumentList.Add(prefixArgument);
        }

        foreach (var argument in command.ArgumentList)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment.Clear();
        foreach (var variable in command.EnvironmentVariables)
        {
            startInfo.Environment[variable.Key] = variable.Value.RevealForProcessStart();
        }

        return startInfo;
    }

    private static string ResolveWorkingDirectory(CommandSpec command)
    {
        try
        {
            var guard = WorkspacePathGuard.Open(command.Workspace.Root);
            var workingDirectory = command.UsesWorkspaceRoot
                ? guard.RootPath
                : guard.Resolve(command.WorkingDirectory!);
            guard.VerifyExisting(workingDirectory);
            if (!Directory.Exists(workingDirectory))
            {
                throw new IOException();
            }

            return workingDirectory;
        }
        catch (WorkspaceContainmentException)
        {
            throw new InfrastructureOperationException(
                "DF-PROC-002",
                "The process working directory is not safely contained.");
        }
        catch (Exception exception) when (WindowsWorkspaceFileSystem.IsExpectedFileSystemFailure(exception))
        {
            throw new InfrastructureOperationException(
                "DF-PROC-002",
                "The process working directory could not be prepared.");
        }
    }

    private TrustedExecutableLaunch ResolveExecutable(ExecutableIdentity executable)
    {
        try
        {
            var resolved = _executableResolver.Resolve(executable);
            if (string.IsNullOrWhiteSpace(resolved.ExecutablePath)
                || !Path.IsPathFullyQualified(resolved.ExecutablePath)
                || resolved.ExecutablePath.StartsWith(@"\\", StringComparison.Ordinal)
                || !File.Exists(resolved.ExecutablePath))
            {
                throw new InfrastructureOperationException(
                    "DF-PROC-001",
                    "The trusted executable could not be resolved.");
            }

            var attributes = File.GetAttributes(resolved.ExecutablePath);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                throw new InfrastructureOperationException(
                    "DF-PROC-001",
                    "The trusted executable could not be resolved.");
            }

            return new TrustedExecutableLaunch(
                Path.GetFullPath(resolved.ExecutablePath),
                resolved.PrefixArguments);
        }
        catch (InfrastructureOperationException)
        {
            throw;
        }
        catch (Exception exception) when (WindowsWorkspaceFileSystem.IsExpectedFileSystemFailure(exception))
        {
            throw new InfrastructureOperationException(
                "DF-PROC-001",
                "The trusted executable could not be resolved.");
        }
    }

    private static void StartProcess(Process process)
    {
        try
        {
            if (!process.Start())
            {
                throw new InfrastructureOperationException(
                    "DF-PROC-001",
                    "The trusted process could not be started.");
            }
        }
        catch (InfrastructureOperationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            throw new InfrastructureOperationException(
                "DF-PROC-001",
                "The trusted process could not be started.");
        }
    }

    private static void TerminateProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
        }
        catch (Win32Exception)
        {
            throw new InfrastructureOperationException(
                "DF-PROC-003",
                "The owned process tree could not be terminated.");
        }
    }

    private static async Task CompleteOutputAsync(
        Task exitTask,
        Task standardOutputTask,
        Task standardErrorTask)
    {
        await exitTask.ConfigureAwait(false);
        await Task.WhenAll(standardOutputTask, standardErrorTask).ConfigureAwait(false);
    }
}
