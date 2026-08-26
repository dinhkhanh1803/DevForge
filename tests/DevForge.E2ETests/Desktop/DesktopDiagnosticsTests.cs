using DevForge.Application.Contracts;
using DevForge.Desktop.Diagnostics;
using DevForge.Desktop.EnvironmentDoctor;
using DevForge.Desktop.Notifications;
using DevForge.Domain.Diagnostics;
using DevForge.Domain.Execution;
using DevForge.Domain.Privacy;

namespace DevForge.E2ETests.Desktop;

public sealed class DesktopDiagnosticsTests
{
    [Fact]
    public async Task ExportAndCopyUseOnlyTypedRelativeReceiptEvidence()
    {
        var clipboard = new RecordingClipboard();
        var notifications = new NotificationService();
        var receipt = CreateReceipt();
        var exporter = new RecordingExporter(receipt);
        var sut = new DesktopDiagnosticsCoordinator(
            exporter,
            new RecordingCleanup(),
            clipboard,
            notifications);

        var result = await sut.ExportAsync("run-001", CancellationToken.None);
        sut.CopyReceipt(result.Value);

        Assert.Equal("run-001", exporter.RunId);
        Assert.Contains(receipt.RelativePath.Value, clipboard.Text, StringComparison.Ordinal);
        Assert.Contains(receipt.Sha256, clipboard.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\", clipboard.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(notifications.Items, item =>
            item.Message == "Support bundle created in local diagnostics storage.");
    }

    [Fact]
    public async Task SafeModeRefusesOwnedBundleCleanupWithoutCallingInfrastructure()
    {
        var cleanup = new RecordingCleanup();
        var sut = new DesktopDiagnosticsCoordinator(
            new RecordingExporter(CreateReceipt()),
            cleanup,
            new RecordingClipboard(),
            new NotificationService());
        sut.EnterReadOnlyMode();

        var result = await sut.CleanupAsync(CreateReceipt(), CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.False(cleanup.WasCalled);
        Assert.NotNull(result.Error);
        Assert.Equal("DF-DIAG-READONLY", result.Error.Code);
    }

    private static SupportBundleReceipt CreateReceipt() => SupportBundleReceipt.Create(
        "bundle-001",
        WorkspaceRelativePath.Create("support-bundles\\bundle-001.zip").Value,
        new string('a', 64),
        123,
        DateTimeOffset.UnixEpoch).Value;

    private sealed class RecordingExporter(SupportBundleReceipt receipt) : ISupportBundleCoordinator
    {
        public string? RunId { get; private set; }

        public Task<ExecutionOperationResult<SupportBundleReceipt>> ExportAsync(
            SupportBundleRequest request,
            CancellationToken cancellationToken)
        {
            RunId = request.RunId;
            return Task.FromResult(ExecutionOperationResult.Success(receipt));
        }
    }

    private sealed class RecordingCleanup : ISupportBundleCleanupService
    {
        public bool WasCalled { get; private set; }

        public Task<ExecutionOperationResult<SupportBundleCleanupReceipt>> CleanupAsync(
            SupportBundleReceipt receipt,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.FromResult(ExecutionOperationResult.Success(
                new SupportBundleCleanupReceipt(receipt.BundleId, WasPresent: true)));
        }
    }

    private sealed class RecordingClipboard : IClipboardService
    {
        public string Text { get; private set; } = string.Empty;

        public void SetText(string text) => Text = text;
    }
}
