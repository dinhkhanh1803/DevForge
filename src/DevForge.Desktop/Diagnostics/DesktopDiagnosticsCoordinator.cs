using DevForge.Application.Contracts;
using DevForge.Desktop.EnvironmentDoctor;
using DevForge.Desktop.Notifications;
using DevForge.Domain.Diagnostics;
using DevForge.Domain.Privacy;

namespace DevForge.Desktop.Diagnostics;

public sealed class DesktopDiagnosticsCoordinator
{
    private readonly ISupportBundleCoordinator _bundleCoordinator;
    private readonly ISupportBundleCleanupService _cleanupService;
    private readonly IClipboardService _clipboard;
    private readonly NotificationService _notifications;
    private bool _isReadOnly;

    public DesktopDiagnosticsCoordinator(
        ISupportBundleCoordinator bundleCoordinator,
        ISupportBundleCleanupService cleanupService,
        IClipboardService clipboard,
        NotificationService notifications)
    {
        _bundleCoordinator = bundleCoordinator
            ?? throw new ArgumentNullException(nameof(bundleCoordinator));
        _cleanupService = cleanupService
            ?? throw new ArgumentNullException(nameof(cleanupService));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
    }

    public async Task<ExecutionOperationResult<SupportBundleReceipt>> ExportAsync(
        string runId,
        CancellationToken cancellationToken)
    {
        var request = SupportBundleRequest.Create(runId, includeEnvironmentSnapshot: true);
        if (!request.IsValid)
        {
            return Failure<SupportBundleReceipt>(
                "DF-DIAG-AUTHORITY",
                "The support bundle request was not authoritative.");
        }

        var result = await _bundleCoordinator.ExportAsync(
            request.Value,
            cancellationToken).ConfigureAwait(false);
        _notifications.TryPublish(
            result.IsSuccessful ? NotificationSeverity.Information : NotificationSeverity.Error,
            result.IsSuccessful
                ? "Support bundle created in local diagnostics storage."
                : "The support bundle could not be created safely.");
        return result;
    }

    public bool CopyReceipt(SupportBundleReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        try
        {
            _clipboard.SetText(
                $"Bundle: {receipt.RelativePath.Value}{Environment.NewLine}" +
                $"SHA-256: {receipt.Sha256}{Environment.NewLine}" +
                $"Bytes: {receipt.Length}");
            _notifications.TryPublish(
                NotificationSeverity.Information,
                "Support bundle receipt copied.");
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or ArgumentException
            or System.Runtime.InteropServices.ExternalException)
        {
            _notifications.TryPublish(
                NotificationSeverity.Error,
                "The support bundle receipt could not be copied.");
            return false;
        }
    }

    public Task<ExecutionOperationResult<SupportBundleCleanupReceipt>> CleanupAsync(
        SupportBundleReceipt receipt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return _isReadOnly
            ? Task.FromResult(Failure<SupportBundleCleanupReceipt>(
                "DF-DIAG-READONLY",
                "Support bundle cleanup is disabled in safe mode."))
            : _cleanupService.CleanupAsync(receipt, cancellationToken);
    }

    public void EnterReadOnlyMode() => _isReadOnly = true;

    private static ExecutionOperationResult<T> Failure<T>(string code, string summary)
        where T : class =>
        ExecutionOperationResult.Failure<T>(DevForgeError.Create(
            code,
            summary,
            RedactedText.FromTrustedRedaction(summary).Value,
            "diagnostics",
            stepId: null,
            isRetryable: false,
            suggestedActions: [],
            redactedContext: []).Value);
}
