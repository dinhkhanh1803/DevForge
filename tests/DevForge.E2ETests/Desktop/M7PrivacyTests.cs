using DevForge.Application.Contracts;
using DevForge.Application.Contracts.Persistence;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Desktop.EnvironmentDoctor;
using DevForge.Desktop.Notifications;
using DevForge.Domain.Privacy;

namespace DevForge.E2ETests.Desktop;

public sealed class M7PrivacyTests
{
    public static TheoryData<string> UnsafeUiText => new()
    {
        "password=hunter2",
        "Server=localhost;Password=hunter2;Database=app",
        ".env contents: TOKEN=hunter2",
        "-----BEGIN PRIVATE KEY-----",
        "Authorization: Bearer abcdefghijklmnop",
        "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.abcdefghijklmnop",
    };

    [Theory]
    [MemberData(nameof(UnsafeUiText))]
    public void UnsafeTextCannotEnterPresetNotificationOrProgressContracts(string value)
    {
        var input = DynamicInputValue.Text(value);
        Assert.False(input.IsValid);
        Assert.Contains(input.Issues, issue => issue.Code is
            "creation.input.text.secret-shaped" or "creation.input.text.source-content");

        var notifications = new NotificationService();
        Assert.False(notifications.TryPublish(NotificationSeverity.Error, value));
        Assert.Empty(notifications.Items);

        var redacted = RedactedText.FromTrustedRedaction(value);
        Assert.False(redacted.IsValid);
    }

    [Fact]
    public void SourceLikeTextIsAllowedForAProjectButCannotEnterPresetsNotificationsOrProgress()
    {
        const string value = "using System; public sealed class GeneratedProject { }";
        var input = DynamicInputValue.Text(value);
        Assert.True(input.IsValid);
        var preset = ProjectCreationPresetDraft.Create(
            BlueprintReference.Create("sample.local", "1.0.0").Value,
            new Dictionary<string, DynamicInputValue?> { ["description"] = input.Value },
            [],
            "none");
        Assert.False(preset.IsValid);
        Assert.Contains(preset.Issues, issue => issue.Code == "creation.preset.input.source-content");

        var notifications = new NotificationService();
        Assert.False(notifications.TryPublish(NotificationSeverity.Error, value));
        Assert.False(RedactedText.FromTrustedRedaction(value).IsValid);
    }

    [Fact]
    public async Task RawExceptionsNeverEnterNotificationsOrDiagnostics()
    {
        const string raw = "C:\\secret\\source.cs token=hunter2\nraw stack trace";
        var notifications = new NotificationService();
        var clipboard = new CapturingClipboard();
        var sut = new EnvironmentDoctorViewModel(
            new ThrowingDoctorService(new InvalidOperationException(raw)),
            clipboard,
            notifications);

        await sut.LoadAsync(CancellationToken.None);

        var notification = Assert.Single(notifications.Items);
        Assert.Equal("Environment health could not be loaded.", notification.Message);
        Assert.DoesNotContain(raw, notification.Message, StringComparison.Ordinal);
        Assert.Null(clipboard.Text);
    }

    [Fact]
    public async Task DiagnosticsProjectionDoesNotExposeSecretOrSourceLikeVersionValues()
    {
        var scannedAt = DateTimeOffset.UnixEpoch;
        var sut = new EnvironmentDoctorViewModel(
            new ThrowingDoctorService(new InvalidOperationException()),
            new CapturingClipboard(),
            new NotificationService());
        sut.ApplySnapshot(new EnvironmentHealthSnapshot(
        [
            new EnvironmentHealthItem("git", "password=hunter2", EnvironmentToolStatus.Unknown, scannedAt),
            new EnvironmentHealthItem("dotnet", "using System; public class Leak { }", EnvironmentToolStatus.Unknown, scannedAt),
        ], scannedAt, EnvironmentSnapshotSource.Cache, true, false));
        var clipboard = new CapturingClipboard();
        sut = new EnvironmentDoctorViewModel(
            new StaticDoctorService(sut.Tools.ToArray(), scannedAt),
            clipboard,
            new NotificationService());

        await sut.LoadAsync(CancellationToken.None);
        sut.CopyDiagnosticsCommand.Execute(null);

        Assert.NotNull(clipboard.Text);
        Assert.DoesNotContain("hunter2", clipboard.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("public class", clipboard.Text, StringComparison.Ordinal);
        Assert.Contains("unknown", clipboard.Text, StringComparison.Ordinal);
    }

    private sealed class ThrowingDoctorService(Exception exception) : IEnvironmentDoctorService
    {
        public Task<EnvironmentHealthSnapshot> LoadAsync(bool forceRefresh, CancellationToken cancellationToken) =>
            Task.FromException<EnvironmentHealthSnapshot>(exception);

        public Task<EnvironmentHealthSnapshot> LoadCachedAsync(CancellationToken cancellationToken) =>
            Task.FromException<EnvironmentHealthSnapshot>(exception);
    }

    private sealed class StaticDoctorService(
        IReadOnlyCollection<EnvironmentHealthItem> tools,
        DateTimeOffset scannedAt) : IEnvironmentDoctorService
    {
        private readonly EnvironmentHealthSnapshot _snapshot = new(
            [.. tools], scannedAt, EnvironmentSnapshotSource.Cache, true, false);

        public Task<EnvironmentHealthSnapshot> LoadAsync(bool forceRefresh, CancellationToken cancellationToken) =>
            Task.FromResult(_snapshot);

        public Task<EnvironmentHealthSnapshot> LoadCachedAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_snapshot);
    }

    private sealed class CapturingClipboard : IClipboardService
    {
        public string? Text { get; private set; }

        public void SetText(string text) => Text = text;
    }
}
