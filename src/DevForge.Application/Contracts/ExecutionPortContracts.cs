using System.Collections.Immutable;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Diagnostics;
using DevForge.Domain.Execution;
using DevForge.Domain.Reports;
using DevForge.Domain.Runs;
using DevForge.Domain.Validation;

namespace DevForge.Application.Contracts;

public enum ExecutionPhase
{
    Prepare = 1,
    Precondition = 2,
    Execute = 3,
    Postcondition = 4,
    Persist = 5,
    Decide = 6,
}

public enum ExecutionHandlerOutcome
{
    Succeeded = 1,
    Failed = 2,
    Cancelled = 3,
}

public sealed class ExecutionOperationResult<T>
    where T : class
{
    private readonly T? _value;

    internal ExecutionOperationResult(T value)
    {
        _value = value;
    }

    internal ExecutionOperationResult(DevForgeError error)
    {
        Error = error;
    }

    public bool IsSuccessful => Error is null;

    public DevForgeError? Error { get; }

    public T Value => IsSuccessful
        ? _value!
        : throw new InvalidOperationException("A failed execution operation has no value.");

}

public static class ExecutionOperationResult
{
    public static ExecutionOperationResult<T> Success<T>(T value)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        return new ExecutionOperationResult<T>(value);
    }

    public static ExecutionOperationResult<T> Failure<T>(DevForgeError error)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(error);
        return new ExecutionOperationResult<T>(error);
    }
}

public sealed class BlueprintExecutionPackage
{
    private BlueprintExecutionPackage(
        ResolvedBlueprint blueprint,
        IWorkspaceFileSystem packageWorkspace)
    {
        Blueprint = blueprint;
        PackageWorkspace = packageWorkspace;
    }

    public ResolvedBlueprint Blueprint { get; }

    public IWorkspaceFileSystem PackageWorkspace { get; }

