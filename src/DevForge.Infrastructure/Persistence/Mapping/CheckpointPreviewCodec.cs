using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevForge.Application.Contracts;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Execution;
using DevForge.Domain.Privacy;
using DevForge.Domain.Projects;
using DevForge.Domain.Validation;

namespace DevForge.Infrastructure.Persistence.Mapping;

internal static class CheckpointPreviewCodec
{
    public const int MaximumPreviewJsonBytes = 1_048_576;
    private static readonly JsonSerializerOptions _options = new()
    {
        MaxDepth = 128,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static EncodedPreview Encode(PlanPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        var dto = new PreviewDto
        {
            BlueprintId = preview.Blueprint.Id,
            BlueprintVersion = preview.Blueprint.Version,
            PlanHash = preview.PlanHash,
            Steps = [.. preview.Steps.Select(item => new StepDto
            {
                Id = item.Id, Handler = item.HandlerId, TimeoutTicks = item.Timeout.Ticks,
                ProcessPreview = item.ProcessPreview?.Value,
            })],
            Validators = [.. preview.Validators.Select(item => new ValidatorDto
            {
                Id = item.Id, Handler = item.HandlerId, TimeoutTicks = item.Timeout.Ticks,
                Required = item.Required, ProcessPreview = item.ProcessPreview?.Value,
            })],
            Tools = [.. preview.RequiredTools.Select(item =>
                new ToolDto { Id = item.Id, Range = item.VersionRange, Required = item.Required })],
            ToolStatuses = [.. preview.ToolStatuses.Select(item => new ToolStatusDto
            {
                Id = item.Id, Range = item.VersionRange, Required = item.Required,
                Available = item.IsAvailable, Compatible = item.IsCompatible,
                DetectedVersion = item.DetectedVersion,
            })],
            Dependencies = [.. preview.Dependencies.Select(item =>
                new DependencyDto { Id = item.Id, Version = item.Version })],
            Artifacts = [.. preview.Artifacts.Select(item => item.Path)],
            Warnings = [.. preview.Warnings.Select(item =>
                new IssueDto { Code = item.Code, Message = item.Message, Location = item.Location })],
            Inputs = [.. preview.EffectiveInputs.Select(item =>
                new InputDto { Name = item.Key, Value = ToDto(item.Value) })],
            Features = [.. preview.EnabledFeatures],
            Git = new GitDto
            {
                Initialize = preview.Git.InitializeRepository,
                Develop = preview.Git.UseDevelopBranch,
                Publish = preview.Git.PublishToGitHub,
                Private = preview.Git.IsPrivate,
                Account = preview.Git.GitHubAccount,
                Repository = preview.Git.GitHubRepository,
            },
            Completion = new CompletionDto
            {
                Report = preview.Completion.WriteGenerationReport,
                Handoff = preview.Completion.WriteHandoffDocument,
                OpenIde = preview.Completion.OpenIde,
                IdeId = preview.Completion.IdeId,
            },
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(dto, _options);
        if (bytes.Length > MaximumPreviewJsonBytes)
        {
            throw new PersistenceDataException();
        }

        return new EncodedPreview(
            new UTF8Encoding(false, true).GetString(bytes),
            $"sha256:{Convert.ToHexStringLower(SHA256.HashData(bytes))}");
    }

    public static PlanPreview Decode(string json, string expectedChecksum)
    {
        try
        {
            var bytes = new UTF8Encoding(false, true).GetBytes(json);
            if (bytes.Length > MaximumPreviewJsonBytes)
            {
                throw new PersistenceDataException();
            }
            var checksum = $"sha256:{Convert.ToHexStringLower(SHA256.HashData(bytes))}";
            if (!StringComparer.Ordinal.Equals(checksum, expectedChecksum))
            {
                throw new PersistenceDataException();
            }

            var dto = JsonSerializer.Deserialize<PreviewDto>(bytes, _options)
                ?? throw new PersistenceDataException();
            var preview = PlanPreview.Create(
                Require(BlueprintReference.Create(dto.BlueprintId, dto.BlueprintVersion)),
                Require(dto.Steps).Select(item => new PlanPreviewStep(
                    item.Id!, item.Handler!, TimeSpan.FromTicks(item.TimeoutTicks),
                    ReadRedacted(item.ProcessPreview))),
                Require(dto.Validators).Select(item => new PlanPreviewValidator(
                    item.Id!, item.Handler!, TimeSpan.FromTicks(item.TimeoutTicks), item.Required,
                    ReadRedacted(item.ProcessPreview))),
                Require(dto.Tools).Select(item => new ToolRequirement(item.Id!, item.Range!, item.Required)),
                Require(dto.ToolStatuses).Select(item => new PlanPreviewToolStatus(
                    item.Id!, item.Range!, item.Required, item.Available, item.Compatible,
                    item.DetectedVersion)),
                Require(dto.Dependencies).Select(item => new BlueprintDependency(item.Id!, item.Version!)),
                Require(dto.Artifacts).Select(item => new BlueprintArtifact(item!)),
                Require(dto.Warnings).Select(item => new ValidationIssue(
                    item.Code!, item.Message!, item.Location)),
                Require(dto.Inputs).Select(item =>
                    KeyValuePair.Create<string, PlanValue?>(item.Name!, FromDto(item.Value!))),
                Require(dto.Features),
                Require(GitOptions.Create(
                    dto.Git?.Initialize ?? false,
                    "main",
                    dto.Git?.Develop ?? false,
                    dto.Git?.Publish ?? false,
                    dto.Git?.Private ?? true,
                    dto.Git?.Account,
                    dto.Git?.Repository)),
                Require(CompletionOptions.Create(
                    dto.Completion?.Report ?? false,
                    dto.Completion?.Handoff ?? false,
                    dto.Completion?.OpenIde ?? false,
                    dto.Completion?.IdeId)),
                dto.PlanHash);
            var value = Require(preview);
            if (!StringComparer.Ordinal.Equals(json, Encode(value).Json))
            {
                throw new PersistenceDataException();
            }

            return value;
        }
        catch (Exception exception) when (exception is not PersistenceDataException)
        {
            throw new PersistenceDataException();
        }
    }

    private static RedactedText? ReadRedacted(string? value) => value is null
        ? null
        : Require(RedactedText.FromTrustedRedaction(value));

    private static ValueDto ToDto(PlanValue value) => value.Kind switch
    {
        PlanValueKind.Text => new() { Kind = "text", Text = value.StringValue },
        PlanValueKind.Boolean => new() { Kind = "boolean", Boolean = value.BooleanValue },
        PlanValueKind.WholeNumber => new() { Kind = "wholeNumber", Integer = value.IntegerValue },
        PlanValueKind.Sequence => new()
        {
            Kind = "sequence",
            Items = [.. value.ArrayValue.Select(ToDto)],
        },
        PlanValueKind.Map => new()
        {
            Kind = "map",
            Entries = [.. value.ObjectValue.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item =>
                new InputDto { Name = item.Key, Value = ToDto(item.Value) })],
        },
        _ => throw new PersistenceDataException(),
    };

    private static PlanValue FromDto(ValueDto dto) => dto.Kind switch
    {
        "text" => Require(PlanValue.FromString(dto.Text)),
        "boolean" => PlanValue.FromBoolean(dto.Boolean),
        "wholeNumber" => PlanValue.FromInteger(dto.Integer),
        "sequence" => Require(PlanValue.FromArray(Require(dto.Items).Select(FromDto))),
        "map" => Require(PlanValue.FromObject(Require(dto.Entries).Select(item =>
            KeyValuePair.Create<string, PlanValue?>(item.Name!, FromDto(item.Value!))))),
        _ => throw new PersistenceDataException(),
    };

    private static T Require<T>(ValidationResult<T> result) where T : class =>
        result.IsValid ? result.Value : throw new PersistenceDataException();

    private static T[] Require<T>(T[]? values) => values ?? throw new PersistenceDataException();

    private sealed class PreviewDto
    {
        public string? BlueprintId { get; set; }
        public string? BlueprintVersion { get; set; }
        public string? PlanHash { get; set; }
        public StepDto[]? Steps { get; set; }
        public ValidatorDto[]? Validators { get; set; }
        public ToolDto[]? Tools { get; set; }
        public ToolStatusDto[]? ToolStatuses { get; set; }
        public DependencyDto[]? Dependencies { get; set; }
        public string?[]? Artifacts { get; set; }
        public IssueDto[]? Warnings { get; set; }
        public InputDto[]? Inputs { get; set; }
        public string?[]? Features { get; set; }
        public GitDto? Git { get; set; }
        public CompletionDto? Completion { get; set; }
    }

    private class StepDto { public string? Id { get; set; } public string? Handler { get; set; } public long TimeoutTicks { get; set; } public string? ProcessPreview { get; set; } }
    private sealed class ValidatorDto : StepDto { public bool Required { get; set; } }
    private class ToolDto { public string? Id { get; set; } public string? Range { get; set; } public bool Required { get; set; } }
    private sealed class ToolStatusDto : ToolDto { public bool Available { get; set; } public bool Compatible { get; set; } public string? DetectedVersion { get; set; } }
    private sealed class DependencyDto { public string? Id { get; set; } public string? Version { get; set; } }
    private sealed class IssueDto { public string? Code { get; set; } public string? Message { get; set; } public string? Location { get; set; } }
    private sealed class InputDto { public string? Name { get; set; } public ValueDto? Value { get; set; } }
    private sealed class ValueDto { public string? Kind { get; set; } public string? Text { get; set; } public bool Boolean { get; set; } public long Integer { get; set; } public ValueDto[]? Items { get; set; } public InputDto[]? Entries { get; set; } }
    private sealed class GitDto
    {
        public bool Initialize { get; set; }
        public bool Develop { get; set; }
        public bool Publish { get; set; }
        public bool Private { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Account { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Repository { get; set; }
    }
    private sealed class CompletionDto { public bool Report { get; set; } public bool Handoff { get; set; } public bool OpenIde { get; set; } public string? IdeId { get; set; } }

    internal sealed record EncodedPreview(string Json, string BodyChecksum);
}
