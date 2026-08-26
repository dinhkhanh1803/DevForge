using System.Collections.Immutable;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Execution;
using DevForge.Domain.Privacy;
using DevForge.Domain.Projects;
using DevForge.Domain.Runs;
using DevForge.Domain.Validation;

namespace DevForge.Application.Contracts;

public enum ExecutionMode
{
    Fresh = 1,
    Resume = 2,
    ManualRetry = 3,
}

public enum FinalizationState
{
    NotStarted = 1,
    IntentPersisted = 2,
    Succeeded = 3,
    Failed = 4,
}

public enum ReportPersistenceState
{
    NotStarted = 1,
    Succeeded = 2,
    Failed = 3,
}

public enum ExecutionEvidenceKind
{
    Step = 1,
    Validator = 2,
    SecretScan = 3,
}

public enum ExecutionEvidenceStatus
{
    Passed = 1,
    Warning = 2,
    Failed = 3,
}

public sealed class ExecutionRequest
{
    private ExecutionRequest(
        PlannedProject plannedProject,
        ProjectRun run,
        IWorkspaceFileSystem targetParentWorkspace,
        WorkspaceRelativePath targetDirectory,
        IWorkspaceFileSystem runArtifactWorkspace,
        ExecutionMode mode)
    {
        PlannedProject = plannedProject;
        Run = run;
        TargetParentWorkspace = targetParentWorkspace;
        TargetDirectory = targetDirectory;
        RunArtifactWorkspace = runArtifactWorkspace;
        Mode = mode;
    }

    public PlannedProject PlannedProject { get; }

    public ProjectRun Run { get; }

    public IWorkspaceFileSystem TargetParentWorkspace { get; }

    public WorkspaceRelativePath TargetDirectory { get; }

    public IWorkspaceFileSystem RunArtifactWorkspace { get; }

    public ExecutionMode Mode { get; }

