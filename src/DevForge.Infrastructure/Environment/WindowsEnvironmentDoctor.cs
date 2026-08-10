using System.Collections.Immutable;
using System.Text.RegularExpressions;
using DevForge.Application.Contracts;
using DevForge.Domain.Environment;
using DevForge.Domain.Privacy;

namespace DevForge.Infrastructure.Environment;

public sealed class WindowsEnvironmentDoctor : IEnvironmentDoctor
{
    private static readonly WorkspaceRelativePath _probeDirectory =
        WorkspaceRelativePath.Create("environment-probe").Value;

    private static readonly Regex _versionPattern = new(
        @"(?<![0-9])(?<version>[0-9]+\.[0-9]+(?:\.[0-9]+)?(?:[-+][a-z0-9.-]+)?)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));

    private readonly IProcessRunner _processRunner;
    private readonly IWorkspaceFileSystem _workspace;
    private readonly TimeProvider _timeProvider;
    private readonly ImmutableArray<EnvironmentProbe> _probes;

    public WindowsEnvironmentDoctor(
        IProcessRunner processRunner,
        IWorkspaceFileSystem workspace,
        TimeProvider timeProvider)
        : this(processRunner, workspace, timeProvider, EnvironmentProbeCatalog.All)
    {
    }

    internal WindowsEnvironmentDoctor(
        IProcessRunner processRunner,
        IWorkspaceFileSystem workspace,
        TimeProvider timeProvider,
        ImmutableArray<EnvironmentProbe> probes)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _probes = probes;
    }

    public async Task<EnvironmentSnapshot> InspectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await _workspace.DirectoryExistsAsync(_probeDirectory, cancellationToken)
                .ConfigureAwait(false))
        {
            await _workspace.CreateDirectoryAsync(_probeDirectory, cancellationToken)
                .ConfigureAwait(false);
        }

        var tools = ImmutableArray.CreateBuilder<EnvironmentTool>(_probes.Length);
        foreach (var probe in _probes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            tools.Add(await InspectToolAsync(probe, cancellationToken).ConfigureAwait(false));
        }

        var properties = new[]
        {
            KeyValuePair.Create(
                "Platform",
                RedactedText.FromTrustedRedaction("Windows").Value),
        };
        return EnvironmentSnapshot.Create(
            _timeProvider.GetUtcNow(),
            tools,
            properties).Value;
    }

    private async Task<EnvironmentTool> InspectToolAsync(
        EnvironmentProbe probe,
        CancellationToken cancellationToken)
    {
        var command = CommandSpec.Create(
            probe.Executable,
            probe.Arguments,
            _workspace,
            _probeDirectory,
            [],
            TimeSpan.FromSeconds(5),
            [0],
            []).Value;

        try
        {
            var result = await _processRunner.RunAsync(command, null, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var available = result.TerminationReason == ProcessTerminationReason.Exited
                && result.ExitCode == 0;
            return new EnvironmentTool(
                probe.Name,
                available ? ExtractVersion(result) : null,
                available);
        }
        catch (InfrastructureOperationException exception) when (exception.Code == "DF-PROC-001")
        {
            return new EnvironmentTool(probe.Name, null, IsAvailable: false);
        }
    }

    private static string? ExtractVersion(ProcessResult result)
    {
        foreach (var line in result.RetainedLines)
        {
            var match = _versionPattern.Match(line.Text.Value);
            if (match.Success)
            {
                return match.Groups["version"].Value;
            }
        }

        return null;
    }
}
