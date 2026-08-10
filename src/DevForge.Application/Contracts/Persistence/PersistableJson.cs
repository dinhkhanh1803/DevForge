using System.Text;
using System.Text.Json;
using DevForge.Domain.Privacy;
using DevForge.Domain.Validation;

namespace DevForge.Application.Contracts.Persistence;

public sealed record PersistableJson
{
    public const int MaxUtf8ByteCount = 65_536;

    private PersistableJson(string value, int utf8ByteCount)
    {
        Value = value;
        Utf8ByteCount = utf8ByteCount;
    }

    public string Value { get; }

    public int Utf8ByteCount { get; }

    public static ValidationResult<PersistableJson> Create(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Failure("persistence.json.required", "A JSON object is required.");
        }

        if (Encoding.UTF8.GetByteCount(json) > MaxUtf8ByteCount)
        {
            return Failure("persistence.json.too-large", "The JSON payload exceeds the persistence limit.");
        }

        try
        {
            using var document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 32,
                });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Failure("persistence.json.root.invalid", "The persisted JSON root must be an object.");
            }

            var validationCode = FindValidationCode(document.RootElement);
            if (validationCode is not null)
            {
                return Failure(
                    validationCode,
                    validationCode == "persistence.json.property.duplicate"
                        ? "JSON property names must be unique within an object."
                        : "The JSON payload resembles credential material and cannot be persisted.");
            }

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
            {
                WriteCanonical(writer, document.RootElement);
            }

            if (stream.Length > MaxUtf8ByteCount)
            {
                return Failure("persistence.json.too-large", "The JSON payload exceeds the persistence limit.");
            }

            var value = Encoding.UTF8.GetString(stream.ToArray());
            return ValidationResult.Success(new PersistableJson(value, checked((int)stream.Length)));
        }
        catch (JsonException)
        {
            return Failure("persistence.json.invalid", "The JSON payload is invalid.");
        }
    }

    public override string ToString()
    {
        return "[PERSISTABLE-JSON]";
    }

    private static string? FindValidationCode(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                    {
                        return "persistence.json.property.duplicate";
                    }

                    if (RedactedText.IsSecretShapedKey(property.Name))
                    {
                        return "persistence.json.secret-detected";
                    }

                    var childCode = FindValidationCode(property.Value);
                    if (childCode is not null)
                    {
                        return childCode;
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    var childCode = FindValidationCode(item);
                    if (childCode is not null)
                    {
                        return childCode;
                    }
                }

                break;
            case JsonValueKind.String:
                var value = element.GetString();
                if (!string.IsNullOrWhiteSpace(value)
                    && !RedactedText.FromTrustedRedaction(value).IsValid)
                {
                    return "persistence.json.secret-detected";
                }

                break;
        }

        return null;
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(
                    property => property.Name,
                    StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static ValidationResult<PersistableJson> Failure(string code, string message)
    {
        return ValidationResult.Failure<PersistableJson>(
        [
            new ValidationIssue(code, message, "json"),
        ]);
    }
}
