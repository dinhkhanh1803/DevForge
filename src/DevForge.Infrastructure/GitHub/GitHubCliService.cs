using DevForge.Application.Contracts;
using DevForge.Domain.Projects;
using DevForge.Infrastructure.Git;
using DevForge.Infrastructure.Processes;

namespace DevForge.Infrastructure.GitHub;

public sealed class GitHubCliService : IGitHubService, IPublicationGitHubService
{
    private readonly SensitiveProcessValue _configDirectory;
    private readonly Func<SensitiveProcessValue> _credentialHelperFactory;
    private readonly IGitService? _gitService;
    private readonly IProcessRunner _processRunner;

    public GitHubCliService(IProcessRunner processRunner, IGitService gitService)
        : this(
            processRunner,
            LocateCurrentUserConfigDirectory(),
            gitService,
            LocateTrustedGitHubCli)
    {
    }

    internal GitHubCliService(
        IProcessRunner processRunner,
        SensitiveProcessValue configDirectory)
        : this(processRunner, configDirectory, null, LocateTrustedGitHubCli)
    {
    }

    internal GitHubCliService(
        IProcessRunner processRunner,
        SensitiveProcessValue configDirectory,
        IGitService gitService,
        SensitiveProcessValue credentialHelperExecutable)
        : this(processRunner, configDirectory, gitService, () => credentialHelperExecutable)
    {
    }