    public static ValidationResult<ExecutionRequest> Create(
        PlannedProject? plannedProject,
        ProjectRun? run,
        IWorkspaceFileSystem? targetParentWorkspace,
        WorkspaceRelativePath? targetDirectory,
        IWorkspaceFileSystem? runArtifactWorkspace,
        ExecutionMode mode)
    {
        var issues = new List<ValidationIssue>();
        AddRequired(plannedProject, "planned-project", "plannedProject", issues);
        AddRequired(run, "run", "run", issues);
        AddRequired(targetParentWorkspace, "target-parent-workspace", "targetParentWorkspace", issues);
        AddRequired(targetDirectory, "target-directory", "targetDirectory", issues);
        AddRequired(runArtifactWorkspace, "run-artifact-workspace", "runArtifactWorkspace", issues);
        if (!Enum.IsDefined(mode))
        {
            issues.Add(new ValidationIssue(
                "execution.request.mode.invalid",
                "The execution request mode is not defined.",
                "mode"));
        }

        if (issues.Count == 0 && !IsModeCompatible(mode, run!))
        {
            issues.Add(new ValidationIssue(
                "execution.request.mode.status-mismatch",
                "The execution mode is incompatible with the run lifecycle state.",
                "mode"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new ExecutionRequest(
                plannedProject!,
                run!,
                targetParentWorkspace!,
                targetDirectory!,
                runArtifactWorkspace!,
                mode))
            : ValidationResult.Failure<ExecutionRequest>(issues);
    }

    private static void AddRequired<T>(
        T? value,
        string codePart,
        string location,
        List<ValidationIssue> issues)
        where T : class
    {
        if (value is null)
        {
            issues.Add(new ValidationIssue(
                $"execution.request.{codePart}.required",
                "A required execution request value is missing.",
                location));
        }
    }

    private static bool IsModeCompatible(ExecutionMode mode, ProjectRun run)
    {
        return mode switch
        {
            ExecutionMode.Fresh => run.Status == RunStatus.Draft && run.Attempts.IsEmpty,
            ExecutionMode.Resume => run.ResumeExecution().IsValid,
            ExecutionMode.ManualRetry => run.Status == RunStatus.Executing
                && run.CurrentStepId is null
                && run.Attempts.LastOrDefault() is
                {
                    Outcome: StepAttemptOutcome.Failed,
                    Error.IsRetryable: true,
                },
            _ => false,
        };
    }
}

public sealed class StagingDescriptor
{
    private StagingDescriptor(
        WorkspaceRelativePath containerDirectory,
        WorkspaceRelativePath payloadDirectory,
        WorkspaceRelativePath markerFile,
        string markerId)
    {
        ContainerDirectory = containerDirectory;
        PayloadDirectory = payloadDirectory;
        MarkerFile = markerFile;
        MarkerId = markerId;
    }

    public WorkspaceRelativePath ContainerDirectory { get; }

    public WorkspaceRelativePath PayloadDirectory { get; }

    public WorkspaceRelativePath MarkerFile { get; }

    public string MarkerId { get; }

    public static ValidationResult<StagingDescriptor> Create(
        WorkspaceRelativePath? containerDirectory,
        WorkspaceRelativePath? payloadDirectory,
        WorkspaceRelativePath? markerFile,
        string? markerId)
    {
        var issues = new List<ValidationIssue>();
        AddPathRequired(containerDirectory, "container", "containerDirectory", issues);
        AddPathRequired(payloadDirectory, "payload", "payloadDirectory", issues);
        AddPathRequired(markerFile, "marker", "markerFile", issues);
        if (!ExecutionContractValidation.IsBoundedIdentifier(markerId))
        {
            issues.Add(new ValidationIssue(
                "staging.marker-id.invalid",
                "A canonical bounded ownership marker identifier is required.",
                "markerId"));
        }

        if (containerDirectory is not null)
        {
            var segments = containerDirectory.Value.Split('\\');
            if (segments.Length != 2
                || !StringComparer.Ordinal.Equals(segments[0], ".devforge-staging"))
            {
                issues.Add(new ValidationIssue(
                    "staging.container.invalid",
                    "The staging container must be a direct run-owned directory below .devforge-staging.",
                    "containerDirectory"));
            }

            if (payloadDirectory is not null
                && !IsDirectChild(containerDirectory, payloadDirectory, "payload"))
            {
                issues.Add(new ValidationIssue(
                    "staging.payload.outside-container",
                    "The payload must be the canonical payload child of the owned container.",
                    "payloadDirectory"));
            }

            if (markerFile is not null
                && !IsDirectChild(containerDirectory, markerFile, "ownership.json"))
            {
                issues.Add(new ValidationIssue(
                    "staging.marker.outside-container",
                    "The marker must be the canonical ownership file beside the payload.",
                    "markerFile"));
            }
        }

        return issues.Count == 0
            ? ValidationResult.Success(new StagingDescriptor(
                containerDirectory!,
                payloadDirectory!,
                markerFile!,
                markerId!))
            : ValidationResult.Failure<StagingDescriptor>(issues);
    }

    private static void AddPathRequired(
        WorkspaceRelativePath? value,
        string codePart,
        string location,
        List<ValidationIssue> issues)
    {
        if (value is null)
        {
            issues.Add(new ValidationIssue(
                $"staging.{codePart}.required",
                "A required guarded staging path is missing.",
                location));
        }
    }

    private static bool IsDirectChild(
        WorkspaceRelativePath parent,
        WorkspaceRelativePath child,
        string expectedName)
    {
        return StringComparer.Ordinal.Equals(
            child.Value,
            $"{parent.Value}\\{expectedName}");
    }
}

public sealed class TargetDescriptor
{
    private TargetDescriptor(
        WorkspaceRoot parentRoot,
        WorkspaceRelativePath targetDirectory,
        WorkspaceRelativePath? crossVolumeTemporaryDirectory)
    {
        ParentRoot = parentRoot;
        TargetDirectory = targetDirectory;
        CrossVolumeTemporaryDirectory = crossVolumeTemporaryDirectory;
    }

    public WorkspaceRoot ParentRoot { get; }

    public WorkspaceRelativePath TargetDirectory { get; }

    public WorkspaceRelativePath? CrossVolumeTemporaryDirectory { get; }

    public static ValidationResult<TargetDescriptor> Create(
        WorkspaceRoot? parentRoot,
        WorkspaceRelativePath? targetDirectory,
        WorkspaceRelativePath? crossVolumeTemporaryDirectory)
    {
        var issues = new List<ValidationIssue>();
        if (parentRoot is null)
        {
            issues.Add(new ValidationIssue(
                "target.parent-root.required",
                "An opaque guarded target-parent root is required.",
                "parentRoot"));
        }

        if (targetDirectory is null)
        {
            issues.Add(new ValidationIssue(
                "target.directory.required",
                "A guarded target directory is required.",
                "targetDirectory"));
        }

        if (targetDirectory is not null
            && crossVolumeTemporaryDirectory is not null
            && targetDirectory.Equals(crossVolumeTemporaryDirectory))
        {
            issues.Add(new ValidationIssue(
                "target.temporary.same-as-target",
                "A cross-volume temporary directory must differ from the final target.",
                "crossVolumeTemporaryDirectory"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new TargetDescriptor(
                parentRoot!,
                targetDirectory!,
                crossVolumeTemporaryDirectory))
            : ValidationResult.Failure<TargetDescriptor>(issues);
    }
}

public sealed class RunArtifactDescriptor
{
    private RunArtifactDescriptor(WorkspaceRoot root)
    {
        Root = root;
    }

    public WorkspaceRoot Root { get; }

    public static ValidationResult<RunArtifactDescriptor> Create(WorkspaceRoot? root)
    {
        return root is null
            ? ValidationResult.Failure<RunArtifactDescriptor>(
            [
                new ValidationIssue(
                    "run-artifacts.root.required",
                    "An opaque guarded run-artifact root is required.",
                    "root"),
            ])
            : ValidationResult.Success(new RunArtifactDescriptor(root));
    }
}

public sealed record ExecutionEvidence
{
    private ExecutionEvidence(
        ExecutionEvidenceKind kind,
        string id,
        ExecutionEvidenceStatus status,
        string outputDigest,
        DateTimeOffset? startedAt,
        DateTimeOffset? completedAt,
        string? errorCode,
        RedactedText? errorSummary)
    {
        Kind = kind;
        Id = id;
        Status = status;
        OutputDigest = outputDigest;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        ErrorCode = errorCode;
        ErrorSummary = errorSummary;
    }

    public ExecutionEvidenceKind Kind { get; }

    public string Id { get; }

    public ExecutionEvidenceStatus Status { get; }

    public string OutputDigest { get; }

    public DateTimeOffset? StartedAt { get; }

    public DateTimeOffset? CompletedAt { get; }

    public string? ErrorCode { get; }

    public RedactedText? ErrorSummary { get; }

    /// <summary>
    /// Rehydrates the exact four-property evidence shape written before timed evidence existed.
    /// Runtime execution code must use the strict timed <see cref="Create"/> overload.
    /// </summary>
    internal static ValidationResult<ExecutionEvidence> RehydrateLegacy(
        ExecutionEvidenceKind kind,
        string? id,
        ExecutionEvidenceStatus status,
        string? outputDigest) => CreateCore(
            kind,
            id,
            status,
            outputDigest,
            null,
            null,
            null,
            null,
            legacy: true);

    public static ValidationResult<ExecutionEvidence> Create(
        ExecutionEvidenceKind kind,
        string? id,
        ExecutionEvidenceStatus status,
        string? outputDigest,
        DateTimeOffset? startedAt,
        DateTimeOffset? completedAt,
        string? errorCode,
        string? errorSummary) => CreateCore(
            kind,
            id,
            status,
            outputDigest,
            startedAt,
            completedAt,
            errorCode,
            errorSummary,
            legacy: false);

    private static ValidationResult<ExecutionEvidence> CreateCore(
        ExecutionEvidenceKind kind,
        string? id,
        ExecutionEvidenceStatus status,
        string? outputDigest,
        DateTimeOffset? startedAt,
        DateTimeOffset? completedAt,
        string? errorCode,
        string? errorSummary,
        bool legacy)
    {
        var issues = new List<ValidationIssue>();
        if (!Enum.IsDefined(kind))
        {
            issues.Add(new ValidationIssue(
                "execution.evidence.kind.invalid",
                "The execution evidence kind is not defined.",
                "kind"));
        }

        if (!ExecutionContractValidation.IsBoundedIdentifier(id))
        {
            issues.Add(new ValidationIssue(
                "execution.evidence.id.invalid",
                "A canonical bounded execution evidence identifier is required.",
                "id"));
        }

        if (!Enum.IsDefined(status))
        {
            issues.Add(new ValidationIssue(
                "execution.evidence.status.invalid",
                "The execution evidence status is not defined.",
                "status"));
        }

        if (!ExecutionContractValidation.IsCanonicalDigest(outputDigest))
        {
            issues.Add(new ValidationIssue(
                "execution.evidence.output-digest.invalid",
                "A canonical lowercase SHA-256 output digest is required.",
                "outputDigest"));
        }

        if (kind == ExecutionEvidenceKind.Step && status == ExecutionEvidenceStatus.Warning)
        {
            issues.Add(new ValidationIssue(
                "execution.evidence.step-warning.invalid",
                "Step evidence cannot have warning status.",
                "status"));
        }

        if ((startedAt is null) != (completedAt is null)
            || completedAt < startedAt
            || completedAt - startedAt > TimeSpan.FromDays(1)
            || !legacy && startedAt is null)
        {
            issues.Add(new ValidationIssue(
                "execution.evidence.duration.invalid",
                "Execution evidence timestamps must form a bounded completed duration.",
                "completedAt"));
        }

        var normalizedErrorCode = errorCode?.Trim();
        var safeErrorSummary = errorSummary is null
            ? null
            : RedactedText.FromTrustedRedaction(errorSummary);
        if ((errorCode is null) != (errorSummary is null)
            || normalizedErrorCode is { Length: 0 or > 128 }
            || normalizedErrorCode is not null && !IsCanonicalErrorCode(errorCode!, normalizedErrorCode)
            || safeErrorSummary is { IsValid: false }
            || !legacy && status == ExecutionEvidenceStatus.Passed && errorCode is not null
            || !legacy && status is ExecutionEvidenceStatus.Warning or ExecutionEvidenceStatus.Failed
                && errorCode is null)
        {
            issues.Add(new ValidationIssue(
                "execution.evidence.error.invalid",
                "Execution evidence error metadata must be bounded and redacted.",
                "error"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new ExecutionEvidence(
                kind,
                id!,
                status,
                outputDigest!,
                startedAt,
                completedAt,
                normalizedErrorCode,
                safeErrorSummary?.Value))
            : ValidationResult.Failure<ExecutionEvidence>(issues);
    }

    private static bool IsCanonicalErrorCode(string source, string normalized)
    {
        if (!StringComparer.Ordinal.Equals(source, normalized)
            || !normalized.StartsWith("DF-", StringComparison.Ordinal))
        {
            return false;
        }

        var segments = normalized.Split('-');
        return segments.Length >= 3
            && segments.All(segment => segment.Length > 0
                && segment.All(character => character is >= 'A' and <= 'Z'
                    || character is >= '0' and <= '9'));
    }
}

public sealed class RunCheckpoint
{
    private RunCheckpoint(
        ProjectRun run,
        ExecutionPlan plan,
        PlanPreview? preview,
        BlueprintReference blueprint,
        BlueprintFingerprint blueprintFingerprint,
        StagingDescriptor staging,
        TargetDescriptor target,
        RunArtifactDescriptor runArtifacts,
        ImmutableArray<ExecutionEvidence> evidence,
        FinalizationState finalizationState,
        ReportPersistenceState reportState,
        PublicationSnapshot publication)
    {
        Run = run;
        Plan = plan;
        Preview = preview;
        Blueprint = blueprint;
        BlueprintFingerprint = blueprintFingerprint;
        Staging = staging;
        Target = target;
        RunArtifacts = runArtifacts;
        Evidence = evidence;
        FinalizationState = finalizationState;
        ReportState = reportState;
        Publication = publication;
    }

    public ProjectRun Run { get; }

    public ExecutionPlan Plan { get; }

    public PlanPreview? Preview { get; }

    public string PlanHash => Plan.Id;

    public BlueprintReference Blueprint { get; }

    public BlueprintFingerprint BlueprintFingerprint { get; }

    public StagingDescriptor Staging { get; }

    public TargetDescriptor Target { get; }

    public RunArtifactDescriptor RunArtifacts { get; }

    public ImmutableArray<ExecutionEvidence> Evidence { get; }

    public FinalizationState FinalizationState { get; }

    public ReportPersistenceState ReportState { get; }

    public PublicationSnapshot Publication { get; }

    public static ValidationResult<RunCheckpoint> Create(
        ProjectRun? run,
        ExecutionPlan? plan,
        BlueprintReference? blueprint,
        BlueprintFingerprint? blueprintFingerprint,
        StagingDescriptor? staging,
        TargetDescriptor? target,
        RunArtifactDescriptor? runArtifacts,
        IEnumerable<ExecutionEvidence?>? evidence,
        FinalizationState finalizationState,
        ReportPersistenceState reportState)
    {
        return Create(
            run, plan, null, blueprint, blueprintFingerprint, staging, target,
            runArtifacts, evidence, finalizationState, reportState,
            PublicationSnapshot.LegacyNotRequested());
    }

    public static ValidationResult<RunCheckpoint> Create(
        ProjectRun? run,
        ExecutionPlan? plan,
        PlanPreview? preview,
        BlueprintReference? blueprint,
        BlueprintFingerprint? blueprintFingerprint,
        StagingDescriptor? staging,
        TargetDescriptor? target,
        RunArtifactDescriptor? runArtifacts,
        IEnumerable<ExecutionEvidence?>? evidence,
        FinalizationState finalizationState,
        ReportPersistenceState reportState)
    {
        return Create(
            run,
            plan,
            preview,
            blueprint,
            blueprintFingerprint,
            staging,
            target,
            runArtifacts,
            evidence,
            finalizationState,
            reportState,
            PublicationSnapshot.LegacyNotRequested());
    }

    public static ValidationResult<RunCheckpoint> Create(
        ProjectRun? run,
        ExecutionPlan? plan,
        PlanPreview? preview,
        BlueprintReference? blueprint,
        BlueprintFingerprint? blueprintFingerprint,
        StagingDescriptor? staging,
        TargetDescriptor? target,
        RunArtifactDescriptor? runArtifacts,
        IEnumerable<ExecutionEvidence?>? evidence,
        FinalizationState finalizationState,
        ReportPersistenceState reportState,
        PublicationSnapshot? publication)
    {
        var snapshot = evidence?.ToImmutableArray() ?? [];
        var issues = new List<ValidationIssue>();
        AddRequired(run, "run", "run", issues);
        AddRequired(plan, "plan", "plan", issues);
        AddRequired(blueprint, "blueprint", "blueprint", issues);
        AddRequired(blueprintFingerprint, "blueprint-fingerprint", "blueprintFingerprint", issues);
        AddRequired(staging, "staging", "staging", issues);
        AddRequired(target, "target", "target", issues);
        AddRequired(runArtifacts, "run-artifacts", "runArtifacts", issues);
        AddRequired(publication, "publication", "publication", issues);
        if (plan is not null && !ExecutionContractValidation.IsCanonicalDigest(plan.Id))
        {
            issues.Add(new ValidationIssue(
                "checkpoint.plan-hash.invalid",
                "The checkpoint plan identifier must be a canonical plan hash.",
                "plan.id"));
        }

        if (preview is not null
            && (plan is null
                || !StringComparer.Ordinal.Equals(preview.PlanHash, plan.Id)
                || blueprint is null
                || !preview.Blueprint.Equals(blueprint)
                || !PreviewMatchesPlan(preview, plan)))
        {
            issues.Add(new ValidationIssue(
                "checkpoint.preview.mismatch",
                "The persisted plan preview must match the exact plan and blueprint.",
                "preview"));
        }

        ValidateEvidence(evidence, snapshot, plan, issues);
        if (!Enum.IsDefined(finalizationState))
        {
            issues.Add(new ValidationIssue(
                "checkpoint.finalization-state.invalid",
                "The checkpoint finalization state is not defined.",
                "finalizationState"));
        }

        if (!Enum.IsDefined(reportState))
        {
            issues.Add(new ValidationIssue(
                "checkpoint.report-state.invalid",
                "The checkpoint report state is not defined.",
                "reportState"));
        }

        if (reportState != ReportPersistenceState.NotStarted
            && finalizationState != FinalizationState.Succeeded)
        {
            issues.Add(new ValidationIssue(
                "checkpoint.report.before-finalization",
                "A report result requires successful finalization evidence.",
                "reportState"));
        }

        if (run?.Status == RunStatus.LocalReady
            && (finalizationState != FinalizationState.Succeeded
                || reportState != ReportPersistenceState.Succeeded))
        {
            issues.Add(new ValidationIssue(
                "checkpoint.local-ready.incomplete",
                "A LocalReady checkpoint requires successful finalization and report persistence.",
                "run.status"));
        }

        if (publication is not null && preview is not null)
        {
            ValidatePublicationIntent(run, preview, publication, issues);
        }

        if (run?.Status is RunStatus.PublishPending or RunStatus.Completed && preview is null)
        {
            issues.Add(new ValidationIssue(
                "checkpoint.publication.preview-required",
                "Publication requires the exact persisted reviewed plan preview.",
                "preview"));
        }

        if (run?.Status == RunStatus.LocalReady
            && publication is not null
            && (publication.GitState != GitPublicationState.NotRequested
                || publication.GitHubState != GitHubPublicationState.NotRequested
                || publication.ReceiptState != PublicationReceiptState.NotRequested))
        {
            issues.Add(new ValidationIssue(
                "checkpoint.local-ready.publication-started",
                "LocalReady cannot contain started publication side effects.",
                "publication"));
        }

        if (run is not null
            && run.Status is not (RunStatus.LocalReady
                or RunStatus.PublishPending
                or RunStatus.Completed)
            && publication is not null
            && (publication.GitState != GitPublicationState.NotRequested
                || publication.GitHubState != GitHubPublicationState.NotRequested
                || publication.ReceiptState != PublicationReceiptState.NotRequested))
        {
            issues.Add(new ValidationIssue(
                "checkpoint.publication.status-invalid",
                "Publication evidence is not allowed before LocalReady or after a terminal failure.",
                "run.status"));
        }

        if (run?.Status is RunStatus.PublishPending or RunStatus.Completed
            && (finalizationState != FinalizationState.Succeeded
                || reportState != ReportPersistenceState.Succeeded))
        {
            issues.Add(new ValidationIssue(
                "checkpoint.publication.before-local-ready",
                "Publication requires successful finalization and report persistence.",
                "run.status"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new RunCheckpoint(
                run!,
                plan!,
                preview,
                blueprint!,
                blueprintFingerprint!,
                staging!,
                target!,
                runArtifacts!,
                [.. snapshot.Select(item => item!)],
                finalizationState,
                reportState,
                publication!))
            : ValidationResult.Failure<RunCheckpoint>(issues);
    }

    private static void ValidatePublicationIntent(
        ProjectRun? run,
        PlanPreview preview,
        PublicationSnapshot publication,
        List<ValidationIssue> issues)
    {
        var git = preview.Git;
        var githubRequested = publication.GitHubState != GitHubPublicationState.NotRequested;
        if ((!git.InitializeRepository && publication.GitState != GitPublicationState.NotRequested)
            || (!git.PublishToGitHub && githubRequested))
        {
            issues.Add(new ValidationIssue(
                "checkpoint.publication.intent-mismatch",
                "Publication evidence does not match the reviewed Git intent.",
                "publication"));
        }

        if (git.PublishToGitHub && githubRequested
            && (publication.RepositoryIdentity is null
                || !StringComparer.Ordinal.Equals(
                    git.GitHubAccount,
                    publication.RepositoryIdentity.Account)
                || !StringComparer.Ordinal.Equals(
                    git.GitHubRepository,
                    publication.RepositoryIdentity.RepositoryName)
                || git.IsPrivate != publication.IsPrivate))
        {
            issues.Add(new ValidationIssue(
                "checkpoint.publication.github-intent-mismatch",
                "GitHub evidence does not match the reviewed repository identity and visibility.",
                "publication.repositoryIdentity"));
        }

        var branchEvidenceMatches = git.BranchPolicy switch
        {
            GitBranchPolicy.Main => publication.Branches.SequenceEqual(
                ["main"], StringComparer.Ordinal),
            GitBranchPolicy.MainAndDevelop when publication.GitState == GitPublicationState.Succeeded =>
                publication.Branches.SequenceEqual(["main", "develop"], StringComparer.Ordinal),
            GitBranchPolicy.MainAndDevelop => publication.Branches.SequenceEqual(
                    ["main"], StringComparer.Ordinal)
                || publication.Branches.SequenceEqual(["main", "develop"], StringComparer.Ordinal),
            _ => false,
        };
        if (publication.InitialCommitId is not null && !branchEvidenceMatches)
        {
            issues.Add(new ValidationIssue(
                "checkpoint.publication.branch-policy-mismatch",
                "Git branch evidence does not match the reviewed branch policy.",
                "publication.branches"));
        }

        if (git.PublishToGitHub
            && publication.ReceiptState != PublicationReceiptState.NotRequested
            && publication.GitHubState != GitHubPublicationState.Succeeded)
        {
            issues.Add(new ValidationIssue(
                "checkpoint.publication.receipt-before-github",
                "Receipt persistence requires verified reviewed GitHub publication.",
                "publication.receiptState"));
        }

        if (run?.Status == RunStatus.PublishPending
            && (!git.InitializeRepository
                || publication.GitState == GitPublicationState.NotRequested))
        {
            issues.Add(new ValidationIssue(
                "checkpoint.publication.pending-without-intent",
                "PublishPending requires persisted reviewed Git intent.",
                "run.status"));
        }

        if (run?.Status == RunStatus.Completed
            && (publication.GitState != GitPublicationState.Succeeded
                || publication.ReceiptState != PublicationReceiptState.Succeeded
                || (git.PublishToGitHub
                    && publication.GitHubState != GitHubPublicationState.Succeeded)
                || (!git.PublishToGitHub
                    && publication.GitHubState != GitHubPublicationState.NotRequested)))
        {
            issues.Add(new ValidationIssue(
                "checkpoint.publication.incomplete",
                "Completed requires exact Git, reviewed GitHub, and receipt evidence.",
                "run.status"));
        }
    }

    private static bool PreviewMatchesPlan(PlanPreview preview, ExecutionPlan plan)
    {
        return preview.Steps.Length == plan.Steps.Length
            && preview.Validators.Length == plan.Validators.Length
            && preview.Steps.Zip(plan.Steps).All(pair =>
                StringComparer.Ordinal.Equals(pair.First.Id, pair.Second.Id)
                && StringComparer.Ordinal.Equals(pair.First.HandlerId, pair.Second.Handler)
                && pair.First.Timeout == pair.Second.Timeout)
            && preview.Validators.Zip(plan.Validators).All(pair =>
                StringComparer.Ordinal.Equals(pair.First.Id, pair.Second.Id)
                && StringComparer.Ordinal.Equals(pair.First.HandlerId, pair.Second.Handler)
                && pair.First.Timeout == pair.Second.Timeout
                && pair.First.Required == pair.Second.Required);
    }

    private static void ValidateEvidence(
        IEnumerable<ExecutionEvidence?>? source,
        ImmutableArray<ExecutionEvidence?> snapshot,
        ExecutionPlan? plan,
        List<ValidationIssue> issues)
    {
        if (source is null)
        {
            issues.Add(new ValidationIssue(
                "checkpoint.evidence.required",
                "Checkpoint evidence is required.",
                "evidence"));
            return;
        }

        var identities = new HashSet<(ExecutionEvidenceKind Kind, string Id)>();
        for (var index = 0; index < snapshot.Length; index++)
        {
            var item = snapshot[index];
            if (item is null)
            {
                issues.Add(new ValidationIssue(
                    "checkpoint.evidence.item.required",
                    "Checkpoint evidence cannot contain null items.",
                    $"evidence[{index}]"));
            }
            else if (!identities.Add((item.Kind, item.Id)))
            {
                issues.Add(new ValidationIssue(
                    "checkpoint.evidence.duplicate",
                    "Checkpoint evidence identities must be unique.",
                    $"evidence[{index}]"));
            }

            if (item is not null && plan is not null)
            {
                var isKnown = item.Kind switch
                {
                    ExecutionEvidenceKind.Step => plan.Steps.Any(step =>
                        StringComparer.Ordinal.Equals(step.Id, item.Id)),
                    ExecutionEvidenceKind.Validator => plan.Validators.Any(validator =>
                        StringComparer.Ordinal.Equals(validator.Id, item.Id)),
                    ExecutionEvidenceKind.SecretScan =>
                        StringComparer.Ordinal.Equals(item.Id, "secret-scan"),
                    _ => false,
                };
                if (!isKnown)
                {
                    issues.Add(new ValidationIssue(
                        $"checkpoint.evidence.{item.Kind.ToString().ToLowerInvariant()}.unknown",
                        "Checkpoint evidence must reference the immutable plan or mandatory secret scan.",
                        $"evidence[{index}].id"));
                }
            }
        }
    }

    private static void AddRequired<T>(
        T? value,
        string codePart,
        string location,
        List<ValidationIssue> issues)
        where T : class
    {
        if (value is null)
        {
            issues.Add(new ValidationIssue(
                $"checkpoint.{codePart}.required",
                "A required checkpoint value is missing.",
                location));
        }
    }
}

internal static class ExecutionContractValidation
{
    public static bool IsCanonicalDigest(string? value)
    {
        const string prefix = "sha256:";
        if (value is null
            || value.Length != prefix.Length + 64
            || !value.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var character in value.AsSpan(prefix.Length))
        {
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsBoundedIdentifier(string? value)
    {
        if (value is null || value.Length is < 1 or > 128)
        {
            return false;
        }

        return value[0] is >= 'a' and <= 'z' or >= '0' and <= '9'
            && value.All(character =>
                character is >= 'a' and <= 'z'
                    or >= '0' and <= '9'
                    or '-'
                    or '.');
    }
}