    public static ValidationResult<BlueprintExecutionPackage> Create(
        ResolvedBlueprint? blueprint,
        IWorkspaceFileSystem? packageWorkspace)
    {
        var issues = new List<ValidationIssue>();
        if (blueprint is null)
        {
            issues.Add(new ValidationIssue(
                "blueprint.execution-package.blueprint.required",
                "A verified resolved blueprint is required.",
                "blueprint"));
        }

        if (packageWorkspace is null)
        {
            issues.Add(new ValidationIssue(
                "blueprint.execution-package.workspace.required",
                "A guarded blueprint package workspace is required.",
                "packageWorkspace"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new BlueprintExecutionPackage(blueprint!, packageWorkspace!))
            : ValidationResult.Failure<BlueprintExecutionPackage>(issues);
    }
}

public sealed class StagingWorkspace
{
    private StagingWorkspace(
        StagingDescriptor descriptor,
        IWorkspaceFileSystem payloadWorkspace)
    {
        Descriptor = descriptor;
        PayloadWorkspace = payloadWorkspace;
    }

    public StagingDescriptor Descriptor { get; }

    public IWorkspaceFileSystem PayloadWorkspace { get; }

    public static ValidationResult<StagingWorkspace> Create(
        StagingDescriptor? descriptor,
        IWorkspaceFileSystem? payloadWorkspace)
    {
        var issues = new List<ValidationIssue>();
        if (descriptor is null)
        {
            issues.Add(new ValidationIssue(
                "staging.workspace.descriptor.required",
                "A validated staging descriptor is required.",
                "descriptor"));
        }

        if (payloadWorkspace is null)
        {
            issues.Add(new ValidationIssue(
                "staging.workspace.payload.required",
                "A guarded staging payload workspace is required.",
                "payloadWorkspace"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new StagingWorkspace(descriptor!, payloadWorkspace!))
            : ValidationResult.Failure<StagingWorkspace>(issues);
    }
}

public interface IStagingWorkspaceLease : IAsyncDisposable
{
    StagingWorkspace Workspace { get; }
}

public sealed class ExecutionHandlerRequest
{
    private ExecutionHandlerRequest(
        string runId,
        ExecutionStep step,
        ExecutionPlan plan,
        StagingWorkspace staging,
        BlueprintExecutionPackage blueprintPackage,
        ImmutableSortedDictionary<string, string> templateContext)
    {
        RunId = runId;
        Step = step;
        Plan = plan;
        Staging = staging;
        BlueprintPackage = blueprintPackage;
        TemplateContext = templateContext;
    }

    public string RunId { get; }

    public ExecutionStep Step { get; }

    public ExecutionPlan Plan { get; }

    public StagingWorkspace Staging { get; }

    public BlueprintExecutionPackage BlueprintPackage { get; }

    public ImmutableSortedDictionary<string, string> TemplateContext { get; }

    public static ValidationResult<ExecutionHandlerRequest> Create(
        string? runId,
        ExecutionStep? step,
        StagingWorkspace? staging,
        BlueprintExecutionPackage? blueprintPackage,
        ExecutionPlan? plan)
    {
        var issues = new List<ValidationIssue>();
        if (!ExecutionContractValidation.IsBoundedIdentifier(runId))
        {
            issues.Add(new ValidationIssue(
                "handler.request.run-id.invalid",
                "A canonical bounded run identifier is required.",
                "runId"));
        }

        AddRequired(step, "step", issues);
        AddRequired(plan, "plan", issues);
        AddRequired(staging, "staging", issues);
        AddRequired(blueprintPackage, "blueprintPackage", issues);
        if (step is not null
            && plan is not null
            && !plan.Steps.Any(candidate => ReferenceEquals(candidate, step)))
        {
            issues.Add(new ValidationIssue(
                "handler.request.step.plan-mismatch",
                "The execution step must be owned by the supplied hashed plan.",
                "step"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new ExecutionHandlerRequest(
                runId!,
                step!,
                plan!,
                staging!,
                blueprintPackage!,
                plan!.TemplateContext))
            : ValidationResult.Failure<ExecutionHandlerRequest>(issues);
    }

    private static void AddRequired<T>(T? value, string location, List<ValidationIssue> issues)
        where T : class
    {
        if (value is null)
        {
            issues.Add(new ValidationIssue(
                $"handler.request.{location}.required",
                "A required handler request value is missing.",
                location));
        }
    }
}

public sealed class ExecutionHandlerResult
{
    private ExecutionHandlerResult(
        ExecutionPhase phase,
        ExecutionHandlerOutcome outcome,
        int? exitCode,
        string? outputDigest,
        DevForgeError? error,
        ImmutableArray<WorkspaceRelativePath> affectedPaths)
    {
        Phase = phase;
        Outcome = outcome;
        ExitCode = exitCode;
        OutputDigest = outputDigest;
        Error = error;
        AffectedPaths = affectedPaths;
    }

    public ExecutionPhase Phase { get; }

    public ExecutionHandlerOutcome Outcome { get; }

    public int? ExitCode { get; }

    public string? OutputDigest { get; }

    public DevForgeError? Error { get; }

    public ImmutableArray<WorkspaceRelativePath> AffectedPaths { get; }

    public static ValidationResult<ExecutionHandlerResult> Create(
        ExecutionPhase phase,
        ExecutionHandlerOutcome outcome,
        int? exitCode,
        string? outputDigest,
        DevForgeError? error,
        IEnumerable<WorkspaceRelativePath?>? affectedPaths)
    {
        var snapshot = affectedPaths?.ToImmutableArray() ?? [];
        var issues = new List<ValidationIssue>();
        if (!Enum.IsDefined(phase) || phase is ExecutionPhase.Persist or ExecutionPhase.Decide)
        {
            issues.Add(new ValidationIssue(
                "handler.result.phase.invalid",
                "A handler result must use a defined handler-owned phase.",
                "phase"));
        }

        if (!Enum.IsDefined(outcome))
        {
            issues.Add(new ValidationIssue(
                "handler.result.outcome.invalid",
                "The handler outcome is not defined.",
                "outcome"));
        }

        if (outputDigest is not null && !ExecutionContractValidation.IsCanonicalDigest(outputDigest))
        {
            issues.Add(new ValidationIssue(
                "handler.result.output-digest.invalid",
                "Handler output evidence must be a canonical lowercase SHA-256 digest.",
                "outputDigest"));
        }

        if (outcome == ExecutionHandlerOutcome.Succeeded && error is not null
            || outcome == ExecutionHandlerOutcome.Failed && error is null)
        {
            issues.Add(new ValidationIssue(
                "handler.result.error.inconsistent",
                "The handler error is inconsistent with its outcome.",
                "error"));
        }

        if (phase is not ExecutionPhase.Execute && exitCode is not null)
        {
            issues.Add(new ValidationIssue(
                "handler.result.exit-code.unexpected",
                "Only the execute phase can carry a process exit code.",
                "exitCode"));
        }

        ValidateAffectedPaths(affectedPaths, snapshot, issues);
        return issues.Count == 0
            ? ValidationResult.Success(new ExecutionHandlerResult(
                phase,
                outcome,
                exitCode,
                outputDigest,
                error,
                [.. snapshot.Select(path => path!)]))
            : ValidationResult.Failure<ExecutionHandlerResult>(issues);
    }

    private static void ValidateAffectedPaths(
        IEnumerable<WorkspaceRelativePath?>? source,
        ImmutableArray<WorkspaceRelativePath?> snapshot,
        List<ValidationIssue> issues)
    {
        if (source is null)
        {
            issues.Add(new ValidationIssue(
                "handler.result.affected-paths.required",
                "Handler affected paths are required.",
                "affectedPaths"));
            return;
        }

        var paths = new HashSet<WorkspaceRelativePath>();
        for (var index = 0; index < snapshot.Length; index++)
        {
            if (snapshot[index] is null)
            {
                issues.Add(new ValidationIssue(
                    "handler.result.affected-path.required",
                    "Handler affected paths cannot contain null values.",
                    $"affectedPaths[{index}]"));
            }
            else if (!paths.Add(snapshot[index]!))
            {
                issues.Add(new ValidationIssue(
                    "handler.result.affected-path.duplicate",
                    "Handler affected paths must be unique.",
                    $"affectedPaths[{index}]"));
            }
        }
    }
}

public sealed class FinalizationReceipt
{
    private FinalizationReceipt(TargetDescriptor target, string treeDigest)
    {
        Target = target;
        TreeDigest = treeDigest;
    }

    public TargetDescriptor Target { get; }

    public string TreeDigest { get; }

    public static ValidationResult<FinalizationReceipt> Create(
        TargetDescriptor? target,
        string? treeDigest)
    {
        var issues = new List<ValidationIssue>();
        if (target is null)
        {
            issues.Add(new ValidationIssue(
                "finalization.receipt.target.required",
                "A finalized guarded target descriptor is required.",
                "target"));
        }

        if (!ExecutionContractValidation.IsCanonicalDigest(treeDigest))
        {
            issues.Add(new ValidationIssue(
                "finalization.receipt.tree-digest.invalid",
                "A canonical lowercase SHA-256 tree digest is required.",
                "treeDigest"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new FinalizationReceipt(target!, treeDigest!))
            : ValidationResult.Failure<FinalizationReceipt>(issues);
    }
}

public sealed class ReportWriteReceipt
{
    private ReportWriteReceipt(
        WorkspaceRelativePath jsonReport,
        WorkspaceRelativePath markdownReport)
    {
        JsonReport = jsonReport;
        MarkdownReport = markdownReport;
    }

    public WorkspaceRelativePath JsonReport { get; }

    public WorkspaceRelativePath MarkdownReport { get; }

    public static ValidationResult<ReportWriteReceipt> Create(
        WorkspaceRelativePath? jsonReport,
        WorkspaceRelativePath? markdownReport)
    {
        var issues = new List<ValidationIssue>();
        if (jsonReport is null)
        {
            issues.Add(new ValidationIssue(
                "report.receipt.json.required",
                "A guarded JSON report path is required.",
                "jsonReport"));
        }
        else if (!jsonReport.Value.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new ValidationIssue(
                "report.receipt.json.extension.invalid",
                "The JSON report path must use the .json extension.",
                "jsonReport"));
        }

        if (markdownReport is null)
        {
            issues.Add(new ValidationIssue(
                "report.receipt.markdown.required",
                "A guarded Markdown report path is required.",
                "markdownReport"));
        }
        else if (!markdownReport.Value.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new ValidationIssue(
                "report.receipt.markdown.extension.invalid",
                "The Markdown report path must use the .md extension.",
                "markdownReport"));
        }

        if (jsonReport is not null && jsonReport.Equals(markdownReport))
        {
            issues.Add(new ValidationIssue(
                "report.receipt.paths.duplicate",
                "The JSON and Markdown report paths must be distinct.",
                "markdownReport"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new ReportWriteReceipt(jsonReport!, markdownReport!))
            : ValidationResult.Failure<ReportWriteReceipt>(issues);
    }
}

public sealed class StagingCleanupReceipt
{
    private StagingCleanupReceipt(string runId, string markerId)
    {
        RunId = runId;
        MarkerId = markerId;
    }

    public string RunId { get; }

    public string MarkerId { get; }

    public static ValidationResult<StagingCleanupReceipt> Create(
        string? runId,
        string? markerId)
    {
        var issues = new List<ValidationIssue>();
        if (!ExecutionContractValidation.IsBoundedIdentifier(runId))
        {
            issues.Add(new ValidationIssue(
                "staging.cleanup.run-id.invalid",
                "A canonical bounded run identifier is required.",
                "runId"));
        }

        if (!ExecutionContractValidation.IsBoundedIdentifier(markerId))
        {
            issues.Add(new ValidationIssue(
                "staging.cleanup.marker-id.invalid",
                "A canonical bounded marker identifier is required.",
                "markerId"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new StagingCleanupReceipt(runId!, markerId!))
            : ValidationResult.Failure<StagingCleanupReceipt>(issues);
    }
}

public interface IRunCheckpointStore
{
    Task SaveAsync(RunCheckpoint checkpoint, CancellationToken cancellationToken);

    Task<RunCheckpoint?> FindAsync(string runId, CancellationToken cancellationToken);

    Task<ImmutableArray<RunCheckpoint>> ListAsync(CancellationToken cancellationToken);
}

public interface IStagingWorkspaceManager
{
    Task<ExecutionOperationResult<IStagingWorkspaceLease>> CreateAsync(
        ExecutionRequest request,
        CancellationToken cancellationToken);

    Task<ExecutionOperationResult<IStagingWorkspaceLease>> ValidateOwnershipAsync(
        RunCheckpoint checkpoint,
        IWorkspaceFileSystem targetParentWorkspace,
        CancellationToken cancellationToken);

    Task<ExecutionOperationResult<StagingCleanupReceipt>> CleanupAsync(
        RunCheckpoint checkpoint,
        IWorkspaceFileSystem targetParentWorkspace,
        CancellationToken cancellationToken);
}

public interface IBlueprintExecutionSource
{
    Task<ExecutionOperationResult<BlueprintExecutionPackage>> OpenAsync(
        BlueprintReference blueprint,
        BlueprintFingerprint fingerprint,
        CancellationToken cancellationToken);
}

public interface IProjectFinalizer
{
    Task<ExecutionOperationResult<FinalizationReceipt>> FinalizeAsync(
        RunCheckpoint checkpoint,
        StagingWorkspace staging,
        IWorkspaceFileSystem targetParentWorkspace,
        CancellationToken cancellationToken);
}

public interface IGenerationReportWriter
{
    Task<ExecutionOperationResult<ReportWriteReceipt>> WriteAsync(
        RunCheckpoint checkpoint,
        GenerationReport report,
        IWorkspaceFileSystem runArtifactWorkspace,
        CancellationToken cancellationToken);
}

public interface IExecutionHandler
{
    string Id { get; }

    Task<ExecutionHandlerResult> PrepareAsync(
        ExecutionHandlerRequest request,
        CancellationToken cancellationToken);

    Task<ExecutionHandlerResult> CheckPreconditionsAsync(
        ExecutionHandlerRequest request,
        CancellationToken cancellationToken);

    Task<ExecutionHandlerResult> ExecuteAsync(
        ExecutionHandlerRequest request,
        IProgress<ExecutionProgressLine>? progress,
        CancellationToken cancellationToken);

    Task<ExecutionHandlerResult> CheckPostconditionsAsync(
        ExecutionHandlerRequest request,
        CancellationToken cancellationToken);

    Task<ExecutionHandlerResult> CleanupForRetryAsync(
        ExecutionHandlerRequest request,
        CancellationToken cancellationToken);
}

public interface IExecutionHandlerRegistry
{
    IExecutionHandler? Resolve(string handlerId);
}

public interface IRunRecoveryService
{
    Task<ExecutionOperationResult<RunCheckpoint>> NormalizeInterruptedAsync(
        RunCheckpoint checkpoint,
        CancellationToken cancellationToken);

    Task<ExecutionOperationResult<RunCheckpoint>> ResumeAsync(
        ExecutionRequest request,
        CancellationToken cancellationToken);

    Task<ExecutionOperationResult<StagingCleanupReceipt>> CleanupAsync(
        RunCheckpoint checkpoint,
        IWorkspaceFileSystem targetParentWorkspace,
        CancellationToken cancellationToken);
}