    private GitHubCliService(
        IProcessRunner processRunner,
        SensitiveProcessValue configDirectory,
        IGitService? gitService,
        Func<SensitiveProcessValue> credentialHelperFactory)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _configDirectory = configDirectory
            ?? throw new ArgumentNullException(nameof(configDirectory));
        _gitService = gitService;
        _credentialHelperFactory = credentialHelperFactory
            ?? throw new ArgumentNullException(nameof(credentialHelperFactory));
    }

    public Task<GitHubAuthenticationResult> CheckAuthenticationAsync(
        GitHubAuthenticationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return GuardAsync(
            () => CheckAuthenticationCoreAsync(request, cancellationToken),
            cancellationToken);
    }

    public Task<GitHubPublishResult> PublishAsync(
        GitHubPublishRequest request,
        CancellationToken cancellationToken)
    {
        return PublishAsync(request, NullGitHubPublicationProgress.Instance, cancellationToken);
    }

    public Task<GitHubPublishResult> PublishAsync(
        GitHubPublishRequest request,
        IGitHubPublicationProgress progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(progress);
        return GuardAsync(
            () => PublishCoreAsync(request, progress, cancellationToken),
            cancellationToken);
    }

    public Task<GitHubPublishResult> VerifyAsync(
        GitHubPublishRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return GuardAsync(
            () => VerifyPublicationCoreAsync(request, cancellationToken),
            cancellationToken);
    }

    private async Task<GitHubAuthenticationResult> CheckAuthenticationCoreAsync(
        GitHubAuthenticationRequest request,
        CancellationToken cancellationToken) => await CheckAuthenticationCoreAsync(
            request,
            AuthenticationWorkspace.Instance,
            cancellationToken).ConfigureAwait(false);

    private async Task<GitHubAuthenticationResult> CheckAuthenticationCoreAsync(
        GitHubAuthenticationRequest request,
        IWorkspaceFileSystem workspace,
        CancellationToken cancellationToken)
    {
        await RunRequiredAsync(
            GitHubCommandFactory.Version(workspace, _configDirectory),
            cancellationToken).ConfigureAwait(false);
        var status = await RunAsync(
            GitHubCommandFactory.AuthenticationStatus(workspace, _configDirectory),
            cancellationToken).ConfigureAwait(false);
        if (status.ExitCode == 1)
        {
            return CreateAuthenticationResult(
                request.Repository,
                GitHubAuthenticationState.NotAuthenticated);
        }
        var statusLines = OutputLines(status);
        if (statusLines.Length == 0)
        {
            return CreateAuthenticationResult(
                request.Repository,
                GitHubAuthenticationState.NotAuthenticated);
        }

        var statusLogin = ParseCanonicalLogin(statusLines);
        var current = await RunAsync(
            GitHubCommandFactory.CurrentLogin(workspace, _configDirectory),
            cancellationToken).ConfigureAwait(false);
        if (current.ExitCode == 1)
        {
            throw NetworkFailure();
        }

        var currentLogin = ParseCanonicalLogin(OutputLines(current));
        if (!StringComparer.Ordinal.Equals(statusLogin, currentLogin))
        {
            throw UnexpectedEvidence();
        }

        return CreateAuthenticationResult(
            request.Repository,
            StringComparer.Ordinal.Equals(currentLogin, request.Repository.Account)
                ? GitHubAuthenticationState.Authenticated
                : GitHubAuthenticationState.DifferentAccount);
    }

    private async Task<GitHubPublishResult> PublishCoreAsync(
        GitHubPublishRequest request,
        IGitHubPublicationProgress progress,
        CancellationToken cancellationToken)
    {
        var authenticationRequest = GitHubAuthenticationRequest.Create(request.Repository);
        if (!authenticationRequest.IsValid)
        {
            throw UnexpectedEvidence();
        }

        var authentication = await CheckAuthenticationCoreAsync(
            authenticationRequest.Value,
            request.Workspace,
            cancellationToken).ConfigureAwait(false);
        if (authentication.State != GitHubAuthenticationState.Authenticated)
        {
            throw new InfrastructureOperationException(
                "DF-GH-003",
                "GitHub publishing requires the exact reviewed personal account.");
        }

        if (_gitService is null)
        {
            throw UnexpectedEvidence();
        }

        var existingOrigin = await InspectExistingOriginAsync(request, cancellationToken)
            .ConfigureAwait(false);
        await VerifyLocalRepositoryAsync(request, existingOrigin, cancellationToken)
            .ConfigureAwait(false);

        var repositoryResult = await RunAsync(
            GitHubCommandFactory.ViewRepository(
                request.Workspace,
                _configDirectory,
                request.Repository,
                allowMissing: true),
            cancellationToken).ConfigureAwait(false);
        if (repositoryResult.ExitCode == 1)
        {
            await EnsureAuthenticatedAsync(request, cancellationToken).ConfigureAwait(false);
            var creation = await RunAsync(
                GitHubCommandFactory.CreateRepository(
                    request.Workspace,
                    _configDirectory,
                    request.Repository,
                    request.IsPrivate,
                    request.OwnershipNonce),
                cancellationToken).ConfigureAwait(false);
            repositoryResult = await RunAsync(
                GitHubCommandFactory.ViewRepository(
                    request.Workspace,
                    _configDirectory,
                    request.Repository,
                    allowMissing: true),
                cancellationToken).ConfigureAwait(false);
            if (repositoryResult.ExitCode != 0)
            {
                throw NetworkFailure();
            }

            if (creation.ExitCode != 0)
            {
                // A failed create may mean an interrupted successful create or an
                // already-existing repository. Exact evidence below decides adoption.
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        var repository = GitHubEvidenceParser.ParseRepository(
            OutputLines(repositoryResult),
            request);
        var references = repository.IsEmpty
            ? System.Collections.Immutable.ImmutableDictionary<string, string>.Empty
            : await ReadReferencesAsync(request, cancellationToken).ConfigureAwait(false);
        if (repository.IsEmpty != (references.Count == 0))
        {
            throw RemoteUnsafe();
        }

        await progress.RemoteCreatedAsync(CancellationToken.None).ConfigureAwait(false);

        await EnsureExactOriginAsync(request, cancellationToken).ConfigureAwait(false);
        var credentialHelper = _credentialHelperFactory();
        foreach (var branch in request.Branches)
        {
            if (!references.ContainsKey(branch))
            {
                await VerifyLocalRepositoryAsync(
                    request,
                    request.Repository.HttpsRemoteUrl,
                    cancellationToken).ConfigureAwait(false);
                await EnsureAuthenticatedAsync(request, cancellationToken).ConfigureAwait(false);
                await RunRequiredAsync(
                    GitCommandFactory.PushBranch(
                        request.Workspace,
                        branch,
                        request.InitialCommitId,
                        request.Repository.HttpsRemoteUrl,
                        credentialHelper,
                        _configDirectory),
                    cancellationToken).ConfigureAwait(false);
            }
        }

        var finalRepository = GitHubEvidenceParser.ParseRepository(
            OutputLines(await RunRequiredAsync(
                GitHubCommandFactory.ViewRepository(
                    request.Workspace,
                    _configDirectory,
                    request.Repository,
                    allowMissing: false),
                cancellationToken).ConfigureAwait(false)),
            request);
        var finalReferences = await ReadReferencesAsync(request, cancellationToken)
            .ConfigureAwait(false);
        if (finalRepository.IsEmpty
            || finalReferences.Count != request.Branches.Length
            || request.Branches.Any(branch => !finalReferences.ContainsKey(branch)))
        {
            throw RemoteUnsafe();
        }

        await VerifyExactOriginAsync(request, cancellationToken).ConfigureAwait(false);
        var result = GitHubPublishResult.Create(
            request.Repository,
            finalRepository.Url,
            request.InitialCommitId,
            request.Branches,
            request.BranchPolicy,
            request.IsPrivate,
            request.OwnershipNonce);
        return result.IsValid ? result.Value : throw RemoteUnsafe();
    }

    private async Task<GitHubPublishResult> VerifyPublicationCoreAsync(
        GitHubPublishRequest request,
        CancellationToken cancellationToken)
    {
        var authenticationRequest = GitHubAuthenticationRequest.Create(request.Repository);
        if (!authenticationRequest.IsValid)
        {
            throw UnexpectedEvidence();
        }

        var authentication = await CheckAuthenticationCoreAsync(
            authenticationRequest.Value,
            request.Workspace,
            cancellationToken).ConfigureAwait(false);
        if (authentication.State != GitHubAuthenticationState.Authenticated || _gitService is null)
        {
            throw new InfrastructureOperationException(
                "DF-GH-003",
                "GitHub verification requires the exact reviewed personal account.");
        }

        await VerifyLocalRepositoryAsync(
            request,
            request.Repository.HttpsRemoteUrl,
            cancellationToken).ConfigureAwait(false);
        var repository = GitHubEvidenceParser.ParseRepository(
            OutputLines(await RunRequiredAsync(
                GitHubCommandFactory.ViewRepository(
                    request.Workspace,
                    _configDirectory,
                    request.Repository,
                    allowMissing: false),
                cancellationToken).ConfigureAwait(false)),
            request);
        var references = await ReadReferencesAsync(request, cancellationToken).ConfigureAwait(false);
        if (repository.IsEmpty
            || references.Count != request.Branches.Length
            || request.Branches.Any(branch => !references.ContainsKey(branch)))
        {
            throw RemoteUnsafe();
        }

        await VerifyExactOriginAsync(request, cancellationToken).ConfigureAwait(false);
        var result = GitHubPublishResult.Create(
            request.Repository,
            repository.Url,
            request.InitialCommitId,
            request.Branches,
            request.BranchPolicy,
            request.IsPrivate,
            request.OwnershipNonce);
        return result.IsValid ? result.Value : throw RemoteUnsafe();
    }

    private sealed class NullGitHubPublicationProgress : IGitHubPublicationProgress
    {
        public static NullGitHubPublicationProgress Instance { get; } = new();

        public Task RemoteCreatedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private async Task EnsureAuthenticatedAsync(
        GitHubPublishRequest request,
        CancellationToken cancellationToken)
    {
        var authenticationRequest = GitHubAuthenticationRequest.Create(request.Repository);
        if (!authenticationRequest.IsValid)
        {
            throw UnexpectedEvidence();
        }

        var authentication = await CheckAuthenticationCoreAsync(
            authenticationRequest.Value,
            request.Workspace,
            cancellationToken).ConfigureAwait(false);
        if (authentication.State != GitHubAuthenticationState.Authenticated)
        {
            throw new InfrastructureOperationException(
                "DF-GH-003",
                "GitHub publishing requires the exact reviewed personal account.");
        }
    }

    private async Task VerifyLocalRepositoryAsync(
        GitHubPublishRequest request,
        string? expectedOriginUrl,
        CancellationToken cancellationToken)
    {
        var verification = GitVerificationRequest.Create(
            request.Workspace,
            request.BranchPolicy,
            request.FinalTreeDigest,
            request.InitialCommitId,
            expectedOriginUrl);
        if (!verification.IsValid)
        {
            throw RemoteUnsafe();
        }

        GitRepositoryReceipt receipt;
        try
        {
            receipt = await _gitService!.VerifyAsync(verification.Value, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InfrastructureOperationException exception) when (
            exception.Code.StartsWith("DF-GIT-", StringComparison.Ordinal))
        {
            throw RemoteUnsafe();
        }
        if (!StringComparer.Ordinal.Equals(receipt.InitialCommitId, request.InitialCommitId)
            || receipt.BranchPolicy != request.BranchPolicy
            || !StringComparer.Ordinal.Equals(receipt.FinalTreeDigest, request.FinalTreeDigest)
            || !receipt.Branches.SequenceEqual(request.Branches, StringComparer.Ordinal))
        {
            throw RemoteUnsafe();
        }
    }

    private async Task<System.Collections.Immutable.ImmutableDictionary<string, string>> ReadReferencesAsync(
        GitHubPublishRequest request,
        CancellationToken cancellationToken)
    {
        var result = await RunRequiredAsync(
            GitHubCommandFactory.BranchReferences(
                request.Workspace,
                _configDirectory,
                request.Repository),
            cancellationToken).ConfigureAwait(false);
        return GitHubEvidenceParser.ParseReferences(OutputLines(result), request);
    }

    private async Task EnsureExactOriginAsync(
        GitHubPublishRequest request,
        CancellationToken cancellationToken)
    {
        var remotes = OutputLines(await RunRequiredAsync(
            GitCommandFactory.Remotes(request.Workspace),
            cancellationToken).ConfigureAwait(false));
        if (remotes.Length == 0)
        {
            await RunRequiredAsync(
                GitCommandFactory.AddOrigin(request.Workspace, request.Repository.HttpsRemoteUrl),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (remotes.Length != 1 || !StringComparer.Ordinal.Equals(remotes[0], "origin"))
        {
            throw RemoteUnsafe();
        }

        await VerifyExactOriginAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> InspectExistingOriginAsync(
        GitHubPublishRequest request,
        CancellationToken cancellationToken)
    {
        var remotes = OutputLines(await RunRequiredAsync(
            GitCommandFactory.Remotes(request.Workspace),
            cancellationToken).ConfigureAwait(false));
        if (remotes.Length == 0)
        {
            return null;
        }

        if (remotes.Length != 1 || !StringComparer.Ordinal.Equals(remotes[0], "origin"))
        {
            throw RemoteUnsafe();
        }

        await VerifyExactOriginAsync(request, cancellationToken).ConfigureAwait(false);
        return request.Repository.HttpsRemoteUrl;
    }

    private async Task VerifyExactOriginAsync(
        GitHubPublishRequest request,
        CancellationToken cancellationToken)
    {
        var origin = await RunAsync(
            GitCommandFactory.OriginUrl(request.Workspace),
            cancellationToken).ConfigureAwait(false);
        var pushOrigin = await RunAsync(
            GitCommandFactory.PushOriginUrl(request.Workspace),
            cancellationToken).ConfigureAwait(false);
        if (origin.ExitCode != 0 || pushOrigin.ExitCode != 0)
        {
            throw RemoteUnsafe();
        }

        var lines = OutputLines(origin);
        var pushLines = OutputLines(pushOrigin);
        if (lines.Length != 1
            || pushLines.Length != 1
            || !StringComparer.Ordinal.Equals(lines[0], request.Repository.HttpsRemoteUrl)
            || !StringComparer.Ordinal.Equals(pushLines[0], request.Repository.HttpsRemoteUrl))
        {
            throw RemoteUnsafe();
        }
    }

    private async Task<ProcessResult> RunRequiredAsync(
        CommandSpec command,
        CancellationToken cancellationToken)
    {
        var result = await RunAsync(command, cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0 ? result : throw NetworkFailure();
    }

    private async Task<ProcessResult> RunAsync(
        CommandSpec command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _processRunner.CheckPreconditionsAsync(command, cancellationToken)
            .ConfigureAwait(false);
        var result = await _processRunner.RunAsync(command, null, cancellationToken)
            .ConfigureAwait(false);
        if (result.TerminationReason == ProcessTerminationReason.Cancelled)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        if (result.TerminationReason == ProcessTerminationReason.TimedOut)
        {
            throw new InfrastructureOperationException(
                "DF-GH-002",
                "The trusted GitHub CLI operation timed out.");
        }

        if (result.IsOutputTruncated
            || result.ExitCode is null
            || !command.AllowedExitCodes.Contains(result.ExitCode.Value))
        {
            throw UnexpectedEvidence();
        }

        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private static string ParseCanonicalLogin(string[] lines)
    {
        if (lines.Length != 1)
        {
            throw UnexpectedEvidence();
        }

        var parsed = GitHubRepositoryIdentity.Create(lines[0], "identity-check");
        if (!parsed.IsValid
            || !StringComparer.Ordinal.Equals(parsed.Value.Account, lines[0]))
        {
            throw UnexpectedEvidence();
        }

        return lines[0];
    }

    private static string[] OutputLines(ProcessResult result) =>
    [
        .. result.RetainedLines
            .Where(line => line.Channel == ProcessOutputChannel.StandardOutput)
            .Select(line => line.Text.Value),
    ];

    private static GitHubAuthenticationResult CreateAuthenticationResult(
        GitHubRepositoryIdentity repository,
        GitHubAuthenticationState state)
    {
        var result = GitHubAuthenticationResult.Create(repository, state);
        return result.IsValid ? result.Value : throw UnexpectedEvidence();
    }

    private static SensitiveProcessValue LocateCurrentUserConfigDirectory()
    {
        var appData = System.Environment.GetFolderPath(
            System.Environment.SpecialFolder.ApplicationData,
            System.Environment.SpecialFolderOption.DoNotVerify);
        var value = SensitiveProcessValue.Create(Path.Combine(appData, "GitHub CLI"));
        return value.IsValid ? value.Value : throw UnexpectedEvidence();
    }

    private static SensitiveProcessValue LocateTrustedGitHubCli()
    {
        var executable = ExecutableIdentity.Create("gh");
        if (!executable.IsValid)
        {
            throw UnexpectedEvidence();
        }

        var launch = new TrustedExecutableResolver().Resolve(executable.Value);
        var value = SensitiveProcessValue.Create(launch.ExecutablePath);
        return value.IsValid ? value.Value : throw UnexpectedEvidence();
    }

    private static async Task<T> GuardAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InfrastructureOperationException exception) when (
            exception.Code.StartsWith("DF-GH-", StringComparison.Ordinal))
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw UnexpectedEvidence();
        }
    }

    private static bool IsExpectedFailure(Exception exception) => exception is
        InfrastructureOperationException
        or IOException
        or UnauthorizedAccessException
        or ArgumentException
        or InvalidOperationException;

    private static InfrastructureOperationException UnexpectedEvidence() => new(
        "DF-GH-001",
        "The GitHub CLI returned unexpected or unsafe evidence.");

    private static InfrastructureOperationException RemoteUnsafe() => new(
        "DF-GH-004",
        "The remote repository evidence does not match the reviewed publication intent.");

    private static InfrastructureOperationException NetworkFailure() => new(
        "DF-GH-005",
        "GitHub could not be reached or did not complete the requested operation.");

    private sealed class AuthenticationWorkspace : IWorkspaceFileSystem
    {
        public static AuthenticationWorkspace Instance { get; } = new();

        public WorkspaceRoot Root { get; } = WorkspaceRoot.Create(
            AppContext.BaseDirectory).Value;

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
}
