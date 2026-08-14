using System.Text.Json;
using DevForge.Application.Contracts;
using DevForge.Domain.Privacy;
using DevForge.Domain.Projects;
using DevForge.Infrastructure;
using DevForge.Infrastructure.GitHub;

namespace DevForge.IntegrationTests.Infrastructure.GitHub;

public sealed class GitHubCliPublicationTests
{
    private const string CommitId = "0123456789abcdef0123456789abcdef01234567";
    private const string Nonce = "abcdef0123456789abcdef0123456789";

    [Fact]
    public async Task MissingRepositoryIsCreatedPrivateWithNonceAndPushedExactlyOnce()
    {
        var runner = new GitHubRemoteSimulator();
        var service = CreateService(runner);
        var progress = new GitHubProgress();

        var result = await service.PublishAsync(
            CreateRequest(GitBranchPolicy.Main, isPrivate: true),
            progress,
            CancellationToken.None);
        var verified = await service.VerifyAsync(
            CreateRequest(GitBranchPolicy.Main, isPrivate: true),
            CancellationToken.None);

        Assert.Equal("https://github.com/octocat/devforge", result.RepositoryUrl);
        Assert.True(result.IsPrivate);
        Assert.Equal(Nonce, result.OwnershipNonce);
        Assert.Equal(["main"], result.Branches.ToArray());
        Assert.Equal(1, runner.CreateCount);
        Assert.Equal(["main"], runner.PushedBranches);
        Assert.Equal("https://github.com/octocat/devforge.git", runner.LocalOrigin);
        Assert.Equal("DevForge ownership " + Nonce, runner.Description);
        Assert.Equal(result.RepositoryUrl, verified.RepositoryUrl);
        Assert.Equal(1, progress.RemoteCreatedCount);
        Assert.DoesNotContain(runner.Commands.SelectMany(command => command.ArgumentList), argument =>
            argument.Contains("--force", StringComparison.OrdinalIgnoreCase)
            || argument.Equals("delete", StringComparison.OrdinalIgnoreCase)
            || argument.Equals("token", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task VerificationOnlyRefusesRemoteDriftWithoutCreatingOrPushing()
    {
        var runner = GitHubRemoteSimulator.Existing(
            isPrivate: true,
            ("main", new string('f', 40)));
        runner.LocalOrigin = "https://github.com/octocat/devforge.git";
        var service = CreateService(runner);

        await Assert.ThrowsAsync<InfrastructureOperationException>(() => service.VerifyAsync(
            CreateRequest(GitBranchPolicy.Main, isPrivate: true),
            CancellationToken.None));

        Assert.Equal(0, runner.CreateCount);
        Assert.Empty(runner.PushedBranches);
    }

    private sealed class GitHubProgress : IGitHubPublicationProgress
    {
        public int RemoteCreatedCount { get; private set; }

        public Task RemoteCreatedAsync(CancellationToken cancellationToken)
        {
            RemoteCreatedCount++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task NonceOwnedPartialRemotePushesOnlyMissingReviewedBranch()
    {
        var runner = GitHubRemoteSimulator.Existing(
            isPrivate: true,
            ("main", CommitId));
        var service = CreateService(runner);

        var result = await service.PublishAsync(
            CreateRequest(GitBranchPolicy.MainAndDevelop, isPrivate: true),
            CancellationToken.None);

        Assert.Equal(0, runner.CreateCount);
        Assert.Equal(["develop"], runner.PushedBranches);
        Assert.Equal(["main", "develop"], result.Branches.ToArray());
    }

    [Fact]
    public async Task CompleteOwnedRemoteAndExactOriginAreIdempotentlyAdopted()
    {
        var runner = GitHubRemoteSimulator.Existing(
            isPrivate: false,
            ("main", CommitId),
            ("develop", CommitId));
        runner.LocalOrigin = "https://github.com/octocat/devforge.git";
        var verifier = new VerifiedGitService();
        var service = CreateService(runner, verifier);

        var result = await service.PublishAsync(
            CreateRequest(GitBranchPolicy.MainAndDevelop, isPrivate: false),
            CancellationToken.None);

        Assert.Equal(0, runner.CreateCount);
        Assert.Empty(runner.PushedBranches);
        Assert.False(result.IsPrivate);
        Assert.Equal(
            ["https://github.com/octocat/devforge.git"],
            verifier.ExpectedOrigins);
    }

    [Theory]
    [InlineData("nonce")]
    [InlineData("visibility")]
    [InlineData("owner")]
    [InlineData("organization")]
    [InlineData("fork")]
    [InlineData("archived")]
    [InlineData("mirror")]
    [InlineData("template")]
    [InlineData("url")]
    [InlineData("unexpected-ref")]
    [InlineData("wrong-commit")]
    [InlineData("origin")]
    [InlineData("pushurl")]
    [InlineData("extra-remote")]
    public async Task UnexpectedRemoteEvidenceFailsClosedWithoutCreateOrPush(string mutation)
    {
        var runner = GitHubRemoteSimulator.Existing(isPrivate: true, ("main", CommitId));
        runner.ApplyMutation(mutation);
        var service = CreateService(runner);

        var exception = await Assert.ThrowsAsync<InfrastructureOperationException>(() =>
            service.PublishAsync(
                CreateRequest(GitBranchPolicy.Main, isPrivate: true),
                CancellationToken.None));

        Assert.Equal("DF-GH-004", exception.Code);
        Assert.Equal(0, runner.CreateCount);
        Assert.Empty(runner.PushedBranches);
    }

    [Fact]
    public async Task DifferentAuthenticatedAccountFailsBeforeRemoteInspection()
    {
        var runner = new GitHubRemoteSimulator { ActiveLogin = "different-user" };
        var service = CreateService(runner);

        var exception = await Assert.ThrowsAsync<InfrastructureOperationException>(() =>
            service.PublishAsync(
                CreateRequest(GitBranchPolicy.Main, isPrivate: true),
                CancellationToken.None));

        Assert.Equal("DF-GH-003", exception.Code);
        Assert.DoesNotContain(runner.Commands, command =>
            command.ArgumentList.Contains("view", StringComparer.Ordinal));
    }

    [Fact]
    public async Task UnsafeLocalRepositoryFailsBeforeRemoteCreateOrPush()
    {
        var runner = new GitHubRemoteSimulator();
        var service = CreateService(runner, new FailingGitService());

        var exception = await Assert.ThrowsAsync<InfrastructureOperationException>(() =>
            service.PublishAsync(
                CreateRequest(GitBranchPolicy.Main, isPrivate: true),
                CancellationToken.None));

        Assert.Equal("DF-GH-004", exception.Code);
        Assert.Equal(0, runner.CreateCount);
        Assert.Empty(runner.PushedBranches);
    }

    [Fact]
    public async Task RepositoryLookupFailureIsNetworkFailureAndDoesNotPush()
    {
        var runner = GitHubRemoteSimulator.Existing(isPrivate: true, ("main", CommitId));
        runner.ApplyMutation("network");
        var service = CreateService(runner);

        var exception = await Assert.ThrowsAsync<InfrastructureOperationException>(() =>
            service.PublishAsync(
                CreateRequest(GitBranchPolicy.Main, isPrivate: true),
                CancellationToken.None));

        Assert.Equal("DF-GH-005", exception.Code);
        Assert.Empty(runner.PushedBranches);
    }

    [Theory]
    [InlineData("malformed-json")]
    [InlineData("duplicate-property")]
    [InlineData("unknown-property")]
    [InlineData("missing-property")]
    [InlineData("wrong-property-type")]
    [InlineData("malformed-ref")]
    [InlineData("too-many-refs")]
    public async Task MalformedOrUnboundedRemoteOutputFailsClosed(string mutation)
    {
        var runner = GitHubRemoteSimulator.Existing(isPrivate: true, ("main", CommitId));
        runner.ApplyMutation(mutation);
        var service = CreateService(runner);

        var exception = await Assert.ThrowsAsync<InfrastructureOperationException>(() =>
            service.PublishAsync(
                CreateRequest(GitBranchPolicy.Main, isPrivate: true),
                CancellationToken.None));

        Assert.Equal("DF-GH-004", exception.Code);
        Assert.Empty(runner.PushedBranches);
    }

    [Theory]
    [InlineData(ProcessTerminationReason.TimedOut, "DF-GH-002")]
    [InlineData(ProcessTerminationReason.Cancelled, null)]
    public async Task PublishTerminalStateIsMappedWithoutMutation(
        ProcessTerminationReason reason,
        string? expectedCode)
    {
        var runner = new GitHubRemoteSimulator { TerminalReason = reason };
        var service = CreateService(runner);
        var request = CreateRequest(GitBranchPolicy.Main, isPrivate: true);

        if (reason == ProcessTerminationReason.Cancelled)
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                service.PublishAsync(request, CancellationToken.None));
            return;
        }

        var exception = await Assert.ThrowsAsync<InfrastructureOperationException>(() =>
            service.PublishAsync(request, CancellationToken.None));
        Assert.Equal(expectedCode, exception.Code);
        Assert.Equal(0, runner.CreateCount);
    }

    private static GitHubCliService CreateService(
        IProcessRunner runner,
        IGitService? gitService = null) => new(
        runner,
        SensitiveProcessValue.Create("C:\\private-gh-config").Value,
        gitService ?? new VerifiedGitService(),
        SensitiveProcessValue.Create("C:\\trusted\\gh.exe").Value);

    private static GitHubPublishRequest CreateRequest(
        GitBranchPolicy branchPolicy,
        bool isPrivate)
    {
        var branches = branchPolicy == GitBranchPolicy.Main
            ? new[] { "main" }
            : ["main", "develop"];
        return GitHubPublishRequest.Create(
            new CommandWorkspace(),
            GitHubRepositoryIdentity.Create("octocat", "devforge").Value,
            branchPolicy,
            CommitId,
            branches,
            isPrivate,
            Nonce,
            "sha256:" + new string('a', 64)).Value;
    }

    private sealed class GitHubRemoteSimulator : IProcessRunner
    {
        private readonly Dictionary<string, string> _refs = new(StringComparer.Ordinal);
        private string? _mutation;

        public string ActiveLogin { get; set; } = "octocat";
        public bool RepositoryExists { get; private set; }
        public bool IsPrivate { get; private set; } = true;
        public string Description { get; private set; } = "DevForge ownership " + Nonce;
        public string NameWithOwner { get; private set; } = "octocat/devforge";
        public string Url { get; private set; } = "https://github.com/octocat/devforge";
        public bool IsOrganization { get; private set; }
        public bool IsFork { get; private set; }
        public bool IsArchived { get; private set; }
        public bool IsMirror { get; private set; }
        public bool IsTemplate { get; private set; }
        public string? LocalOrigin { get; set; }
        public string? LocalPushOrigin { get; set; }
        public ProcessTerminationReason? TerminalReason { get; set; }
        public int CreateCount { get; private set; }
        public List<string> PushedBranches { get; } = [];
        public List<CommandSpec> Commands { get; } = [];

        public static GitHubRemoteSimulator Existing(
            bool isPrivate,
            params (string Branch, string Commit)[] refs)
        {
            var runner = new GitHubRemoteSimulator
            {
                RepositoryExists = true,
                IsPrivate = isPrivate,
            };
            foreach (var (branch, commit) in refs)
            {
                runner._refs.Add(branch, commit);
            }
            return runner;
        }

        public void ApplyMutation(string mutation)
        {
            _mutation = mutation;
            switch (mutation)
            {
                case "nonce": Description = "DevForge ownership " + new string('0', 32); break;
                case "visibility": IsPrivate = false; break;
                case "owner": NameWithOwner = "someone/devforge"; break;
                case "organization": IsOrganization = true; break;
                case "fork": IsFork = true; break;
                case "archived": IsArchived = true; break;
                case "mirror": IsMirror = true; break;
                case "template": IsTemplate = true; break;
                case "url": Url = "https://example.invalid/octocat/devforge"; break;
                case "unexpected-ref": _refs["feature"] = CommitId; break;
                case "wrong-commit": _refs["main"] = new string('f', 40); break;
                case "origin": LocalOrigin = "https://github.com/other/repo.git"; break;
                case "pushurl":
                    LocalOrigin = "https://github.com/octocat/devforge.git";
                    LocalPushOrigin = "https://github.com/other/repo.git";
                    break;
                case "extra-remote": LocalOrigin = "EXTRA_REMOTE"; break;
                case "malformed-json": break;
                case "duplicate-property": break;
                case "unknown-property": break;
                case "missing-property": break;
                case "wrong-property-type": break;
                case "malformed-ref": break;
                case "too-many-refs": break;
                case "network": break;
                default: throw new InvalidOperationException();
            }
        }

        public Task CheckPreconditionsAsync(
            CommandSpec command,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<ProcessResult> RunAsync(
            CommandSpec command,
            IProgress<ProcessOutputLine>? progress,
            CancellationToken cancellationToken)
        {
            Commands.Add(command);
            if (TerminalReason is { } terminal)
            {
                TerminalReason = null;
                return Task.FromResult(ProcessResult.Create(terminal, null, []).Value);
            }

            var args = command.ArgumentList.ToArray();
            if (command.Executable.Tool == ExecutableTool.Git)
            {
                var operationIndex = Array.FindIndex(
                    args,
                    argument => argument is "remote" or "push");
                args = args[operationIndex..];
            }
            if (args.SequenceEqual(["--version"]))
            {
                return Task.FromResult(Success("gh version 2.99.0"));
            }
            if (args.Contains("status"))
            {
                return Task.FromResult(Success(ActiveLogin));
            }
            if (args.SequenceEqual(["api", "user", "--hostname", "github.com", "--jq", ".login"]))
            {
                return Task.FromResult(Success(ActiveLogin));
            }
            if (args.Contains("view"))
            {
                return Task.FromResult(ViewResult());
            }
            if (args.Contains("create"))
            {
                return Task.FromResult(CreateResult(args));
            }
            if (args.Any(argument => argument.EndsWith("/git/matching-refs/heads", StringComparison.Ordinal)))
            {
                return Task.FromResult(ReferenceResult());
            }
            if (args.SequenceEqual(["remote"]))
            {
                return Task.FromResult(RemotesResult());
            }
            if (args.SequenceEqual(["remote", "get-url", "origin"]))
            {
                return Task.FromResult(LocalOrigin is null ? Exited(2) : Success(LocalOrigin));
            }
            if (args.SequenceEqual(["remote", "get-url", "--push", "--all", "origin"]))
            {
                var pushOrigin = LocalPushOrigin ?? LocalOrigin;
                return Task.FromResult(pushOrigin is null ? Exited(2) : Success(pushOrigin));
            }
            if (args.Take(3).SequenceEqual(["remote", "add", "origin"]))
            {
                LocalOrigin = args[3];
                return Task.FromResult(Success());
            }
            if (args.FirstOrDefault() == "push")
            {
                return Task.FromResult(PushResult(args));
            }
            throw new InvalidOperationException(string.Join(' ', args));
        }

        private ProcessResult ViewResult()
        {
            if (!RepositoryExists)
            {
                return Exited(1);
            }
            if (_mutation == "network")
            {
                return Exited(1);
            }
            if (_mutation == "malformed-json")
            {
                return Success("{");
            }
            var payload = new Dictionary<string, object?>
            {
                ["nameWithOwner"] = NameWithOwner,
                ["description"] = Description,
                ["visibility"] = IsPrivate ? "PRIVATE" : "PUBLIC",
                ["isEmpty"] = _refs.Count == 0,
                ["isFork"] = IsFork,
                ["isInOrganization"] = IsOrganization,
                ["isArchived"] = IsArchived,
                ["isMirror"] = IsMirror,
                ["isTemplate"] = IsTemplate,
                ["url"] = Url,
            };
            var json = JsonSerializer.Serialize(payload);
            if (_mutation == "duplicate-property")
            {
                json = json.Insert(1, "\"url\":\"https://github.com/octocat/devforge\",");
            }
            else if (_mutation == "unknown-property")
            {
                json = json.Insert(1, "\"unexpected\":\"forbidden\",");
            }
            else if (_mutation == "missing-property")
            {
                payload.Remove("description");
                json = JsonSerializer.Serialize(payload);
            }
            else if (_mutation == "wrong-property-type")
            {
                payload["isFork"] = "false";
                json = JsonSerializer.Serialize(payload);
            }
            return Success(json);
        }

        private ProcessResult CreateResult(string[] args)
        {
            if (RepositoryExists)
            {
                return Exited(1);
            }
            RepositoryExists = true;
            CreateCount++;
            IsPrivate = args.Contains("--private", StringComparer.Ordinal);
            Description = args[args.IndexOf("--description") + 1];
            return Success(Url);
        }

        private ProcessResult ReferenceResult()
        {
            if (_refs.Count == 0)
            {
                return Exited(1);
            }
            if (_mutation == "malformed-ref")
            {
                return Success("refs/heads/main not-a-tab " + CommitId);
            }
            if (_mutation == "too-many-refs")
            {
                return Success(
                    "refs/heads/main\t" + CommitId,
                    "refs/heads/develop\t" + CommitId,
                    "refs/heads/third\t" + CommitId);
            }
            return Success(_refs.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"refs/heads/{pair.Key}\t{pair.Value}")
                .ToArray());
        }

        private ProcessResult RemotesResult()
        {
            if (LocalOrigin == "EXTRA_REMOTE")
            {
                return Success("origin", "upstream");
            }
            return LocalOrigin is null ? Success() : Success("origin");
        }

        private ProcessResult PushResult(string[] args)
        {
            var refspec = args[^1];
            var branch = refspec.Split('/')[2].Split(':')[0];
            _refs[branch] = CommitId;
            PushedBranches.Add(branch);
            return Success();
        }

        private static ProcessResult Success(params string[] lines) => Exited(0, lines);

        private static ProcessResult Exited(int exitCode, params string[] lines) =>
            ProcessResult.Create(
                ProcessTerminationReason.Exited,
                exitCode,
                lines.Select(line => ProcessOutputLine.Create(
                    ProcessOutputChannel.StandardOutput,
                    RedactedText.FromTrustedRedaction(line).Value).Value)).Value;
    }

    private sealed class CommandWorkspace : IWorkspaceFileSystem
    {
        public WorkspaceRoot Root { get; } = WorkspaceRoot.Create("C:\\github-publish-workspace").Value;

        public Task<bool> FileExistsAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DirectoryExistsAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CreateDirectoryAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Stream> OpenWriteAsync(WorkspaceRelativePath path, bool overwrite, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteFileAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<System.Collections.Immutable.ImmutableArray<WorkspaceRelativePath>> EnumerateAllFilesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<System.Collections.Immutable.ImmutableArray<WorkspaceRelativePath>> EnumerateRootDirectoriesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<System.Collections.Immutable.ImmutableArray<WorkspaceRelativePath>> EnumerateFilesAsync(WorkspaceRelativePath directory, bool recursive, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<System.Collections.Immutable.ImmutableArray<WorkspaceRelativePath>> EnumerateDirectoriesAsync(WorkspaceRelativePath directory, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteDirectoryAsync(WorkspaceRelativePath path, DirectoryCleanupIntent intent, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MoveDirectoryAsync(WorkspaceRelativePath source, WorkspaceRelativePath destination, WorkspaceMoveIntent intent, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class VerifiedGitService : IGitService
    {
        public List<string?> ExpectedOrigins { get; } = [];

        public Task<GitRepositoryReceipt> BootstrapAsync(
            GitBootstrapRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<GitRepositoryReceipt> VerifyAsync(
            GitVerificationRequest request,
            CancellationToken cancellationToken)
        {
            ExpectedOrigins.Add(request.ExpectedOriginUrl);
            var branches = request.BranchPolicy == GitBranchPolicy.Main
                ? new[] { "main" }
                : ["main", "develop"];
            return Task.FromResult(GitRepositoryReceipt.Create(
                CommitId,
                request.BranchPolicy,
                branches,
                request.FinalTreeDigest).Value);
        }
    }

    private sealed class FailingGitService : IGitService
    {
        public Task<GitRepositoryReceipt> BootstrapAsync(
            GitBootstrapRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<GitRepositoryReceipt> VerifyAsync(
            GitVerificationRequest request,
            CancellationToken cancellationToken) => throw new InfrastructureOperationException(
                "DF-GIT-001",
                "The local repository is unsafe.");
    }
}

internal static class ImmutableArrayTestExtensions
{
    public static int IndexOf(this IReadOnlyList<string> values, string expected)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (StringComparer.Ordinal.Equals(values[index], expected))
            {
                return index;
            }
        }
        return -1;
    }
}
