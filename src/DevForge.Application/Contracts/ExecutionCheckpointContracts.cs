using System.Collections.Immutable;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Execution;
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
        string outputDigest)
    {
        Kind = kind;
        Id = id;
        Status = status;
        OutputDigest = outputDigest;
    }

    public ExecutionEvidenceKind Kind { get; }

    public string Id { get; }

    public ExecutionEvidenceStatus Status { get; }

    public string OutputDigest { get; }

    public static ValidationResult<ExecutionEvidence> Create(
        ExecutionEvidenceKind kind,
        string? id,
        ExecutionEvidenceStatus status,
        string? outputDigest)
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

        return issues.Count == 0
            ? ValidationResult.Success(new ExecutionEvidence(kind, id!, status, outputDigest!))
            : ValidationResult.Failure<ExecutionEvidence>(issues);
    }
}

public sealed class RunCheckpoint
{
    private RunCheckpoint(
        ProjectRun run,
        ExecutionPlan plan,
        BlueprintReference blueprint,
        BlueprintFingerprint blueprintFingerprint,
        StagingDescriptor staging,
        TargetDescriptor target,
        RunArtifactDescriptor runArtifacts,
        ImmutableArray<ExecutionEvidence> evidence,
        FinalizationState finalizationState,
        ReportPersistenceState reportState)
    {
        Run = run;
        Plan = plan;
        Blueprint = blueprint;
        BlueprintFingerprint = blueprintFingerprint;
        Staging = staging;
        Target = target;
        RunArtifacts = runArtifacts;
        Evidence = evidence;
        FinalizationState = finalizationState;
        ReportState = reportState;
    }

    public ProjectRun Run { get; }

    public ExecutionPlan Plan { get; }

    public string PlanHash => Plan.Id;

    public BlueprintReference Blueprint { get; }

    public BlueprintFingerprint BlueprintFingerprint { get; }

    public StagingDescriptor Staging { get; }

    public TargetDescriptor Target { get; }

    public RunArtifactDescriptor RunArtifacts { get; }

    public ImmutableArray<ExecutionEvidence> Evidence { get; }

    public FinalizationState FinalizationState { get; }

    public ReportPersistenceState ReportState { get; }

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
        var snapshot = evidence?.ToImmutableArray() ?? [];
        var issues = new List<ValidationIssue>();
        AddRequired(run, "run", "run", issues);
        AddRequired(plan, "plan", "plan", issues);
        AddRequired(blueprint, "blueprint", "blueprint", issues);
        AddRequired(blueprintFingerprint, "blueprint-fingerprint", "blueprintFingerprint", issues);
        AddRequired(staging, "staging", "staging", issues);
        AddRequired(target, "target", "target", issues);
        AddRequired(runArtifacts, "run-artifacts", "runArtifacts", issues);
        if (plan is not null && !ExecutionContractValidation.IsCanonicalDigest(plan.Id))
        {
            issues.Add(new ValidationIssue(
                "checkpoint.plan-hash.invalid",
                "The checkpoint plan identifier must be a canonical plan hash.",
                "plan.id"));
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
                "A successful report requires successful finalization evidence.",
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

        return issues.Count == 0
            ? ValidationResult.Success(new RunCheckpoint(
                run!,
                plan!,
                blueprint!,
                blueprintFingerprint!,
                staging!,
                target!,
                runArtifacts!,
                [.. snapshot.Select(item => item!)],
                finalizationState,
                reportState))
            : ValidationResult.Failure<RunCheckpoint>(issues);
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
