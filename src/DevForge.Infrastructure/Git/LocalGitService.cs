using System.Collections.Immutable;
using System.Text;
using DevForge.Application.Contracts;
using DevForge.Domain.Projects;
using DevForge.Infrastructure.Execution;
using DevForge.Infrastructure.FileSystem;

namespace DevForge.Infrastructure.Git;

public sealed class LocalGitService(
    IProcessRunner processRunner,
    ISecretScanner secretScanner) : IGitService, IPublicationGitService
{
    private const int MaximumGitConfigBytes = 32 * 1024;
    private const int MaximumReflogBytes = 64 * 1024;
    private static readonly UTF8Encoding _strictUtf8 = new(false, true);
    private static readonly WorkspaceRelativePath _gitConfig = PathOf(".git\\config");
    private readonly IProcessRunner _processRunner = processRunner
        ?? throw new ArgumentNullException(nameof(processRunner));
    private readonly ISecretScanner _secretScanner = secretScanner
        ?? throw new ArgumentNullException(nameof(secretScanner));

    public Task<GitRepositoryReceipt> BootstrapAsync(
        GitBootstrapRequest request,
        CancellationToken cancellationToken)
    {
        return BootstrapAsync(request, NullGitPublicationProgress.Instance, cancellationToken);
    }

    public Task<GitRepositoryReceipt> BootstrapAsync(
        GitBootstrapRequest request,
        IGitPublicationProgress progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(progress);
        return GuardAsync(
            () => BootstrapCoreAsync(request, progress, cancellationToken),
            cancellationToken);
    }

    public Task<GitRepositoryReceipt> VerifyAsync(
        GitVerificationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return GuardAsync(
            () => VerifyCoreAsync(request, cancellationToken),
            cancellationToken);
    }

    private async Task<GitRepositoryReceipt> BootstrapCoreAsync(
        GitBootstrapRequest request,
        IGitPublicationProgress progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await RunRequiredAsync(
            GitCommandFactory.Version(request.Workspace),
            cancellationToken).ConfigureAwait(false);
        var tree = await CaptureAndScanAsync(
            request.Workspace,
            request.FinalTreeDigest,
            cancellationToken).ConfigureAwait(false);
        if (!tree.HasRootGit)
        {
            await RunRequiredAsync(
                GitCommandFactory.Initialize(request.Workspace),
                cancellationToken).ConfigureAwait(false);
            tree = await CaptureAndScanAsync(
                request.Workspace,
                request.FinalTreeDigest,
                cancellationToken).ConfigureAwait(false);
        }

        await VerifyRepositoryMetadataAsync(request.Workspace, expectedOriginUrl: null, cancellationToken)
            .ConfigureAwait(false);
        await progress.RepositoryInitializedAsync(CancellationToken.None).ConfigureAwait(false);
        var head = await ReadHeadAsync(
            request.Workspace,
            allowMissing: true,
            cancellationToken).ConfigureAwait(false);
        if (head is null)
        {
            var branches = await ReadLinesAsync(
                GitCommandFactory.Branches(request.Workspace),
                cancellationToken).ConfigureAwait(false);
            var currentBranch = await ReadSingleLineAsync(
                GitCommandFactory.CurrentBranch(request.Workspace),
                cancellationToken).ConfigureAwait(false);
            if (branches.Length != 0
                || !StringComparer.Ordinal.Equals(currentBranch, "main"))
            {
                throw UnsafeRepository();
            }

            await CaptureAndScanAsync(
                request.Workspace,
                request.FinalTreeDigest,
                cancellationToken).ConfigureAwait(false);
            await RunRequiredAsync(
                GitCommandFactory.AddAll(request.Workspace),
                cancellationToken).ConfigureAwait(false);
            await CaptureAndScanAsync(
                request.Workspace,
                request.FinalTreeDigest,
                cancellationToken).ConfigureAwait(false);
            await RunRequiredAsync(
                GitCommandFactory.Commit(request.Workspace),
                cancellationToken).ConfigureAwait(false);
            head = await ReadHeadAsync(
                request.Workspace,
                allowMissing: false,
                cancellationToken).ConfigureAwait(false);
        }

        await VerifyCommittedRepositoryAsync(
            request.Workspace,
            head ?? throw UnsafeRepository(),
            request.FinalTreeDigest,
            request.BranchPolicy,
            allowIncompleteBranches: true,
            expectedOriginUrl: null,
            cancellationToken).ConfigureAwait(false);
        var branchHeads = await ReadBranchHeadsAsync(request.Workspace, cancellationToken)
            .ConfigureAwait(false);
        if (request.BranchPolicy == GitBranchPolicy.MainAndDevelop
            && !branchHeads.ContainsKey("develop"))
        {
            await CaptureAndScanAsync(
                request.Workspace,
                request.FinalTreeDigest,
                cancellationToken).ConfigureAwait(false);
            await RunRequiredAsync(
                GitCommandFactory.CreateDevelop(request.Workspace, head),
                cancellationToken).ConfigureAwait(false);
        }

        var current = await ReadSingleLineAsync(
            GitCommandFactory.CurrentBranch(request.Workspace),
            cancellationToken).ConfigureAwait(false);
        if (!StringComparer.Ordinal.Equals(current, "main"))
        {
            throw UnsafeRepository();
        }

        return await VerifyCommittedRepositoryAsync(
            request.Workspace,
            head,
            request.FinalTreeDigest,
            request.BranchPolicy,
            allowIncompleteBranches: false,
            expectedOriginUrl: null,
            cancellationToken).ConfigureAwait(false);
    }

    private sealed class NullGitPublicationProgress : IGitPublicationProgress
    {
        public static NullGitPublicationProgress Instance { get; } = new();

        public Task RepositoryInitializedAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private async Task<GitRepositoryReceipt> VerifyCoreAsync(
        GitVerificationRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await RunRequiredAsync(
            GitCommandFactory.Version(request.Workspace),
            cancellationToken).ConfigureAwait(false);
        var tree = await CaptureAndScanAsync(
            request.Workspace,
            request.FinalTreeDigest,
            cancellationToken).ConfigureAwait(false);
        if (!tree.HasRootGit)
        {
            throw UnsafeRepository();
        }

        await VerifyRepositoryMetadataAsync(
                request.Workspace,
                request.ExpectedOriginUrl,
                cancellationToken)
            .ConfigureAwait(false);
        var head = await ReadHeadAsync(
            request.Workspace,
            allowMissing: false,
            cancellationToken).ConfigureAwait(false);
        if (!StringComparer.Ordinal.Equals(head, request.InitialCommitId))
        {
            throw UnsafeRepository();
        }

        return await VerifyCommittedRepositoryAsync(
            request.Workspace,
            head,
            request.FinalTreeDigest,
            request.BranchPolicy,
            allowIncompleteBranches: false,
            request.ExpectedOriginUrl,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<GitRepositoryReceipt> VerifyCommittedRepositoryAsync(
        IWorkspaceFileSystem workspace,
        string head,
        string finalTreeDigest,
        GitBranchPolicy branchPolicy,
        bool allowIncompleteBranches,
        string? expectedOriginUrl,
        CancellationToken cancellationToken)
    {
        if (!PublicationSnapshot.IsObjectId(head))
        {
            throw UnsafeRepository();
        }

        await VerifyRepositoryMetadataAsync(workspace, expectedOriginUrl, cancellationToken)
            .ConfigureAwait(false);
        var projectTree = await CaptureAndScanAsync(
            workspace,
            finalTreeDigest,
            cancellationToken).ConfigureAwait(false);
        var metadata = await GitCommitObjectReader.ReadAsync(
            workspace,
            head,
            cancellationToken).ConfigureAwait(false);
        if (metadata.ParentCommitId is not null
            || !StringComparer.Ordinal.Equals(metadata.AuthorName, GitCommandFactory.AuthorName)
            || !StringComparer.Ordinal.Equals(metadata.AuthorEmail, GitCommandFactory.AuthorEmail)
            || !StringComparer.Ordinal.Equals(metadata.CommitterName, GitCommandFactory.AuthorName)
            || !StringComparer.Ordinal.Equals(metadata.CommitterEmail, GitCommandFactory.AuthorEmail)
            || !StringComparer.Ordinal.Equals(metadata.Subject, GitCommandFactory.BootstrapMessage))
        {
            throw UnsafeRepository();
        }

        var treeEvidence = await GitTreeVerifier.VerifyAsync(
            workspace,
            projectTree,
            metadata.TreeId,
            cancellationToken).ConfigureAwait(false);
        var reachableObjects = treeEvidence.ObjectIds.Add(head);
        var looseObjects = await ReadLooseObjectIdsAsync(workspace, cancellationToken)
            .ConfigureAwait(false);
        if (!reachableObjects.SetEquals(looseObjects))
        {
            throw UnsafeRepository();
        }


        var branchHeads = await ReadBranchHeadsAsync(workspace, cancellationToken)
            .ConfigureAwait(false);
        var allowed = branchPolicy == GitBranchPolicy.Main
            ? new[] { "main" }
            : ["main", "develop"];
        if (!branchHeads.ContainsKey("main")
            || branchHeads.Any(pair => !allowed.Contains(pair.Key, StringComparer.Ordinal)
                || !StringComparer.Ordinal.Equals(pair.Value, head))
            || !allowIncompleteBranches && branchHeads.Count != allowed.Length)
        {
            throw UnsafeRepository();
        }

        var current = await ReadSingleLineAsync(
            GitCommandFactory.CurrentBranch(workspace),
            cancellationToken).ConfigureAwait(false);
        if (current != "main")
        {
            throw UnsafeRepository();
        }

        await VerifyAuxiliaryMetadataAsync(
            workspace,
            head,
            treeEvidence,
            branchHeads.ContainsKey("develop"),
            cancellationToken).ConfigureAwait(false);

        var status = await ReadLinesAsync(
            GitCommandFactory.Status(workspace),
            cancellationToken).ConfigureAwait(false);
        if (status.Length != 0)
        {
            throw UnsafeRepository();
        }

        var remotes = await ReadLinesAsync(
            GitCommandFactory.Remotes(workspace),
            cancellationToken).ConfigureAwait(false);
        if (expectedOriginUrl is null)
        {
            if (remotes.Length != 0)
            {
                throw UnsafeRepository();
            }
        }
        else
        {
            var origin = await ReadLinesAsync(
                GitCommandFactory.OriginUrl(workspace),
                cancellationToken).ConfigureAwait(false);
            var pushOrigin = await ReadLinesAsync(
                GitCommandFactory.PushOriginUrl(workspace),
                cancellationToken).ConfigureAwait(false);
            if (remotes.Length != 1
                || !StringComparer.Ordinal.Equals(remotes[0], "origin")
                || origin.Length != 1
                || pushOrigin.Length != 1
                || !StringComparer.Ordinal.Equals(origin[0], expectedOriginUrl)
                || !StringComparer.Ordinal.Equals(pushOrigin[0], expectedOriginUrl))
            {
                throw UnsafeRepository();
            }
        }

        var receiptBranches = allowIncompleteBranches && !branchHeads.ContainsKey("develop")
            ? new[] { "main" }
            : allowed;
        var receipt = GitRepositoryReceipt.Create(
            head,
            allowIncompleteBranches && receiptBranches.Length == 1
                ? GitBranchPolicy.Main
                : branchPolicy,
            receiptBranches,
            finalTreeDigest);
        return receipt.IsValid ? receipt.Value : throw UnsafeRepository();
    }

    private async Task<CanonicalProjectTreeSnapshot> CaptureAndScanAsync(
        IWorkspaceFileSystem workspace,
        string expectedDigest,
        CancellationToken cancellationToken)
    {
        var hasRootGit = await workspace.DirectoryExistsAsync(
            PathOf(".git"),
            cancellationToken).ConfigureAwait(false);
        var tree = await CanonicalProjectTree.CaptureAsync(
            workspace,
            allowOwnedRootGit: hasRootGit,
            cancellationToken).ConfigureAwait(false);
        if (!StringComparer.Ordinal.Equals(tree.Digest, expectedDigest))
        {
            throw new InfrastructureOperationException(
                "DF-GIT-004",
                "The finalized project tree changed before Git publication.");
        }

        if (tree.AllFiles.Length != 0)
        {
            SecretScanResult scan;
            try
            {
                scan = await _secretScanner.ScanAsync(
                    SecretScanRequest.ExplicitPaths(workspace, tree.AllFiles).Value,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (IsExpectedFailure(exception))
            {
                throw new InfrastructureOperationException(
                    "DF-GIT-003",
                    "The finalized project could not be scanned safely before Git publication.");
            }

            if (scan.Findings.Length != 0)
            {
                throw new InfrastructureOperationException(
                    "DF-GIT-003",
                    "Credential-shaped content blocks Git publication.");
            }
        }

        return tree;
    }

    private static async Task VerifyRepositoryMetadataAsync(
        IWorkspaceFileSystem workspace,
        string? expectedOriginUrl,
        CancellationToken cancellationToken)
    {
        if (workspace is not IBoundedWorkspaceEnumerator bounded)
        {
            throw UnsafeRepository();
        }

        var enumeration = await bounded.EnumerateTreeBoundedAsync(
            excludedRootDirectory: null,
            AtomicProjectFinalizer.MaximumFileCount * 3,
            AtomicProjectFinalizer.MaximumDirectoryCount * 2,
            AtomicProjectFinalizer.MaximumPathDepth * 2,
            cancellationToken).ConfigureAwait(false);
        if (enumeration.LimitExceeded
            || enumeration.Files.Any(path => path.Value.StartsWith(
                    ".git\\",
                    StringComparison.OrdinalIgnoreCase)
                && !IsAllowedGitFile(path.Value))
            || enumeration.Directories.Any(path => path.Value.Equals(
                    ".git",
                    StringComparison.OrdinalIgnoreCase)
                || path.Value.StartsWith(".git\\", StringComparison.OrdinalIgnoreCase)
                ? !IsAllowedGitDirectory(path.Value)
                : false))
        {
            throw UnsafeRepository();
        }

        var forbiddenFiles = new[]
        {
            ".git\\commondir", ".git\\gitdir", ".git\\shallow", ".git\\config.worktree",
            ".git\\MERGE_HEAD", ".git\\CHERRY_PICK_HEAD", ".git\\REVERT_HEAD",
            ".git\\BISECT_LOG", ".git\\objects\\info\\alternates", ".git\\packed-refs",
        };
        var forbiddenDirectories = new[]
        {
            ".git\\hooks", ".git\\modules", ".git\\worktrees", ".git\\svn",
            ".git\\rebase-merge", ".git\\rebase-apply", ".git\\sequencer",
            ".git\\refs\\remotes",
        };
        foreach (var path in forbiddenFiles)
        {
            if (await workspace.FileExistsAsync(PathOf(path), cancellationToken)
                .ConfigureAwait(false))
            {
                throw UnsafeRepository();
            }
        }

        foreach (var path in forbiddenDirectories)
        {
            if (await workspace.DirectoryExistsAsync(PathOf(path), cancellationToken)
                .ConfigureAwait(false))
            {
                throw UnsafeRepository();
            }
        }

        var config = await ReadBoundedTextAsync(
            workspace,
            _gitConfig,
            MaximumGitConfigBytes,
            cancellationToken).ConfigureAwait(false);
        ValidateLocalConfig(config, expectedOriginUrl);
    }

    private static async Task<ImmutableHashSet<string>> ReadLooseObjectIdsAsync(
        IWorkspaceFileSystem workspace,
        CancellationToken cancellationToken)
    {
        if (workspace is not IBoundedWorkspaceEnumerator bounded)
        {
            throw UnsafeRepository();
        }

        var enumeration = await bounded.EnumerateTreeBoundedAsync(
            excludedRootDirectory: null,
            AtomicProjectFinalizer.MaximumFileCount * 3,
            AtomicProjectFinalizer.MaximumDirectoryCount * 2,
            AtomicProjectFinalizer.MaximumPathDepth * 2,
            cancellationToken).ConfigureAwait(false);
        if (enumeration.LimitExceeded)
        {
            throw UnsafeRepository();
        }

        const string prefix = ".git\\objects\\";
        var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var path in enumeration.Files.Where(path =>
                     path.Value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            var remainder = path.Value[prefix.Length..].Replace("\\", string.Empty, StringComparison.Ordinal);
            if (!PublicationSnapshot.IsObjectId(remainder) || !builder.Add(remainder))
            {
                throw UnsafeRepository();
            }
        }

        return builder.ToImmutable();
    }

    private static async Task VerifyReflogsAsync(
        IWorkspaceFileSystem workspace,
        string head,
        bool hasDevelop,
        CancellationToken cancellationToken)
    {
        await VerifyReflogAsync(
            workspace,
            PathOf(".git\\logs\\HEAD"),
            head,
            "commit (initial): " + GitCommandFactory.BootstrapMessage,
            cancellationToken).ConfigureAwait(false);
        await VerifyReflogAsync(
            workspace,
            PathOf(".git\\logs\\refs\\heads\\main"),
            head,
            "commit (initial): " + GitCommandFactory.BootstrapMessage,
            cancellationToken).ConfigureAwait(false);
        var develop = PathOf(".git\\logs\\refs\\heads\\develop");
        var developExists = await workspace.FileExistsAsync(develop, cancellationToken)
            .ConfigureAwait(false);
        if (developExists != hasDevelop)
        {
            throw UnsafeRepository();
        }

        if (developExists)
        {
            await VerifyReflogAsync(
                workspace,
                develop,
                head,
                "branch: Created from " + head,
                cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task VerifyAuxiliaryMetadataAsync(
        IWorkspaceFileSystem workspace,
        string head,
        GitTreeEvidence treeEvidence,
        bool hasDevelop,
        CancellationToken cancellationToken)
    {
        var editMessage = await ReadBoundedTextAsync(
            workspace,
            PathOf(".git\\COMMIT_EDITMSG"),
            4096,
            cancellationToken).ConfigureAwait(false);
        if (!StringComparer.Ordinal.Equals(
                editMessage.Replace("\r\n", "\n", StringComparison.Ordinal),
                GitCommandFactory.BootstrapMessage + "\n"))
        {
            throw UnsafeRepository();
        }

        await GitIndexVerifier.VerifyAsync(
            workspace,
            head.Length,
            treeEvidence.Blobs,
            treeEvidence.Trees,
            cancellationToken).ConfigureAwait(false);
        await VerifyReflogsAsync(workspace, head, hasDevelop, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task VerifyReflogAsync(
        IWorkspaceFileSystem workspace,
        WorkspaceRelativePath path,
        string head,
        string expectedAction,
        CancellationToken cancellationToken)
    {
        var content = await ReadBoundedTextAsync(
            workspace,
            path,
            MaximumReflogBytes,
            cancellationToken).ConfigureAwait(false);
        var lines = content.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length != 1)
        {
            throw UnsafeRepository();
        }

        var fields = lines[0].Split(' ', 3);
        var identityPrefix = GitCommandFactory.AuthorName
            + " <" + GitCommandFactory.AuthorEmail + "> ";
        if (fields.Length != 3
            || fields[0].Length != head.Length
            || fields[0].Any(character => character != '0')
            || !StringComparer.Ordinal.Equals(fields[1], head)
            || !fields[2].StartsWith(identityPrefix, StringComparison.Ordinal))
        {
            throw UnsafeRepository();
        }


        var tail = fields[2][identityPrefix.Length..];
        var tab = tail.IndexOf('\t');
        var timeFields = tab < 0 ? [] : tail[..tab].Split(' ');
        if (tab <= 0
            || timeFields.Length != 2
            || timeFields[0].Length == 0
            || !timeFields[0].All(char.IsAsciiDigit)
            || timeFields[1].Length != 5
            || timeFields[1][0] is not ('+' or '-')
            || !timeFields[1][1..].All(char.IsAsciiDigit)
            || !StringComparer.Ordinal.Equals(tail[(tab + 1)..], expectedAction))
        {
            throw UnsafeRepository();
        }
    }

    private static bool IsAllowedGitFile(string path)
    {
        if (path is ".git\\HEAD"
            or ".git\\config"
            or ".git\\index"
            or ".git\\COMMIT_EDITMSG"
            or ".git\\logs\\HEAD"
            or ".git\\logs\\refs\\heads\\main"
            or ".git\\logs\\refs\\heads\\develop"
            or ".git\\refs\\heads\\main"
            or ".git\\refs\\heads\\develop")
        {
            return true;
        }

        const string prefix = ".git\\objects\\";
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var remainder = path[prefix.Length..].Split('\\');
        return remainder.Length == 2
            && remainder[0].Length == 2
            && remainder[1].Length is 38 or 62
            && remainder.SelectMany(value => value).All(character =>
                character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    private static bool IsAllowedGitDirectory(string path)
    {
        if (path is ".git"
            or ".git\\branches"
            or ".git\\objects"
            or ".git\\objects\\info"
            or ".git\\objects\\pack"
            or ".git\\refs"
            or ".git\\refs\\heads"
            or ".git\\refs\\tags"
            or ".git\\logs"
            or ".git\\logs\\refs"
            or ".git\\logs\\refs\\heads")
        {
            return true;
        }

        const string prefix = ".git\\objects\\";
        var remainder = path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? path[prefix.Length..]
            : string.Empty;
        return remainder.Length == 2
            && remainder.All(character =>
                character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    private static void ValidateLocalConfig(string content, string? expectedOriginUrl)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? section = null;
        foreach (var raw in content.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                var parsedSection = line[1..^1].Trim();
                section = parsedSection.Equals("core", StringComparison.OrdinalIgnoreCase)
                    ? "core"
                    : parsedSection.Equals("remote \"origin\"", StringComparison.OrdinalIgnoreCase)
                        ? "remote.origin"
                        : null;
                if (section is null)
                {
                    throw UnsafeRepository();
                }
                continue;
            }

            var equals = line.IndexOf('=');
            if (section is null || equals <= 0)
            {
                throw UnsafeRepository();
            }

            var key = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim();
            if (!values.TryAdd(section + "." + key, value))
            {
                throw UnsafeRepository();
            }
        }

        var required = new Dictionary<string, Func<string, bool>>(StringComparer.OrdinalIgnoreCase)
        {
            ["core.repositoryformatversion"] = value => value == "0",
            ["core.filemode"] = IsBoolean,
            ["core.bare"] = value => value.Equals("false", StringComparison.OrdinalIgnoreCase),
            ["core.logallrefupdates"] = value => value.Equals("true", StringComparison.OrdinalIgnoreCase),
            ["core.symlinks"] = IsBoolean,
            ["core.ignorecase"] = IsBoolean,
        };
        var mandatory = new[]
        {
            "core.repositoryformatversion", "core.filemode", "core.bare", "core.logallrefupdates",
        };
        if (expectedOriginUrl is not null)
        {
            required["remote.origin.url"] = value =>
                StringComparer.Ordinal.Equals(value, expectedOriginUrl);
            required["remote.origin.fetch"] = value => StringComparer.Ordinal.Equals(
                value,
                "+refs/heads/*:refs/remotes/origin/*");
            mandatory = [.. mandatory, "remote.origin.url", "remote.origin.fetch"];
        }
        if (values.Any(pair => !required.TryGetValue(pair.Key, out var validator)
                || !validator(pair.Value))
            || mandatory.Any(key => !values.ContainsKey(key)))
        {
            throw UnsafeRepository();
        }
    }

    private async Task<ImmutableDictionary<string, string>> ReadBranchHeadsAsync(
        IWorkspaceFileSystem workspace,
        CancellationToken cancellationToken)
    {
        var lines = await ReadLinesAsync(
            GitCommandFactory.BranchHeads(workspace),
            cancellationToken).ConfigureAwait(false);
        var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var separator = line.IndexOf(' ');
            if (separator <= 0
                || !builder.TryAdd(line[..separator], line[(separator + 1)..]))
            {
                throw UnsafeRepository();
            }
        }

        return builder.ToImmutable();
    }

    private async Task<string?> ReadHeadAsync(
        IWorkspaceFileSystem workspace,
        bool allowMissing,
        CancellationToken cancellationToken)
    {
        var result = await RunAsync(
            GitCommandFactory.Head(workspace, allowMissing),
            cancellationToken).ConfigureAwait(false);
        if (allowMissing && result.ExitCode == 128)
        {
            return null;
        }

        var lines = OutputLines(result);
        return lines.Length == 1 ? lines[0] : throw UnsafeRepository();
    }

    private async Task<string> ReadSingleLineAsync(
        CommandSpec command,
        CancellationToken cancellationToken)
    {
        var lines = await ReadLinesAsync(command, cancellationToken).ConfigureAwait(false);
        return lines.Length == 1 ? lines[0] : throw UnsafeRepository();
    }

    private async Task<string[]> ReadLinesAsync(
        CommandSpec command,
        CancellationToken cancellationToken) =>
        OutputLines(await RunRequiredAsync(command, cancellationToken).ConfigureAwait(false));

    private async Task<ProcessResult> RunRequiredAsync(
        CommandSpec command,
        CancellationToken cancellationToken)
    {
        var result = await RunAsync(command, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new InfrastructureOperationException(
                "DF-GIT-001",
                "The trusted Git operation failed safely.");
        }

        return result;
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
                "DF-GIT-002",
                "The trusted Git operation timed out.");
        }

        if (result.IsOutputTruncated || !command.AllowedExitCodes.Contains(result.ExitCode!.Value))
        {
            throw new InfrastructureOperationException(
                "DF-GIT-001",
                "The trusted Git operation returned unexpected evidence.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private static string[] OutputLines(ProcessResult result) =>
    [
        .. result.RetainedLines
            .Where(line => line.Channel == ProcessOutputChannel.StandardOutput)
            .Select(line => line.Text.Value),
    ];

    private static async Task<string> ReadBoundedTextAsync(
        IWorkspaceFileSystem workspace,
        WorkspaceRelativePath path,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var input = await workspace.OpenReadAsync(path, cancellationToken)
            .ConfigureAwait(false);
        if (input.Length < 0 || input.Length > maximumBytes)
        {
            throw UnsafeRepository();
        }

        using var reader = new StreamReader(
            input,
            _strictUtf8,
            detectEncodingFromByteOrderMarks: true,
            leaveOpen: true);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
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
            exception.Code.StartsWith("DF-GIT-", StringComparison.Ordinal))
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InfrastructureOperationException(
                "DF-GIT-001",
                "The local repository could not be prepared safely.");
        }
    }

    private static bool IsExpectedFailure(Exception exception) => exception is
        InfrastructureOperationException
        or IOException
        or UnauthorizedAccessException
        or ArgumentException
        or InvalidOperationException
        or DecoderFallbackException;

    private static bool IsBoolean(string value) =>
        value.Equals("true", StringComparison.OrdinalIgnoreCase)
        || value.Equals("false", StringComparison.OrdinalIgnoreCase);

    private static WorkspaceRelativePath PathOf(string value) =>
        WorkspaceRelativePath.Create(value).Value;

    private static InfrastructureOperationException UnsafeRepository() => new(
        "DF-GIT-001",
        "The local repository does not match the reviewed DevForge bootstrap state.");
}
