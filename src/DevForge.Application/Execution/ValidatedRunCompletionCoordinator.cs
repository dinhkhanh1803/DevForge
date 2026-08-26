using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using DevForge.Application.Contracts;
using DevForge.Domain.Diagnostics;
using DevForge.Domain.Privacy;
using DevForge.Domain.Reports;
using DevForge.Domain.Runs;

namespace DevForge.Application.Execution;

public sealed class ValidatedRunCompletionCoordinator : IRunCompletionCoordinator
{
    private readonly IRunCheckpointStore _checkpointStore;
    private readonly ISecretScanner _secretScanner;
    private readonly IProjectFinalizer _finalizer;
    private readonly IProjectEvidenceWriter _projectEvidenceWriter;
    private readonly IGenerationReportWriter _reportWriter;
    private readonly TimeProvider _timeProvider;

    public ValidatedRunCompletionCoordinator(
        IRunCheckpointStore checkpointStore,
        ISecretScanner secretScanner,
        IProjectFinalizer finalizer,
        IProjectEvidenceWriter projectEvidenceWriter,
        IGenerationReportWriter reportWriter,
        TimeProvider timeProvider)
    {
        _checkpointStore = checkpointStore ?? throw new ArgumentNullException(nameof(checkpointStore));
        _secretScanner = secretScanner ?? throw new ArgumentNullException(nameof(secretScanner));
        _finalizer = finalizer ?? throw new ArgumentNullException(nameof(finalizer));
        _projectEvidenceWriter = projectEvidenceWriter
            ?? throw new ArgumentNullException(nameof(projectEvidenceWriter));
        _reportWriter = reportWriter ?? throw new ArgumentNullException(nameof(reportWriter));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<RunCheckpoint> CompleteAsync(
        ExecutionRequest request,
        RunCheckpoint checkpoint,
        StagingWorkspace staging,
        BlueprintExecutionPackage blueprintPackage,
        IExecutionHandlerRegistry registry,
        IProgress<ExecutionProgressLine>? progress,
        CancellationToken cancellationToken)
    {
        ValidateBoundary(request, checkpoint, staging, blueprintPackage, registry);
        var validations = new List<ValidationCheck>();
        try
        {
            foreach (var validator in checkpoint.Plan.Validators)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var reusableEvidence = request.Mode == ExecutionMode.Resume
                    ? FindReusableEvidence(
                        checkpoint,
                        ExecutionEvidenceKind.Validator,
                        validator.Id,
                        allowWarning: !validator.Required)
                    : null;
                var handler = registry.Resolve(validator.Handler)
                    ?? throw new InvalidOperationException("The validator handler is unavailable.");
                var handlerRequest = ExecutionHandlerRequest.Create(
                    checkpoint.Run.Id,
                    validator,
                    staging,
                    blueprintPackage,
                    checkpoint.Plan).Value;
                var validatorStartedAt = _timeProvider.GetUtcNow();
                var result = await ExecuteValidatorAsync(
                    handler,
                    handlerRequest,
                    progress,
                    cancellationToken).ConfigureAwait(false);
                var validatorCompletedAt = _timeProvider.GetUtcNow();
                var digest = result.OutputDigest ?? Digest(
                    $"validator:{validator.Id}:{result.Outcome}:{result.Error?.Code}");
                var status = result.Outcome == ExecutionHandlerOutcome.Succeeded
                    ? ExecutionEvidenceStatus.Passed
                    : validator.Required
                        ? ExecutionEvidenceStatus.Failed
                        : ExecutionEvidenceStatus.Warning;
                if (reusableEvidence is not null
                    && (status != reusableEvidence.Status
                        || !StringComparer.Ordinal.Equals(digest, reusableEvidence.OutputDigest)))
                {
                    checkpoint = AppendError(checkpoint, Error(
                        "DF-VALID-002",
                        "Previously successful validation evidence no longer matches.",
                        "A resumed validator produced evidence different from the persisted result.",
                        validator.Id));
                    await SaveAsync(checkpoint).ConfigureAwait(false);
                    checkpoint = Transition(checkpoint, RunStatus.ValidationFailed);
                    await SaveAsync(checkpoint).ConfigureAwait(false);
                    return checkpoint;
                }

                if (reusableEvidence is null)
                {
                    checkpoint = WithEvidence(
                        checkpoint,
                        ExecutionEvidence.Create(
                            ExecutionEvidenceKind.Validator,
                            validator.Id,
                            status,
                            digest,
                            validatorStartedAt,
                            validatorCompletedAt,
                            result.Error?.Code,
                            result.Error?.Summary).Value);
                }
                validations.Add(new ValidationCheck(
                    validator.Id,
                    status switch
                    {
                        ExecutionEvidenceStatus.Passed => ValidationCheckStatus.Passed,
                        ExecutionEvidenceStatus.Warning => ValidationCheckStatus.Warning,
                        _ => ValidationCheckStatus.Failed,
                    },
                    reusableEvidence?.Status switch
                    {
                        ExecutionEvidenceStatus.Passed => "Validation passed.",
                        ExecutionEvidenceStatus.Warning => reusableEvidence.ErrorSummary?.Value
                            ?? "Validation completed with a warning.",
                        _ => result.Outcome == ExecutionHandlerOutcome.Succeeded
                            ? "Validation passed."
                            : result.Error!.Summary,
                    },
                    reusableEvidence is null ? result.Error?.TechnicalDetail : null));
                if (result.Outcome == ExecutionHandlerOutcome.Failed && validator.Required)
                {
                    checkpoint = AppendError(checkpoint, result.Error!);
                    await SaveAsync(checkpoint).ConfigureAwait(false);
                    checkpoint = Transition(checkpoint, RunStatus.ValidationFailed);
                    await SaveAsync(checkpoint).ConfigureAwait(false);
                    return checkpoint;
                }

                await SaveAsync(checkpoint).ConfigureAwait(false);
            }

            var reusableScanEvidence = request.Mode == ExecutionMode.Resume
                ? FindReusableEvidence(
                    checkpoint,
                    ExecutionEvidenceKind.SecretScan,
                    "secret-scan",
                    allowWarning: false)
                : null;
            SecretScanResult scan;
            var scanStartedAt = _timeProvider.GetUtcNow();
            try
            {
                scan = await _secretScanner.ScanAsync(
                    SecretScanRequest.WholeWorkspace(staging.PayloadWorkspace).Value,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (IsRecoverableBoundaryFailure(exception))
            {
                var error = Error(
                    "DF-SECRET-001",
                    "The generated project could not be scanned safely.",
                    "The whole staged payload scan could not be completed.",
                    "secret-scan");
                checkpoint = AppendError(checkpoint, error);
                if (reusableScanEvidence is null)
                {
                    checkpoint = WithEvidence(
                        checkpoint,
                        ExecutionEvidence.Create(
                            ExecutionEvidenceKind.SecretScan,
                            "secret-scan",
                            ExecutionEvidenceStatus.Failed,
                            Digest("secret-scan:operational-failure"),
                            scanStartedAt,
                            _timeProvider.GetUtcNow(),
                            error.Code,
                            error.Summary).Value);
                }
                await SaveAsync(checkpoint).ConfigureAwait(false);
                checkpoint = Transition(checkpoint, RunStatus.ValidationFailed);
                await SaveAsync(checkpoint).ConfigureAwait(false);
                return checkpoint;
            }
            var scanDigest = Digest(string.Join(
                "\n",
                scan.Findings.OrderBy(finding => finding.Path.Value, StringComparer.Ordinal)
                    .Select(finding => $"{finding.Path.Value}:{finding.LineNumber}:{finding.Description.Value}")));
            if (!scan.Findings.IsEmpty)
            {
                var error = Error(
                    "DF-SECRET-001",
                    "Potential credential material was found in the generated project.",
                    "The whole staged payload scan found blocked credential-shaped content.",
                    "secret-scan");
                checkpoint = AppendError(checkpoint, error);
                if (reusableScanEvidence is null)
                {
                    checkpoint = WithEvidence(
                        checkpoint,
                        ExecutionEvidence.Create(
                            ExecutionEvidenceKind.SecretScan,
                            "secret-scan",
                            ExecutionEvidenceStatus.Failed,
                            scanDigest,
                            scanStartedAt,
                            _timeProvider.GetUtcNow(),
                            error.Code,
                            error.Summary).Value);
                }
                validations.Add(new ValidationCheck(
                    "whole-payload-secret-scan",
                    ValidationCheckStatus.Failed,
                    error.Summary,
                    error.TechnicalDetail));
                await SaveAsync(checkpoint).ConfigureAwait(false);
                checkpoint = Transition(checkpoint, RunStatus.ValidationFailed);
                await SaveAsync(checkpoint).ConfigureAwait(false);
                return checkpoint;
            }

            if (reusableScanEvidence is not null
                && !StringComparer.Ordinal.Equals(scanDigest, reusableScanEvidence.OutputDigest))
            {
                checkpoint = AppendError(checkpoint, Error(
                    "DF-SECRET-002",
                    "Previously passed secret-scan evidence no longer matches.",
                    "A resumed whole-payload scan produced evidence different from the persisted pass.",
                    "secret-scan"));
                await SaveAsync(checkpoint).ConfigureAwait(false);
                checkpoint = Transition(checkpoint, RunStatus.ValidationFailed);
                await SaveAsync(checkpoint).ConfigureAwait(false);
                return checkpoint;
            }

            if (reusableScanEvidence is null)
            {
                checkpoint = WithEvidence(
                    checkpoint,
                    ExecutionEvidence.Create(
                        ExecutionEvidenceKind.SecretScan,
                        "secret-scan",
                        ExecutionEvidenceStatus.Passed,
                        scanDigest,
                        scanStartedAt,
                        _timeProvider.GetUtcNow(),
                        null,
                        null).Value);
                validations.Add(new ValidationCheck(
                    "whole-payload-secret-scan",
                    ValidationCheckStatus.Passed,
                    "No credential-shaped content was found.",
                    null));
                await SaveAsync(checkpoint).ConfigureAwait(false);
            }
            else
            {
                validations.Add(new ValidationCheck(
                    "whole-payload-secret-scan",
                    ValidationCheckStatus.Passed,
                    "No credential-shaped content was found.",
                    null));
            }
            var reportResult = await CreateReportAsync(
                request,
                checkpoint,
                staging,
                validations,
                cancellationToken).ConfigureAwait(false);
            if (!reportResult.IsSuccessful)
            {
                checkpoint = AppendError(checkpoint, reportResult.Error!);
                await SaveAsync(checkpoint).ConfigureAwait(false);
                checkpoint = Transition(checkpoint, RunStatus.Failed);
                await SaveAsync(checkpoint).ConfigureAwait(false);
                return checkpoint;
            }

            var report = reportResult.Value;
            var evidence = await _projectEvidenceWriter.WriteAsync(
                checkpoint,
                report,
                staging.PayloadWorkspace,
                cancellationToken).ConfigureAwait(false);
            if (!evidence.IsSuccessful)
            {
                checkpoint = AppendError(checkpoint, evidence.Error!);
                await SaveAsync(checkpoint).ConfigureAwait(false);
                checkpoint = Transition(checkpoint, RunStatus.Failed);
                await SaveAsync(checkpoint).ConfigureAwait(false);
                return checkpoint;
            }

            checkpoint = Recreate(checkpoint, finalizationState: FinalizationState.IntentPersisted);
            await SaveAsync(checkpoint).ConfigureAwait(false);
            var finalized = await _finalizer.FinalizeAsync(
                checkpoint,
                staging,
                request.TargetParentWorkspace,
                cancellationToken).ConfigureAwait(false);
            if (!finalized.IsSuccessful)
            {
                checkpoint = Recreate(checkpoint, finalizationState: FinalizationState.Failed);
                checkpoint = AppendError(checkpoint, finalized.Error!);
                await SaveAsync(checkpoint).ConfigureAwait(false);
                checkpoint = Transition(checkpoint, RunStatus.Failed);
                await SaveAsync(checkpoint).ConfigureAwait(false);
                return checkpoint;
            }

            checkpoint = Recreate(
                checkpoint,
                finalizationState: FinalizationState.Succeeded,
                publication: PublicationSnapshot.CreateNotRequested(
                    finalized.Value.TreeDigest).Value);
            await SaveAsync(checkpoint).ConfigureAwait(false);
            checkpoint = await PersistReportAsync(
                request,
                checkpoint,
                report,
                CancellationToken.None).ConfigureAwait(false);
            if (checkpoint.ReportState != ReportPersistenceState.Succeeded)
            {
                return checkpoint;
            }

            checkpoint = Transition(checkpoint, RunStatus.LocalReady);
            await SaveAsync(checkpoint).ConfigureAwait(false);
            return checkpoint;
        }
        catch (OperationCanceledException)
        {
            checkpoint = await PersistCancellationAsync(checkpoint).ConfigureAwait(false);
            throw new RunCompletionCancelledException(checkpoint, cancellationToken);
        }
    }

    private async Task<RunCheckpoint> PersistReportAsync(
        ExecutionRequest request,
        RunCheckpoint checkpoint,
        GenerationReport report,
        CancellationToken cancellationToken)
    {
        var written = await _reportWriter.WriteAsync(
            checkpoint,
            report,
            request.RunArtifactWorkspace,
            cancellationToken).ConfigureAwait(false);
        if (written.IsSuccessful)
        {
            checkpoint = Recreate(checkpoint, reportState: ReportPersistenceState.Succeeded);
            await SaveAsync(checkpoint).ConfigureAwait(false);
            return checkpoint;
        }

        checkpoint = Recreate(checkpoint, reportState: ReportPersistenceState.Failed);
        checkpoint = AppendError(checkpoint, written.Error!);
        await SaveAsync(checkpoint).ConfigureAwait(false);
        if (checkpoint.Run.Status == RunStatus.Executing)
        {
            checkpoint = Transition(checkpoint, RunStatus.Failed);
            await SaveAsync(checkpoint).ConfigureAwait(false);
        }

        return checkpoint;
    }

    private async Task<ExecutionOperationResult<GenerationReport>> CreateReportAsync(
        ExecutionRequest request,
        RunCheckpoint checkpoint,
        StagingWorkspace staging,
        IReadOnlyCollection<ValidationCheck> validations,
        CancellationToken cancellationToken)
    {
        var preview = checkpoint.Preview ?? request.PlannedProject.Preview;
        var artifacts = new List<WorkspaceRelativePath>(preview.Artifacts.Length);
        foreach (var declared in preview.Artifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = WorkspaceRelativePath.Create(declared.Path);
            if (!path.IsValid
                || ProjectEvidencePathPolicy.IsReserved(path.Value)
                || !await staging.PayloadWorkspace.FileExistsAsync(path.Value, cancellationToken)
                    .ConfigureAwait(false))
            {
                return ExecutionOperationResult.Failure<GenerationReport>(Error(
                    "DF-EVIDENCE-001",
                    "Reviewed generated artifact evidence is incomplete.",
                    "A reviewed artifact was missing or reserved before evidence capture.",
                    "project-evidence"));
            }

            artifacts.Add(path.Value);
        }

        var report = GenerationReport.Create(
            checkpoint.Run.Id,
            _timeProvider.GetUtcNow(),
            validations,
            preview.ToolStatuses.Select(status =>
                new ReportToolStatus(
                    status.Id,
                    status.Required,
                    status.IsAvailable,
                    status.IsCompatible,
                    status.DetectedVersion)),
            preview.Warnings.Select(warning =>
                new ReportWarning(
                    warning.Code,
                    RedactedText.FromTrustedRedaction(warning.Message).Value)),
            checkpoint.Run.Errors,
            artifacts.OrderBy(path => path.Value, StringComparer.Ordinal)
                .Select(path => path.Value)).Value;
        return ExecutionOperationResult.Success(report);
    }

    private static async Task<ExecutionHandlerResult> ExecuteValidatorAsync(
        IExecutionHandler handler,
        ExecutionHandlerRequest request,
        IProgress<ExecutionProgressLine>? progress,
        CancellationToken cancellationToken)
    {
        var prepare = await handler.PrepareAsync(request, cancellationToken).ConfigureAwait(false);
        if (StopValidatorPhase(prepare, ExecutionPhase.Prepare, cancellationToken))
        {
            return prepare;
        }

        var precondition = await handler.CheckPreconditionsAsync(
            request,
            cancellationToken).ConfigureAwait(false);
        if (StopValidatorPhase(precondition, ExecutionPhase.Precondition, cancellationToken))
        {
            return precondition;
        }

        var execute = await handler.ExecuteAsync(request, progress, cancellationToken).ConfigureAwait(false);
        if (StopValidatorPhase(execute, ExecutionPhase.Execute, cancellationToken))
        {
            return execute;
        }

        var postcondition = await handler.CheckPostconditionsAsync(
            request,
            cancellationToken).ConfigureAwait(false);
        _ = StopValidatorPhase(postcondition, ExecutionPhase.Postcondition, cancellationToken);
        return postcondition.Outcome == ExecutionHandlerOutcome.Succeeded ? execute : postcondition;
    }

    private static bool StopValidatorPhase(
        ExecutionHandlerResult result,
        ExecutionPhase expectedPhase,
        CancellationToken cancellationToken)
    {
        if (result.Phase != expectedPhase)
        {
            throw new InvalidOperationException("The validator returned evidence for an unexpected phase.");
        }

        if (result.Outcome == ExecutionHandlerOutcome.Cancelled)
        {
            throw new OperationCanceledException(
                "Validator execution was cancelled.",
                cancellationToken);
        }

        return result.Outcome == ExecutionHandlerOutcome.Failed;
    }

    private async Task<RunCheckpoint> PersistCancellationAsync(RunCheckpoint checkpoint)
    {
        if (checkpoint.Run.Status == RunStatus.Executing)
        {
            checkpoint = Transition(checkpoint, RunStatus.Cancelled);
            await SaveAsync(checkpoint).ConfigureAwait(false);
        }

        return checkpoint;
    }

    private Task SaveAsync(RunCheckpoint checkpoint) =>
        _checkpointStore.SaveAsync(checkpoint, CancellationToken.None);

    private static RunCheckpoint AppendError(RunCheckpoint checkpoint, DevForgeError error) =>
        Recreate(checkpoint, run: checkpoint.Run.AppendError(error).Value);

    private static RunCheckpoint Transition(RunCheckpoint checkpoint, RunStatus status) =>
        Recreate(checkpoint, run: checkpoint.Run.TransitionTo(status).Value);

    private static RunCheckpoint WithEvidence(RunCheckpoint checkpoint, ExecutionEvidence evidence)
    {
        var items = checkpoint.Evidence
            .Where(item => item.Kind != evidence.Kind || !StringComparer.Ordinal.Equals(item.Id, evidence.Id))
            .Append(evidence)
            .ToImmutableArray();
        return Recreate(checkpoint, evidence: items);
    }

    private static ExecutionEvidence? FindReusableEvidence(
        RunCheckpoint checkpoint,
        ExecutionEvidenceKind kind,
        string id,
        bool allowWarning) => checkpoint.Evidence.FirstOrDefault(item =>
            item.Kind == kind
            && (item.Status == ExecutionEvidenceStatus.Passed
                || allowWarning && item.Status == ExecutionEvidenceStatus.Warning)
            && StringComparer.Ordinal.Equals(item.Id, id));

    private static RunCheckpoint Recreate(
        RunCheckpoint checkpoint,
        ProjectRun? run = null,
        ImmutableArray<ExecutionEvidence>? evidence = null,
        FinalizationState? finalizationState = null,
        ReportPersistenceState? reportState = null,
        PublicationSnapshot? publication = null) =>
        RunCheckpoint.Create(
            run ?? checkpoint.Run,
            checkpoint.Plan,
            checkpoint.Preview,
            checkpoint.Blueprint,
            checkpoint.BlueprintFingerprint,
            checkpoint.Staging,
            checkpoint.Target,
            checkpoint.RunArtifacts,
            evidence ?? checkpoint.Evidence,
            finalizationState ?? checkpoint.FinalizationState,
            reportState ?? checkpoint.ReportState,
            publication ?? checkpoint.Publication).Value;

    private static string Digest(string value) =>
        $"sha256:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))}";

    private static DevForgeError Error(
        string code,
        string summary,
        string detail,
        string phase) =>
        DevForgeError.Create(
            code,
            summary,
            RedactedText.FromTrustedRedaction(detail).Value,
            phase,
            null,
            false,
            [],
            []).Value;

    private static bool IsRecoverableBoundaryFailure(Exception exception) =>
        exception is not OutOfMemoryException
            and not StackOverflowException
            and not AccessViolationException;

    private static void ValidateBoundary(
        ExecutionRequest request,
        RunCheckpoint checkpoint,
        StagingWorkspace staging,
        BlueprintExecutionPackage package,
        IExecutionHandlerRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(staging);
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(registry);
        if (checkpoint.Run.Status != RunStatus.Executing
            || !checkpoint.Staging.Equals(staging.Descriptor)
            || !checkpoint.BlueprintFingerprint.Equals(package.Blueprint.Fingerprint))
        {
            throw new ExecutionCheckpointMismatchException();
        }
    }
}

internal sealed class RunCompletionCancelledException : OperationCanceledException
{
    public RunCompletionCancelledException(RunCheckpoint checkpoint, CancellationToken cancellationToken)
        : base("Run completion was cancelled after its checkpoint was persisted.", cancellationToken)
    {
        Checkpoint = checkpoint ?? throw new ArgumentNullException(nameof(checkpoint));
    }

    public RunCheckpoint Checkpoint { get; }
}
