using System.Security.Cryptography;
using DevForge.Domain.Validation;

namespace DevForge.Application.Contracts;

public enum PublicationMutationMode
{
    Normal = 1,
    SafeReadOnly = 2,
}

public sealed class PublicationRequest
{
    private PublicationRequest(string runId, PublicationMutationMode mutationMode)
    {
        RunId = runId;
        MutationMode = mutationMode;
    }

    public string RunId { get; }

    public PublicationMutationMode MutationMode { get; }

    public static ValidationResult<PublicationRequest> Create(
        string? runId,
        PublicationMutationMode mutationMode)
    {
        var issues = new List<ValidationIssue>();
        if (!IsCanonicalRunId(runId))
        {
            issues.Add(new ValidationIssue(
                "publication.request.run-id.invalid",
                "A canonical generated run identifier is required.",
                "runId"));
        }

        if (!Enum.IsDefined(mutationMode))
        {
            issues.Add(new ValidationIssue(
                "publication.request.mode.invalid",
                "The publication mutation mode is not defined.",
                "mutationMode"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new PublicationRequest(runId!, mutationMode))
            : ValidationResult.Failure<PublicationRequest>(issues);
    }

    private static bool IsCanonicalRunId(string? value)
    {
        const string prefix = "run-";
        return value is { Length: 36 }
            && value.StartsWith(prefix, StringComparison.Ordinal)
            && value.AsSpan(prefix.Length).ToArray().All(character =>
                character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }
}

public interface IPublicationLease : IAsyncDisposable
{
}

public interface IPublicationLeaseProvider
{
    Task<ExecutionOperationResult<IPublicationLease>> AcquireAsync(
        string runId,
        CancellationToken cancellationToken);
}

public sealed record ProjectPublicationWorkspaces(
    IWorkspaceFileSystem FinalProject,
    IWorkspaceFileSystem RunArtifacts);

public interface IProjectPublicationWorkspaceFactory
{
    Task<ExecutionOperationResult<ProjectPublicationWorkspaces>> OpenAsync(
        RunCheckpoint checkpoint,
        CancellationToken cancellationToken);
}

public sealed class PublicationReceiptWriteRequest
{
    public const int MaximumBodyBytes = 1024 * 1024;

    private PublicationReceiptWriteRequest(
        IWorkspaceFileSystem workspace,
        WorkspaceRelativePath path,
        ReadOnlyMemory<byte> body,
        string bodyDigest,
        PublicationReceiptAccessMode accessMode)
    {
        Workspace = workspace;
        Path = path;
        Body = body;
        BodyDigest = bodyDigest;
        AccessMode = accessMode;
    }

    public IWorkspaceFileSystem Workspace { get; }

    public WorkspaceRelativePath Path { get; }

    public ReadOnlyMemory<byte> Body { get; }

    public string BodyDigest { get; }

    public PublicationReceiptAccessMode AccessMode { get; }

    public static ValidationResult<PublicationReceiptWriteRequest> Create(
        IWorkspaceFileSystem? workspace,
        WorkspaceRelativePath? path,
        ReadOnlyMemory<byte> body,
        string? bodyDigest,
        PublicationReceiptAccessMode accessMode = PublicationReceiptAccessMode.WriteOrVerify)
    {
        var issues = new List<ValidationIssue>();
        if (workspace is null)
        {
            issues.Add(new ValidationIssue(
                "publication.receipt.workspace.required",
                "A guarded run-artifact workspace is required.",
                "workspace"));
        }

        if (path is null)
        {
            issues.Add(new ValidationIssue(
                "publication.receipt.path.required",
                "A guarded publication receipt path is required.",
                "path"));
        }

        if (body.IsEmpty || body.Length > MaximumBodyBytes)
        {
            issues.Add(new ValidationIssue(
                "publication.receipt.body.invalid",
                "The canonical publication receipt body must be nonempty and bounded.",
                "body"));
        }

        var computed = body.IsEmpty
            ? null
            : $"sha256:{Convert.ToHexStringLower(SHA256.HashData(body.Span))}";
        if (!ExecutionContractValidation.IsCanonicalDigest(bodyDigest)
            || !StringComparer.Ordinal.Equals(bodyDigest, computed))
        {
            issues.Add(new ValidationIssue(
                "publication.receipt.body-digest.mismatch",
                "The receipt body digest must match the exact caller-owned bytes.",
                "bodyDigest"));
        }

        if (!Enum.IsDefined(accessMode))
        {
            issues.Add(new ValidationIssue(
                "publication.receipt.access-mode.invalid",
                "The publication receipt access mode is not defined.",
                "accessMode"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new PublicationReceiptWriteRequest(
                workspace!, path!, body.ToArray(), bodyDigest!, accessMode))
            : ValidationResult.Failure<PublicationReceiptWriteRequest>(issues);
    }
}

public enum PublicationReceiptAccessMode
{
    WriteOrVerify = 1,
    VerifyOnly = 2,
}

public sealed record PublicationReceiptWriteResult(
    WorkspaceRelativePath Path,
    string BodyDigest,
    bool AdoptedExisting);

public interface IPublicationReceiptStore
{
    Task<ExecutionOperationResult<PublicationReceiptWriteResult>> WriteOrVerifyAsync(
        PublicationReceiptWriteRequest request,
        CancellationToken cancellationToken);
}

public interface IPublicationNonceGenerator
{
    string CreateOwnershipNonce();
}

public interface IProjectPublicationCoordinator
{
    Task<ExecutionOperationResult<RunCheckpoint>> PublishAsync(
        PublicationRequest request,
        CancellationToken cancellationToken);
}

public sealed class ProjectPublicationOutcome
{
    private ProjectPublicationOutcome(
        RunCheckpoint checkpoint,
        DevForge.Domain.Diagnostics.DevForgeError? error)
    {
        Checkpoint = checkpoint;
        Error = error;
    }

    public RunCheckpoint Checkpoint { get; }

    public DevForge.Domain.Diagnostics.DevForgeError? Error { get; }

    public bool IsCompleted => Checkpoint.Run.Status == DevForge.Domain.Runs.RunStatus.Completed;

    public static ValidationResult<ProjectPublicationOutcome> Create(
        RunCheckpoint? checkpoint,
        DevForge.Domain.Diagnostics.DevForgeError? error) => checkpoint is null
        ? ValidationResult.Failure<ProjectPublicationOutcome>(
        [
            new ValidationIssue(
                "publication.outcome.checkpoint.required",
                "An authoritative publication checkpoint is required.",
                "checkpoint"),
        ])
        : ValidationResult.Success(new ProjectPublicationOutcome(checkpoint, error));
}

public interface IProjectPublicationWorkflow
{
    Task<ExecutionOperationResult<ProjectPublicationOutcome>> CompleteAsync(
        string runId,
        PublicationMutationMode mutationMode,
        CancellationToken cancellationToken);
}

public interface IGitPublicationProgress
{
    Task RepositoryInitializedAsync(CancellationToken cancellationToken);
}

public interface IPublicationGitService
{
    Task<GitRepositoryReceipt> BootstrapAsync(
        GitBootstrapRequest request,
        IGitPublicationProgress progress,
        CancellationToken cancellationToken);

    Task<GitRepositoryReceipt> VerifyAsync(
        GitVerificationRequest request,
        CancellationToken cancellationToken);
}

public interface IGitHubPublicationProgress
{
    Task RemoteCreatedAsync(CancellationToken cancellationToken);
}

public interface IPublicationGitHubService
{
    Task<GitHubPublishResult> PublishAsync(
        GitHubPublishRequest request,
        IGitHubPublicationProgress progress,
        CancellationToken cancellationToken);

    Task<GitHubPublishResult> VerifyAsync(
        GitHubPublishRequest request,
        CancellationToken cancellationToken);
}
