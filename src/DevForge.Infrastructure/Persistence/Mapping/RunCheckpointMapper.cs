using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevForge.Application.Contracts;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Validation;
using DevForge.Infrastructure.Persistence.Entities;

namespace DevForge.Infrastructure.Persistence.Mapping;

internal static class RunCheckpointMapper
{
    private const int MaximumEvidenceJsonBytes = 262_144;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        MaxDepth = 32,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static ProjectRunEntity CreateEntity(RunCheckpoint checkpoint, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        var entity = RunJournalMapper.CreateEntity(checkpoint.Run, now);
        ApplyCheckpoint(entity, checkpoint);
        return entity;
    }

    public static void UpdateEntity(
        ProjectRunEntity entity,
        RunCheckpoint checkpoint,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(checkpoint);
        RunJournalMapper.UpdateEntity(entity, checkpoint.Run, now, allowCheckpointUpdate: true);
        ApplyCheckpoint(entity, checkpoint);
    }

    public static IReadOnlyList<RunStepEntity> CreateStepEntities(RunCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        var steps = RunJournalMapper.CreateStepEntities(checkpoint.Run);
        for (var index = 0; index < steps.Count; index++)
        {
            steps[index].SequenceNumber = index;
        }

        return steps;
    }

    public static RunCheckpoint ToModel(ProjectRunEntity entity)
    {
        try
        {
            EnsureComplete(entity);
            var plan = CheckpointPlanCodec.Decode(
                entity.PlanJson!,
                entity.PlanBodyChecksum!,
                entity.PlanHash!);
            var run = RunJournalMapper.ToModel(entity);
            var blueprint = RequireValid(BlueprintReference.Create(
                entity.BlueprintId,
                entity.BlueprintVersion));
            var fingerprint = RequireValid(BlueprintFingerprint.Create(
                entity.BlueprintSourceId,
                RequireValid(WorkspaceRelativePath.Create(entity.BlueprintPackageDirectory)),
                ParseDefined<BlueprintTrust>(entity.BlueprintTrust),
                entity.BlueprintChecksum));
            var staging = RequireValid(StagingDescriptor.Create(
                RequireValid(WorkspaceRelativePath.Create(entity.StagingPath)),
                RequireValid(WorkspaceRelativePath.Create(entity.StagingPayloadPath)),
                RequireValid(WorkspaceRelativePath.Create(entity.OwnershipMarkerPath)),
                entity.OwnershipMarkerId));
            var target = RequireValid(TargetDescriptor.Create(
                RequireValid(WorkspaceRoot.Create(entity.TargetParentRoot)),
                RequireValid(WorkspaceRelativePath.Create(entity.TargetPath)),
                entity.CrossVolumeTemporaryPath is null
                    ? null
                    : RequireValid(WorkspaceRelativePath.Create(entity.CrossVolumeTemporaryPath))));
            var runArtifacts = RequireValid(RunArtifactDescriptor.Create(
                RequireValid(WorkspaceRoot.Create(entity.RunArtifactRoot))));
            var preview = entity.PlanPreviewJson is null
                ? null
                : CheckpointPreviewCodec.Decode(
                    entity.PlanPreviewJson,
                    entity.PlanPreviewBodyChecksum!);
            var publication = entity.PublicationJson is null
                ? null
                : CheckpointPublicationCodec.Decode(
                    entity.PublicationJson,
                    entity.PublicationBodyChecksum!);
            return RequireValid(publication is null
                ? RunCheckpoint.Create(
                    run,
                    plan,
                    preview,
                    blueprint,
                    fingerprint,
                    staging,
                    target,
                    runArtifacts,
                    DeserializeEvidence(entity.EvidenceJson!),
                    ParseDefined<FinalizationState>(entity.FinalizationState),
                    ParseDefined<ReportPersistenceState>(entity.ReportState))
                : RunCheckpoint.Create(
                run,
                plan,
                preview,
                blueprint,
                fingerprint,
                staging,
                target,
                runArtifacts,
                DeserializeEvidence(entity.EvidenceJson!),
                ParseDefined<FinalizationState>(entity.FinalizationState),
                ParseDefined<ReportPersistenceState>(entity.ReportState),
                publication));
        }
        catch (Exception exception) when (IsDataException(exception))
        {
            throw new PersistenceDataException();
        }
    }

