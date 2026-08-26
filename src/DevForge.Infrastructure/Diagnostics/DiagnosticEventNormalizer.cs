using System.Globalization;
using System.Text;
using System.Text.Json;
using DevForge.Application.Contracts;
using DevForge.Domain.Privacy;

namespace DevForge.Infrastructure.Diagnostics;

internal static class DiagnosticEventNormalizer
{
    internal const int MaximumEventBytes = 32 * 1024;
    internal const int MaximumMessageCharacters = 4 * 1024;
    private const string TruncatedMessage = "[DIAGNOSTIC TRUNCATED]";
    private const string RedactedMessage = "[DIAGNOSTIC REDACTED]";

    internal static byte[] Serialize(DiagnosticEvent diagnosticEvent)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);
        var message = NormalizeMessage(diagnosticEvent.Message.Value);
        var serialized = SerializeCore(diagnosticEvent, message);
        return serialized.Length <= MaximumEventBytes
            ? serialized
            : SerializeCore(diagnosticEvent, TruncatedMessage);
    }

    private static string NormalizeMessage(string value)
    {
        if (value.Length > MaximumMessageCharacters)
        {
            return TruncatedMessage;
        }

        var normalized = string.Create(
            value.Length,
            value,
            static (span, source) =>
            {
                for (var index = 0; index < source.Length; index++)
                {
                    span[index] = char.IsControl(source[index]) ? ' ' : source[index];
                }
            });
        return RedactedText.IsSecretShapedValue(normalized)
            || RedactedText.IsSourceShapedContent(normalized)
                ? RedactedMessage
                : normalized;
    }

    private static byte[] SerializeCore(DiagnosticEvent diagnosticEvent, string message)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(
                   stream,
                   new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString(
                "timestampUtc",
                diagnosticEvent.TimestampUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteString("level", diagnosticEvent.Level.ToString());
            writer.WriteString("eventId", NormalizeStructured(diagnosticEvent.EventId));
            WriteNullableString(writer, "runId", NormalizeStructured(diagnosticEvent.RunId));
            WriteNullableString(writer, "stepId", NormalizeStructured(diagnosticEvent.StepId));
            WriteNullableNumber(writer, "attempt", diagnosticEvent.Attempt);
            writer.WriteString("source", NormalizeStructured(diagnosticEvent.Source));
            writer.WriteString("message", message);
            WriteNullableNumber(writer, "durationMs", diagnosticEvent.DurationMs);
            WriteNullableString(writer, "errorCode", NormalizeStructured(diagnosticEvent.ErrorCode));
            writer.WriteEndObject();
        }

        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    private static string? NormalizeStructured(string? value)
    {
        return value is not null
            && (RedactedText.IsSecretShapedValue(value) || RedactedText.IsSecretShapedKey(value))
                ? "redacted"
                : value;
    }

    private static void WriteNullableString(
        Utf8JsonWriter writer,
        string propertyName,
        string? value)
    {
        if (value is null)
        {
            writer.WriteNull(propertyName);
        }
        else
        {
            writer.WriteString(propertyName, value);
        }
    }

    private static void WriteNullableNumber(
        Utf8JsonWriter writer,
        string propertyName,
        long? value)
    {
        if (value is null)
        {
            writer.WriteNull(propertyName);
        }
        else
        {
            writer.WriteNumber(propertyName, value.Value);
        }
    }
}
