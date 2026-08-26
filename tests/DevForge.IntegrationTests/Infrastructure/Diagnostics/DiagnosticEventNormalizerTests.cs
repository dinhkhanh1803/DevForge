using System.Text;
using System.Text.Json;
using DevForge.Application.Contracts;
using DevForge.Domain.Privacy;
using DevForge.Infrastructure.Diagnostics;

namespace DevForge.IntegrationTests.Infrastructure.Diagnostics;

public sealed class DiagnosticEventNormalizerTests
{
    [Fact]
    public void SerializesCanonicalJsonLineWithFixedPropertyOrderAndNormalizedControls()
    {
        var diagnosticEvent = CreateEvent("safe\r\nforged\tline");

        var bytes = DiagnosticEventNormalizer.Serialize(diagnosticEvent);
        var text = Encoding.UTF8.GetString(bytes.AsSpan(0, bytes.Length - 1));
        using var document = JsonDocument.Parse(text);

        Assert.Equal(
            [
                "timestampUtc",
                "level",
                "eventId",
                "runId",
                "stepId",
                "attempt",
                "source",
                "message",
                "durationMs",
                "errorCode",
            ],
            document.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal("safe  forged line", document.RootElement.GetProperty("message").GetString());
        Assert.Equal((byte)'\n', bytes[^1]);
    }

    [Fact]
    public void ReplacesOversizedMessageAndCapsThePhysicalLine()
    {
        var diagnosticEvent = CreateEvent(new string('x', 40_000));

        var bytes = DiagnosticEventNormalizer.Serialize(diagnosticEvent);
        using var document = JsonDocument.Parse(bytes.AsMemory(0, bytes.Length - 1));

        Assert.True(bytes.Length <= DiagnosticEventNormalizer.MaximumEventBytes);
        Assert.Equal(
            "[DIAGNOSTIC TRUNCATED]",
            document.RootElement.GetProperty("message").GetString());
    }

    private static DiagnosticEvent CreateEvent(string message) =>
        DiagnosticEvent.Create(
            new DateTimeOffset(2026, 8, 26, 7, 30, 0, TimeSpan.Zero),
            DiagnosticLevel.Information,
            "execution.step.completed",
            "run-001",
            "restore",
            1,
            "execution-orchestrator",
            RedactedText.FromTrustedRedaction(message).Value,
            125,
            null).Value;
}