    private static void ApplyCheckpoint(ProjectRunEntity entity, RunCheckpoint checkpoint)
    {
        var encodedPlan = CheckpointPlanCodec.Encode(checkpoint.Plan);
        entity.PlanHash = checkpoint.PlanHash;
        entity.PlanJson = encodedPlan.Json;
        entity.PlanBodyChecksum = encodedPlan.BodyChecksum;
        var encodedPreview = checkpoint.Preview is null
            ? null
            : CheckpointPreviewCodec.Encode(checkpoint.Preview);
        entity.PlanPreviewJson = encodedPreview?.Json;
        entity.PlanPreviewBodyChecksum = encodedPreview?.BodyChecksum;
        entity.BlueprintId = checkpoint.Blueprint.Id;
        entity.BlueprintVersion = checkpoint.Blueprint.Version;
        entity.BlueprintSourceId = checkpoint.BlueprintFingerprint.SourceId;
        entity.BlueprintPackageDirectory = checkpoint.BlueprintFingerprint.PackageDirectory.Value;
        entity.BlueprintTrust = checkpoint.BlueprintFingerprint.Trust.ToString();
        entity.BlueprintChecksum = checkpoint.BlueprintFingerprint.AggregateChecksum;
        entity.StagingPath = checkpoint.Staging.ContainerDirectory.Value;
        entity.StagingPayloadPath = checkpoint.Staging.PayloadDirectory.Value;
        entity.OwnershipMarkerPath = checkpoint.Staging.MarkerFile.Value;
        entity.OwnershipMarkerId = checkpoint.Staging.MarkerId;
        entity.TargetParentRoot = checkpoint.Target.ParentRoot.RevealForFileSystem();
        entity.TargetPath = checkpoint.Target.TargetDirectory.Value;
        entity.CrossVolumeTemporaryPath = checkpoint.Target.CrossVolumeTemporaryDirectory?.Value;
        entity.RunArtifactRoot = checkpoint.RunArtifacts.Root.RevealForFileSystem();
        entity.EvidenceJson = SerializeEvidence(checkpoint.Evidence);
        entity.FinalizationState = checkpoint.FinalizationState.ToString();
        entity.ReportState = checkpoint.ReportState.ToString();
        if (checkpoint.Publication.FinalTreeDigest is null)
        {
            entity.PublicationJson = null;
            entity.PublicationBodyChecksum = null;
        }
        else
        {
            var encodedPublication = CheckpointPublicationCodec.Encode(checkpoint.Publication);
            entity.PublicationJson = encodedPublication.Json;
            entity.PublicationBodyChecksum = encodedPublication.BodyChecksum;
        }
    }

    private static string SerializeEvidence(IEnumerable<ExecutionEvidence> evidence)
    {
        try
        {
            var dto = evidence.Select(item => new EvidenceDto
            {
                Kind = item.Kind.ToString(),
                Id = item.Id,
                Status = item.Status.ToString(),
                OutputDigest = item.OutputDigest,
            }).ToArray();
            var json = JsonSerializer.Serialize(dto, _jsonOptions);
            EnsureUtf8Bound(json, MaximumEvidenceJsonBytes);
            return json;
        }
        catch (Exception exception) when (IsDataException(exception))
        {
            throw new PersistenceDataException();
        }
    }

    private static ImmutableArray<ExecutionEvidence> DeserializeEvidence(string json)
    {
        try
        {
            EnsureUtf8Bound(json, MaximumEvidenceJsonBytes);
            using (var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32,
            }))
            {
                RejectDuplicateProperties(document.RootElement);
            }

            var dto = JsonSerializer.Deserialize<EvidenceDto?[]>(json, _jsonOptions)
                ?? throw new PersistenceDataException();
            ImmutableArray<ExecutionEvidence> evidence = [.. dto.Select(item => item is null
                ? throw new PersistenceDataException()
                : RequireValid(ExecutionEvidence.Create(
                    ParseDefined<ExecutionEvidenceKind>(item.Kind),
                    item.Id,
                    ParseDefined<ExecutionEvidenceStatus>(item.Status),
                    item.OutputDigest)))];
            if (!StringComparer.Ordinal.Equals(json, SerializeEvidence(evidence)))
            {
                throw new PersistenceDataException();
            }

