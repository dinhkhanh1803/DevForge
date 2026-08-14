using DevForge.Application.Contracts;
using DevForge.Domain.Privacy;
using DevForge.Domain.Projects;
using DevForge.Infrastructure;
using DevForge.Infrastructure.GitHub;

namespace DevForge.IntegrationTests.Infrastructure.GitHub;

public sealed class GitHubCliAuthenticationTests
{
    [Theory]
    [InlineData("octocat", GitHubAuthenticationState.Authenticated)]
    [InlineData("different-user", GitHubAuthenticationState.DifferentAccount)]
    public async Task ExactActiveAccountIsReturnedWithoutAuthenticationMaterial(
        string activeLogin,
        GitHubAuthenticationState expectedState)
    {
        var runner = new SequenceRunner(
            Success("gh version 2.99.0"),
            Success(activeLogin),
            Success(activeLogin));
        var service = CreateService(runner);
        var identity = GitHubRepositoryIdentity.Create("octocat", "devforge").Value;

        var result = await service.CheckAuthenticationAsync(
            GitHubAuthenticationRequest.Create(identity).Value,
            CancellationToken.None);

        Assert.Equal(expectedState, result.State);
        Assert.Equal(identity, result.Repository);
        Assert.All(runner.Commands, command =>
            Assert.DoesNotContain(command.ArgumentList, argument =>
                argument.Equals("token", StringComparison.OrdinalIgnoreCase)
                || argument.Equals("--show-token", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task MissingOrInvalidAuthenticationReturnsTypedRemediationState()
    {
        var runner = new SequenceRunner(
            Success("gh version 2.99.0"),
            Success());
        var service = CreateService(runner);
        var identity = GitHubRepositoryIdentity.Create("octocat", "devforge").Value;

        var result = await service.CheckAuthenticationAsync(
            GitHubAuthenticationRequest.Create(identity).Value,
            CancellationToken.None);

        Assert.Equal(GitHubAuthenticationState.NotAuthenticated, result.State);
    }

    [Fact]
    public async Task ApiFailureIsNetworkFailureRatherThanAuthenticationRemediation()
    {
        var runner = new SequenceRunner(
            Success("gh version 2.99.0"),
            Success("octocat"),
            Exited(1));
        var service = CreateService(runner);
        var identity = GitHubRepositoryIdentity.Create("octocat", "devforge").Value;

        var exception = await Assert.ThrowsAsync<InfrastructureOperationException>(() =>
            service.CheckAuthenticationAsync(
                GitHubAuthenticationRequest.Create(identity).Value,
                CancellationToken.None));

        Assert.Equal("DF-GH-005", exception.Code);
    }

    [Theory]
    [InlineData(ProcessTerminationReason.TimedOut, "DF-GH-002")]
    [InlineData(ProcessTerminationReason.Cancelled, null)]
    public async Task TerminalProcessStateIsMappedWithoutLeakingConfigPath(
        ProcessTerminationReason reason,
        string? expectedCode)
    {
        var runner = new SequenceRunner(Terminated(reason));
        var service = CreateService(runner);
        var request = GitHubAuthenticationRequest.Create(
            GitHubRepositoryIdentity.Create("octocat", "devforge").Value).Value;

        if (reason == ProcessTerminationReason.Cancelled)
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                service.CheckAuthenticationAsync(request, CancellationToken.None));
            return;
        }

        var exception = await Assert.ThrowsAsync<InfrastructureOperationException>(() =>
            service.CheckAuthenticationAsync(request, CancellationToken.None));
        Assert.Equal(expectedCode, exception.Code);
        Assert.DoesNotContain("C:\\private-gh-config", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(true, "octocat", "extra")]
    [InlineData(false, "not a canonical login", null)]
    public async Task TruncatedOrMalformedIdentityFailsClosed(
        bool truncated,
        string firstLine,
        string? secondLine)
    {
        var lines = secondLine is null ? new[] { firstLine } : [firstLine, secondLine];
        var runner = new SequenceRunner(
            Success("gh version 2.99.0"),
            Success("octocat"),
            Exited(0, truncated, lines));
        var service = CreateService(runner);
        var request = GitHubAuthenticationRequest.Create(
            GitHubRepositoryIdentity.Create("octocat", "devforge").Value).Value;

        var exception = await Assert.ThrowsAsync<InfrastructureOperationException>(() =>
            service.CheckAuthenticationAsync(request, CancellationToken.None));

        Assert.Equal("DF-GH-001", exception.Code);
    }

    private static GitHubCliService CreateService(IProcessRunner runner) => new(
        runner,
        SensitiveProcessValue.Create("C:\\private-gh-config").Value);

    private static ProcessResult Success(params string[] lines) => Exited(0, false, lines);

    private static ProcessResult Exited(
        int exitCode,
        bool truncated = false,
        params string[] lines) => ProcessResult.Create(
            ProcessTerminationReason.Exited,
            exitCode,
            lines.Select(line => ProcessOutputLine.Create(
                ProcessOutputChannel.StandardOutput,
                RedactedText.FromTrustedRedaction(line).Value).Value),
            truncated).Value;

    private static ProcessResult Terminated(ProcessTerminationReason reason) =>
        ProcessResult.Create(reason, null, []).Value;

    private sealed class SequenceRunner(params ProcessResult[] results) : IProcessRunner
    {
        private readonly Queue<ProcessResult> _results = new(results);

        public List<CommandSpec> Commands { get; } = [];

        public Task CheckPreconditionsAsync(
            CommandSpec command,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<ProcessResult> RunAsync(
            CommandSpec command,
            IProgress<ProcessOutputLine>? progress,
            CancellationToken cancellationToken)
        {
            Commands.Add(command);
            return Task.FromResult(_results.Dequeue());
        }
    }
}
