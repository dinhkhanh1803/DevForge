using System.Reflection;
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

    [Theory]
    [MemberData(nameof(SecretStructuredValues))]
    public void SerializerRedactsSecretShapedStructuredValuesEvenWhenFactoryIsBypassed(
        int constructorArgumentIndex,
        string credential)
    {
        var constructor = typeof(DiagnosticEvent).GetConstructors(
                BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate => candidate.GetParameters().Length == 10);
        object?[] arguments =
        [
            new DateTimeOffset(2026, 8, 26, 7, 30, 0, TimeSpan.Zero),
            DiagnosticLevel.Information,
            "execution.step.completed",
            "run-001",
            "restore",
            1,
            "execution-orchestrator",
            RedactedText.FromTrustedRedaction("Restore completed.").Value,
            125L,
            "DF-EXEC-001",
        ];
        arguments[constructorArgumentIndex] = credential;
        var diagnosticEvent = Assert.IsType<DiagnosticEvent>(constructor.Invoke(arguments));

        var text = Encoding.UTF8.GetString(DiagnosticEventNormalizer.Serialize(diagnosticEvent));

        Assert.DoesNotContain(credential, text, StringComparison.Ordinal);
        Assert.Contains("redacted", text, StringComparison.Ordinal);
    }

    public static TheoryData<int, string> SecretStructuredValues()
    {
        var data = new TheoryData<int, string>();
        foreach (var argumentIndex in new[] { 2, 3, 4, 6, 9 })
        {
            data.Add(argumentIndex, "ghp_abcdefghijklmnop");
            data.Add(argumentIndex, "Bearer abcdefghijklmnop");
            data.Add(argumentIndex, "password=hunter2");
        }

        return data;
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
