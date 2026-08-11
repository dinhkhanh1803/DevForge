using System.Text.Json;
using System.Text.Json.Serialization;
using DevForge.Application.Contracts;

namespace DevForge.Infrastructure.Execution;

internal sealed record StagingOwnershipMarker(
    string MarkerId,
    string RunId,
    string PlanHash,
    BlueprintReference Blueprint,
    string BlueprintChecksum);

internal static class StagingOwnershipMarkerCodec
{
    public const int MaximumMarkerBytes = 16_384;
    private const int SchemaVersion = 1;
    private const string LifecycleIntent = "staging";
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        MaxDepth = 16,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static byte[] Encode(StagingOwnershipMarker marker)
    {
        ArgumentNullException.ThrowIfNull(marker);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(ToDto(marker), _jsonOptions);
        if (bytes.Length > MaximumMarkerBytes)
        {
            throw new InvalidStagingMarkerException();
        }

        return bytes;
    }

    public static StagingOwnershipMarker Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        try
        {
            if (bytes.Length is 0 or > MaximumMarkerBytes)
            {
                throw new InvalidStagingMarkerException();
            }

            using (var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 16,
            }))
            {
                RejectDuplicateProperties(document.RootElement);
            }

            var dto = JsonSerializer.Deserialize<MarkerDto>(bytes, _jsonOptions)
                ?? throw new InvalidStagingMarkerException();
            var marker = FromDto(dto);
            if (!bytes.SequenceEqual(Encode(marker)))
            {
                throw new InvalidStagingMarkerException();
            }

            return marker;
        }
        catch (Exception exception) when (exception is JsonException
            or ArgumentException
            or InvalidOperationException
            or InvalidStagingMarkerException)
        {
            throw new InvalidStagingMarkerException();
        }
    }

    private static MarkerDto ToDto(StagingOwnershipMarker marker)
    {
        return new MarkerDto
        {
            SchemaVersion = SchemaVersion,
            MarkerId = marker.MarkerId,
            RunId = marker.RunId,
            PlanHash = marker.PlanHash,
            BlueprintId = marker.Blueprint.Id,
            BlueprintVersion = marker.Blueprint.Version,
            BlueprintChecksum = marker.BlueprintChecksum,
            LifecycleIntent = LifecycleIntent,
        };
    }

    private static StagingOwnershipMarker FromDto(MarkerDto dto)
    {
        if (dto.SchemaVersion != SchemaVersion
            || !StringComparer.Ordinal.Equals(dto.LifecycleIntent, LifecycleIntent)
            || !IsBoundedIdentifier(dto.MarkerId)
            || !IsBoundedIdentifier(dto.RunId)
            || !IsCanonicalDigest(dto.PlanHash))
        {
            throw new InvalidStagingMarkerException();
        }

        var blueprint = BlueprintReference.Create(dto.BlueprintId, dto.BlueprintVersion);
        if (!blueprint.IsValid
            || !IsCanonicalDigest(dto.BlueprintChecksum))
        {
            throw new InvalidStagingMarkerException();
        }

        return new StagingOwnershipMarker(
            dto.MarkerId!,
            dto.RunId!,
            dto.PlanHash!,
            blueprint.Value,
            dto.BlueprintChecksum!);
    }

    private static bool IsCanonicalDigest(string? value)
    {
        const string prefix = "sha256:";
        if (value is null
            || value.Length != prefix.Length + 64
            || !value.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var character in value.AsSpan(prefix.Length))
        {
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsBoundedIdentifier(string? value)
    {
        return value is not null
            && value.Length is >= 1 and <= 128
            && value[0] is >= 'a' and <= 'z' or >= '0' and <= '9'
            && value.All(character =>
                character is >= 'a' and <= 'z'
                    or >= '0' and <= '9'
                    or '-'
                    or '.');
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new InvalidStagingMarkerException();
                }

                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                RejectDuplicateProperties(item);
            }
        }
    }

    private sealed class MarkerDto
    {
        public int SchemaVersion { get; set; }

        public string? MarkerId { get; set; }

        public string? RunId { get; set; }

        public string? PlanHash { get; set; }

        public string? BlueprintId { get; set; }

        public string? BlueprintVersion { get; set; }

        public string? BlueprintChecksum { get; set; }

        public string? LifecycleIntent { get; set; }
    }
}

internal sealed class InvalidStagingMarkerException : Exception;
