using DevForge.Application.Contracts;
using DevForge.Desktop.Diagnostics;

namespace DevForge.Desktop.RunHistory;

public sealed class RunHistoryActionCoordinator
{
    private bool _isReadOnly;
    private readonly IProjectRecoveryWorkflow _workflow;
    private readonly DesktopDiagnosticsCoordinator? _diagnostics;

    public RunHistoryActionCoordinator(
        IProjectRecoveryWorkflow workflow,
        IProjectPublicationWorkflow publicationWorkflow)
        : this(workflow, publicationWorkflow, diagnostics: null)
    {
    }

    public RunHistoryActionCoordinator(
        IProjectRecoveryWorkflow workflow,
        IProjectPublicationWorkflow publicationWorkflow,
        DesktopDiagnosticsCoordinator? diagnostics)
    {
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        PublicationWorkflow = publicationWorkflow
            ?? throw new ArgumentNullException(nameof(publicationWorkflow));
        _diagnostics = diagnostics;
    }

    internal IProjectPublicationWorkflow PublicationWorkflow { get; }

    public bool CanExportSupportBundle => _diagnostics is not null;

    public Task<ProjectRecoveryEligibility> InspectAsync(
        RunCheckpoint checkpoint,
        CancellationToken cancellationToken) =>
        _workflow.InspectAsync(checkpoint, cancellationToken);

    public Task<ProjectRecoverySnapshot> ContinueAsync(
        string runId,
        ExecutionMode mode,
        CancellationToken cancellationToken) =>
        _workflow.ContinueAsync(runId, mode, cancellationToken);

    public Task CleanupAsync(string runId, CancellationToken cancellationToken) =>
        _workflow.CleanupAsync(runId, cancellationToken);

    public void EnterReadOnlyMode()
    {
        _isReadOnly = true;
        _diagnostics?.EnterReadOnlyMode();
    }

    public Task<ExecutionOperationResult<SupportBundleReceipt>> ExportSupportBundleAsync(
        string runId,
        CancellationToken cancellationToken) =>
        _diagnostics is null
            ? throw new InvalidOperationException("Desktop diagnostics are unavailable.")
            : _diagnostics.ExportAsync(runId, cancellationToken);

    public bool CopySupportBundleReceipt(SupportBundleReceipt receipt) =>
        _diagnostics is not null && _diagnostics.CopyReceipt(receipt);

    public Task<ExecutionOperationResult<ProjectPublicationOutcome>> RetryPublicationAsync(
        string runId,
        CancellationToken cancellationToken) =>
        PublicationWorkflow.CompleteAsync(
            runId,
            _isReadOnly
                ? PublicationMutationMode.SafeReadOnly
                : PublicationMutationMode.Normal,
            cancellationToken);
}