            return evidence;
        }
        catch (Exception exception) when (IsDataException(exception))
        {
            throw new PersistenceDataException();
        }
    }

    private static void EnsureComplete(ProjectRunEntity entity)
    {
        string?[] required =
        [
            entity.PlanHash,
            entity.PlanJson,
            entity.PlanBodyChecksum,
            entity.BlueprintId,
            entity.BlueprintVersion,
            entity.BlueprintSourceId,
            entity.BlueprintPackageDirectory,
            entity.BlueprintTrust,
            entity.BlueprintChecksum,
            entity.StagingPath,
            entity.StagingPayloadPath,
            entity.OwnershipMarkerPath,
            entity.OwnershipMarkerId,
            entity.TargetParentRoot,
            entity.TargetPath,
            entity.RunArtifactRoot,
            entity.EvidenceJson,
            entity.FinalizationState,
            entity.ReportState,
        ];
        if (required.Any(string.IsNullOrWhiteSpace))
        {
            throw new PersistenceDataException();
        }

        EnsureUtf8Bound(entity.PlanHash!, 71);
        EnsureUtf8Bound(entity.PlanBodyChecksum!, 71);
        if (entity.PlanPreviewJson is not null)
        {
            EnsureUtf8Bound(entity.PlanPreviewJson, CheckpointPreviewCodec.MaximumPreviewJsonBytes);
            EnsureUtf8Bound(entity.PlanPreviewBodyChecksum!, 71);
        }
        if ((entity.PublicationJson is null) != (entity.PublicationBodyChecksum is null))
        {
            throw new PersistenceDataException();
        }
        if (entity.PublicationJson is not null)
        {
            EnsureUtf8Bound(
                entity.PublicationJson,
                CheckpointPublicationCodec.MaximumPublicationJsonBytes);
            EnsureUtf8Bound(entity.PublicationBodyChecksum!, 71);
        }
        EnsureUtf8Bound(entity.BlueprintId!, 128);
        EnsureUtf8Bound(entity.BlueprintVersion!, 64);
        EnsureUtf8Bound(entity.BlueprintSourceId!, 128);
        EnsureUtf8Bound(entity.BlueprintPackageDirectory!, 1_024);
        EnsureUtf8Bound(entity.BlueprintTrust!, 32);
        EnsureUtf8Bound(entity.BlueprintChecksum!, 71);
        EnsureUtf8Bound(entity.StagingPath!, 1_024);
        EnsureUtf8Bound(entity.StagingPayloadPath!, 1_024);
        EnsureUtf8Bound(entity.OwnershipMarkerPath!, 1_024);
        EnsureUtf8Bound(entity.OwnershipMarkerId!, 128);
        EnsureUtf8Bound(entity.TargetParentRoot!, 1_024);
        EnsureUtf8Bound(entity.TargetPath!, 1_024);
        if (entity.CrossVolumeTemporaryPath is not null)
        {
            EnsureUtf8Bound(entity.CrossVolumeTemporaryPath, 1_024);
        }

        EnsureUtf8Bound(entity.RunArtifactRoot!, 1_024);
        EnsureUtf8Bound(entity.FinalizationState!, 32);
        EnsureUtf8Bound(entity.ReportState!, 32);
    }

    private static void EnsureUtf8Bound(string value, int maximumBytes)
    {
        if (Encoding.UTF8.GetByteCount(value) > maximumBytes)
        {
            throw new PersistenceDataException();
        }
    }

    private static T ParseDefined<T>(string? value)
        where T : struct, Enum
    {
        if (!Enum.TryParse(value, ignoreCase: false, out T parsed) || !Enum.IsDefined(parsed))
        {
            throw new PersistenceDataException();
        }

        return parsed;
    }

    private static T RequireValid<T>(ValidationResult<T> result)
    {
        return result.IsValid ? result.Value : throw new PersistenceDataException();
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
                    throw new PersistenceDataException();
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

    private static bool IsDataException(Exception exception)
    {
        return exception is JsonException
            or ArgumentException
            or InvalidOperationException
            or PersistenceDataException;
    }

    private sealed class EvidenceDto
    {
        public string? Kind { get; set; }

        public string? Id { get; set; }

        public string? Status { get; set; }

        public string? OutputDigest { get; set; }
    }
}
