using DevForge.Application.Contracts;
using DevForge.Domain.Validation;

namespace DevForge.Infrastructure.Creation;

public sealed class WindowsProjectTargetService(
    IFileSystem fileSystem,
    WorkspaceRoot localDataRoot) : IProjectTargetPreflight, IProjectExecutionWorkspaceFactory,
    IProjectRecoveryWorkspaceFactory
{
    private const string RunsDirectoryName = "runs";
    private const string ProbePrefix = ".devforge-write-probe-";

    private readonly IFileSystem _fileSystem =
        fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly WorkspaceRoot _localDataRoot =
        localDataRoot ?? throw new ArgumentNullException(nameof(localDataRoot));

    public async Task<ValidationResult<ProjectTargetDescriptor>> PreflightAsync(
        string rootPath,
        string outputFolder,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = WorkspaceRoot.Create(rootPath);
        var directory = WorkspaceRelativePath.Create(outputFolder);
        var issues = new List<ValidationIssue>();
        if (!root.IsValid)
        {
            issues.Add(new ValidationIssue(
                "project.target.root.invalid",
                "A canonical guarded target root is required.",
                "rootPath"));
        }

        if (!directory.IsValid || outputFolder.Contains('\\'))
        {
            issues.Add(new ValidationIssue(
                "project.target.directory.invalid",
                "A single guarded target directory segment is required.",
                "outputFolder"));
        }

        if (issues.Count != 0)
        {
            return ValidationResult.Failure<ProjectTargetDescriptor>(issues);
        }

        try
        {
            var workspace = await _fileSystem.OpenWorkspaceAsync(
                root.Value,
                cancellationToken).ConfigureAwait(false);
            if (await workspace.DirectoryExistsAsync(directory.Value, cancellationToken)
                    .ConfigureAwait(false)
                || await workspace.FileExistsAsync(directory.Value, cancellationToken)
                    .ConfigureAwait(false))
            {
                return Failure<ProjectTargetDescriptor>(
                    "project.target.not-empty",
                    "The selected target already exists and will not be overwritten.",
                    "outputFolder");
            }

            if (workspace is not IAtomicWorkspaceFileSystem atomicWorkspace)
            {
                return Failure<ProjectTargetDescriptor>(
                    "project.target.atomic-create.required",
                    "The target root cannot prove exclusive write ownership.",
                    "rootPath");
            }

            var probe = Relative($"{ProbePrefix}{Guid.NewGuid():N}");
            var probeCreated = await atomicWorkspace.TryCreateDirectoryAsync(
                probe,
                cancellationToken).ConfigureAwait(false);
            if (!probeCreated)
            {
                return Failure<ProjectTargetDescriptor>(
                    "project.target.write-probe.failed",
                    "The target root could not be safely write-probed.",
                    "rootPath");
            }

            await workspace.DeleteDirectoryAsync(
                probe,
                DirectoryCleanupIntent.RecursiveRunOwned,
                CancellationToken.None).ConfigureAwait(false);

            return ProjectTargetDescriptor.Create(root.Value, directory.Value);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            return Failure<ProjectTargetDescriptor>(
                "project.target.unavailable",
                "The target root could not be safely inspected.",
                "rootPath");
        }
    }

    public async Task<ValidationResult<ProjectExecutionWorkspaces>> OpenAsync(
        ProjectTargetDescriptor target,
        string runId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCanonicalRunId(runId))
        {
            return Failure<ProjectExecutionWorkspaces>(
                "project.workspaces.run-id.invalid",
                "A canonical generated run identifier is required.",
                "runId");
        }

        IWorkspaceFileSystem? localDataWorkspace = null;
        WorkspaceRelativePath? runDirectory = null;
        var runDirectoryCreated = false;
        try
        {
            var targetParent = await _fileSystem.OpenWorkspaceAsync(
                target.ParentRoot,
                cancellationToken).ConfigureAwait(false);
            if (await targetParent.DirectoryExistsAsync(
                    target.TargetDirectory,
                    cancellationToken).ConfigureAwait(false)
                || await targetParent.FileExistsAsync(
                    target.TargetDirectory,
                    cancellationToken).ConfigureAwait(false))
            {
                return Failure<ProjectExecutionWorkspaces>(
                    "project.target.not-empty",
                    "The reviewed target now exists and will not be overwritten.",
                    "target");
            }

            localDataWorkspace = await _fileSystem.OpenWorkspaceAsync(
                _localDataRoot,
                cancellationToken).ConfigureAwait(false);
            if (localDataWorkspace is not IAtomicWorkspaceFileSystem atomicWorkspace)
            {
                return Failure<ProjectExecutionWorkspaces>(
                    "project.workspaces.atomic-create.required",
                    "Run artifacts cannot be exclusively owned.",
                    "runArtifacts");
            }

            var runsDirectory = Relative(RunsDirectoryName);
            await localDataWorkspace.CreateDirectoryAsync(
                runsDirectory,
                cancellationToken).ConfigureAwait(false);
            runDirectory = Relative($"{RunsDirectoryName}\\{runId}");
            runDirectoryCreated = await atomicWorkspace.TryCreateDirectoryAsync(
                runDirectory,
                cancellationToken).ConfigureAwait(false);
            if (!runDirectoryCreated)
            {
                return Failure<ProjectExecutionWorkspaces>(
                    "project.workspaces.run-artifacts.exists",
                    "The generated run artifact workspace is already owned.",
                    "runId");
            }

            var artifactRootPath = Path.GetFullPath(Path.Combine(
                _localDataRoot.RevealForFileSystem(),
                runDirectory.RevealForFileSystem()));
            var artifactRoot = WorkspaceRoot.Create(artifactRootPath);
            if (!artifactRoot.IsValid)
            {
                throw new InfrastructureOperationException(
                    "DF-FS-003",
                    "Run artifact containment could not be proven.");
            }

            var runArtifacts = await _fileSystem.OpenWorkspaceAsync(
                artifactRoot.Value,
                cancellationToken).ConfigureAwait(false);
            return ProjectExecutionWorkspaces.Create(target, targetParent, runArtifacts);
        }
        catch (OperationCanceledException)
        {
            if (runDirectoryCreated && localDataWorkspace is not null && runDirectory is not null)
            {
                await TryCleanupRunArtifactsAsync(localDataWorkspace, runDirectory).ConfigureAwait(false);
            }

            throw;
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            if (runDirectoryCreated && localDataWorkspace is not null && runDirectory is not null)
            {
                await TryCleanupRunArtifactsAsync(localDataWorkspace, runDirectory).ConfigureAwait(false);
            }

            return Failure<ProjectExecutionWorkspaces>(
                "project.workspaces.unavailable",
                "Guarded project execution workspaces could not be opened.",
                "target");
        }
    }

    async Task<ProjectRecoveryWorkspaces> IProjectRecoveryWorkspaceFactory.OpenAsync(
        RunCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        var target = await _fileSystem.OpenWorkspaceAsync(
            checkpoint.Target.ParentRoot,
            cancellationToken).ConfigureAwait(false);
        var artifacts = await _fileSystem.OpenWorkspaceAsync(
            checkpoint.RunArtifacts.Root,
            cancellationToken).ConfigureAwait(false);
        return new ProjectRecoveryWorkspaces(target, artifacts);
    }

    async Task<IWorkspaceFileSystem> IProjectRecoveryWorkspaceFactory.OpenFinalProjectAsync(
        RunCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        var fullPath = Path.Combine(
            checkpoint.Target.ParentRoot.RevealForFileSystem(),
            checkpoint.Target.TargetDirectory.RevealForFileSystem());
        var root = WorkspaceRoot.Create(fullPath);
        if (!root.IsValid)
        {
            throw new InfrastructureOperationException(
                "DF-FS-003",
                "The finalized project root could not be proven.");
        }

        return await _fileSystem.OpenWorkspaceAsync(root.Value, cancellationToken).ConfigureAwait(false);
    }

    LocalReadyPresentation IProjectRecoveryWorkspaceFactory.DescribeLocalReady(
        RunCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        var targetPath = Path.GetFullPath(Path.Combine(
            checkpoint.Target.ParentRoot.RevealForFileSystem(),
            checkpoint.Target.TargetDirectory.RevealForFileSystem()));
        var reportDirectory = Path.Combine(
            checkpoint.RunArtifacts.Root.RevealForFileSystem(),
            "reports");
        return LocalReadyPresentation.Create(
            targetPath,
            [
                Path.Combine(reportDirectory, $"{checkpoint.Run.Id}.json"),
                Path.Combine(reportDirectory, $"{checkpoint.Run.Id}.md"),
            ]).Value;
    }

    private static async Task TryCleanupRunArtifactsAsync(
        IWorkspaceFileSystem workspace,
        WorkspaceRelativePath runDirectory)
    {
        try
        {
            await workspace.DeleteDirectoryAsync(
                runDirectory,
                DirectoryCleanupIntent.RecursiveRunOwned,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            // The caller receives a scrubbed failure and can surface cleanup debt without exposing paths.
        }
    }

    private static bool IsCanonicalRunId(string? value)
    {
        const string prefix = "run-";
        return value is not null
            && value.Length == prefix.Length + 32
            && value.StartsWith(prefix, StringComparison.Ordinal)
            && value.AsSpan(prefix.Length).ToArray().All(character =>
                character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    private static WorkspaceRelativePath Relative(string value)
    {
        var result = WorkspaceRelativePath.Create(value);
        if (!result.IsValid)
        {
            throw new InvalidOperationException("A trusted creation path constant is invalid.");
        }

        return result.Value;
    }

    private static ValidationResult<T> Failure<T>(
        string code,
        string message,
        string location)
    {
        return ValidationResult.Failure<T>([new ValidationIssue(code, message, location)]);
    }

    private static bool IsExpectedFailure(Exception exception)
    {
        return exception is InfrastructureOperationException
            or IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException;
    }
}
