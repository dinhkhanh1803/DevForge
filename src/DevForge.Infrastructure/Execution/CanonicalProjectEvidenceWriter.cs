using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DevForge.Application.Contracts;
using DevForge.Domain.Diagnostics;
using DevForge.Domain.Execution;
using DevForge.Domain.Privacy;
using DevForge.Domain.Reports;
using DevForge.Domain.Runs;

namespace DevForge.Infrastructure.Execution;

public sealed class CanonicalProjectEvidenceWriter : IProjectEvidenceWriter
{
    public const int MaximumEvidenceFileBytes = 1024 * 1024;
    private static readonly UTF8Encoding _utf8 = new(false, true);
    private static readonly WorkspaceRelativePath _recipePath =
        WorkspaceRelativePath.Create(@".devforge\project.recipe.yaml").Value;
    private static readonly WorkspaceRelativePath _lockPath =
        WorkspaceRelativePath.Create("devforge.lock.json").Value;
    private static readonly WorkspaceRelativePath _reportPath =
        WorkspaceRelativePath.Create("generation-report.json").Value;
    private static readonly WorkspaceRelativePath _policyPath =
        WorkspaceRelativePath.Create("policy.snapshot.json").Value;
    private static readonly WorkspaceRelativePath[] _paths =
        [_recipePath, _lockPath, _reportPath, _policyPath];

    public async Task<ExecutionOperationResult<ProjectEvidenceWriteReceipt>> WriteAsync(
        RunCheckpoint checkpoint,
        GenerationReport report,
        IWorkspaceFileSystem payloadWorkspace,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(payloadWorkspace);
        cancellationToken.ThrowIfCancellationRequested();
        if (!StringComparer.Ordinal.Equals(checkpoint.Run.Id, report.RunId)
            || payloadWorkspace is not IAtomicFileWorkspaceFileSystem atomicWorkspace)
        {
            return Failure();
        }

        try
        {
            var recipe = WriteRecipe(checkpoint);
            var targetReport = WriteReport(checkpoint, report);
            var policy = WritePolicy(checkpoint);
            var buildOutputs = await BuildOutputManifest.CreateAsync(checkpoint, payloadWorkspace, cancellationToken)
                .ConfigureAwait(false);
            var integrityDigests = new List<(WorkspaceRelativePath Path, string Digest)>
            {
                (Path: _recipePath, Digest: Digest(recipe)),
                (Path: _reportPath, Digest: Digest(targetReport)),
                (Path: _policyPath, Digest: Digest(policy)),
            };
            if (buildOutputs is not null)
            {
                integrityDigests.Add((BuildOutputManifest.Path, Digest(buildOutputs)));
            }
            var projectLock = WriteLock(checkpoint, report, integrityDigests);
            var files = new List<(WorkspaceRelativePath, byte[])>
            {
                (_recipePath, recipe),
                (_lockPath, projectLock),
                (_reportPath, targetReport),
                (_policyPath, policy),
            };
            if (buildOutputs is not null)
            {
                files.Add((BuildOutputManifest.Path, buildOutputs));
            }
            if (files.Any(file => file.Item2.Length > MaximumEvidenceFileBytes
                    || RedactedText.IsSecretShapedValue(_utf8.GetString(file.Item2))))
            {
                return Failure();
            }

            var missing = new List<(WorkspaceRelativePath Path, byte[] Bytes)>();
            foreach (var file in files)
            {
                if (await payloadWorkspace.DirectoryExistsAsync(file.Item1, cancellationToken)
                        .ConfigureAwait(false))
                {
                    return Failure();
                }

                if (!await payloadWorkspace.FileExistsAsync(file.Item1, cancellationToken)
                    .ConfigureAwait(false))
                {
                    missing.Add(file);
                    continue;
                }

                var existing = await ReadBoundedAsync(
                    payloadWorkspace,
                    file.Item1,
                    cancellationToken).ConfigureAwait(false);
                if (!existing.AsSpan().SequenceEqual(file.Item2))
                {
                    return Failure();
                }
            }

            await payloadWorkspace.CreateDirectoryAsync(
                WorkspaceRelativePath.Create(".devforge").Value,
                cancellationToken).ConfigureAwait(false);
            foreach (var file in missing)
            {
                await atomicWorkspace.WriteFileAtomicallyAsync(
                    file.Path,
                    file.Bytes,
                    overwrite: false,
                    cancellationToken).ConfigureAwait(false);
            }

            return ExecutionOperationResult.Success(
                ProjectEvidenceWriteReceipt.Create(
                    _paths,
                    files.Take(_paths.Length).Select(file => Digest(file.Item2))).Value);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            return Failure();
        }
    }

    private static byte[] WriteRecipe(RunCheckpoint checkpoint)
    {
        var preview = checkpoint.Preview
            ?? throw new InvalidDataException("Canonical project evidence requires the persisted plan preview.");
        var text = new StringBuilder();
        text.AppendLine("schema: devforge-project-recipe-v1");
        if (checkpoint.Plan.TemplateContext.TryGetValue("project.name", out var projectName))
        {
            text.Append("projectName: ").AppendLine(Quote(projectName));
            text.AppendLine("projectNameStatus: recorded");
        }
        else
        {
            text.AppendLine("projectName: null");
            text.AppendLine("projectNameStatus: not-recorded");
        }
        text.AppendLine("blueprint:");
        text.Append("  id: ").AppendLine(Quote(checkpoint.Blueprint.Id));
        text.Append("  version: ").AppendLine(Quote(checkpoint.Blueprint.Version));
        text.AppendLine("inputs:");
        foreach (var input in preview.EffectiveInputs)
        {
            text.Append("  ").Append(Quote(input.Key)).Append(": ")
                .AppendLine(ToYaml(input.Value));
        }

        text.AppendLine("features:");
        foreach (var feature in preview.EnabledFeatures.Order(StringComparer.Ordinal))
        {
            text.Append("  - ").AppendLine(Quote(feature));
        }

        text.AppendLine("git:");
        text.Append("  initializeRepository: ").AppendLine(
            preview.Git.InitializeRepository ? "true" : "false");
        text.Append("  primaryBranch: ").AppendLine(Quote(preview.Git.PrimaryBranch));
        text.Append("  useDevelopBranch: ").AppendLine(
            preview.Git.UseDevelopBranch ? "true" : "false");
        text.Append("  publishToGitHub: ").AppendLine(
            preview.Git.PublishToGitHub ? "true" : "false");
        text.Append("  isPrivate: ").AppendLine(preview.Git.IsPrivate ? "true" : "false");
        text.Append("  githubAccount: ").AppendLine(
            preview.Git.GitHubAccount is null ? "null" : Quote(preview.Git.GitHubAccount));
        text.Append("  githubRepository: ").AppendLine(
            preview.Git.GitHubRepository is null ? "null" : Quote(preview.Git.GitHubRepository));
        WriteTeam(text, checkpoint);
        return _utf8.GetBytes(text.ToString());
    }

    private static void WriteTeam(StringBuilder text, RunCheckpoint checkpoint)
    {
        if (!checkpoint.Plan.TemplateContext.TryGetValue("team.snapshot_status", out var status))
        {
            text.AppendLine("teamSnapshotStatus: not-recorded");
            text.AppendLine("team: null");
            return;
        }

        if (StringComparer.Ordinal.Equals(status, "none"))
        {
            text.AppendLine("teamSnapshotStatus: none");
            text.AppendLine("team: null");
            return;
        }

        if (!StringComparer.Ordinal.Equals(status, "recorded")
            || !checkpoint.Plan.TemplateContext.TryGetValue("team.profile_id", out var id)
            || !checkpoint.Plan.TemplateContext.TryGetValue("team.profile_name", out var name)
            || !checkpoint.Plan.TemplateContext.TryGetValue("team.standards_json", out var standardsJson))
        {
            throw new InvalidDataException("Canonical team snapshot status is inconsistent.");
        }

        using var standards = JsonDocument.Parse(standardsJson);
        if (standards.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Canonical team standards must be a JSON object.");
        }

        text.AppendLine("teamSnapshotStatus: recorded");
        text.AppendLine("team:");
        text.Append("  id: ").AppendLine(Quote(id));
        text.Append("  name: ").AppendLine(Quote(name));
        text.AppendLine("  standards:");
        foreach (var standard in standards.RootElement.EnumerateObject())
        {
            if (standard.Value.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException("Canonical team standards must contain strings.");
            }

            text.Append("    ").Append(Quote(standard.Name)).Append(": ")
                .AppendLine(Quote(standard.Value.GetString()!));
        }
    }

    private static byte[] WriteLock(
        RunCheckpoint checkpoint,
        GenerationReport report,
        IEnumerable<(WorkspaceRelativePath Path, string Digest)> integrityDigests) =>
        WriteJson(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("schema", "devforge-lock-v1");
            writer.WriteString("planHash", checkpoint.PlanHash);
            WriteEngineProvenance(writer, checkpoint);
            WriteBlueprint(writer, checkpoint);
            writer.WriteStartArray("dependencies");
            var preview = RequirePreview(checkpoint);
            foreach (var dependency in preview.Dependencies.OrderBy(item => item.Id, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("id", dependency.Id);
                writer.WriteString("version", dependency.Version);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("tools");
            foreach (var tool in preview.ToolStatuses.OrderBy(item => item.Id, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("id", tool.Id);
                writer.WriteString("versionRange", tool.VersionRange);
                writer.WriteBoolean("required", tool.Required);
                if (tool.DetectedVersion is not null)
                {
                    writer.WriteString("detectedVersion", tool.DetectedVersion);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("artifacts");
            foreach (var artifact in report.GeneratedArtifacts.Order(StringComparer.Ordinal))
            {
                writer.WriteStringValue(artifact.Replace('\\', '/'));
            }

            writer.WriteEndArray();
            writer.WriteStartArray("evidenceDigests");
            foreach (var evidence in integrityDigests)
            {
                writer.WriteStartObject();
                writer.WriteString("path", evidence.Path.Value.Replace('\\', '/'));
                writer.WriteString("sha256", evidence.Digest);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        });

    private static byte[] WriteReport(RunCheckpoint checkpoint, GenerationReport report) =>
        WriteJson(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("schema", "devforge-project-generation-report-v1");
            writer.WriteString("capturePhase", "validated-pre-finalization");
            WriteEngineProvenance(writer, checkpoint);
            writer.WriteString("planHash", checkpoint.PlanHash);
            WriteBlueprint(writer, checkpoint);
            writer.WriteStartArray("toolStatuses");
            foreach (var tool in report.ToolStatuses.OrderBy(item => item.Id, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("id", tool.Id);
                writer.WriteBoolean("required", tool.Required);
                writer.WriteBoolean("available", tool.IsAvailable);
                writer.WriteBoolean("compatible", tool.IsCompatible);
                if (tool.DetectedVersion is not null)
                {
                    writer.WriteString("detectedVersion", tool.DetectedVersion);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("stepResults");
            foreach (var attempt in checkpoint.Run.Attempts
                .OrderBy(item => item.StepId, StringComparer.Ordinal)
                .ThenBy(item => item.AttemptNumber))
            {
                writer.WriteStartObject();
                writer.WriteString("stepId", attempt.StepId);
                writer.WriteNumber("attemptNumber", attempt.AttemptNumber);
                writer.WriteString("outcome", attempt.Outcome.ToString());
                var stepEvidence = checkpoint.Evidence.FirstOrDefault(item =>
                    item.Kind == ExecutionEvidenceKind.Step
                    && StringComparer.Ordinal.Equals(item.Id, attempt.StepId));
                writer.WriteString(
                    "checkpointStatus",
                    stepEvidence?.Status.ToString() ?? ToEvidenceStatus(attempt.Outcome));
                writer.WriteNumber(
                    "durationMilliseconds",
                    DurationMilliseconds(attempt.StartedAt, attempt.CompletedAt));
                if (attempt.ExitCode is not null)
                {
                    writer.WriteNumber("exitCode", attempt.ExitCode.Value);
                }

                if (attempt.OutputDigest is not null)
                {
                    writer.WriteString("outputDigest", attempt.OutputDigest);
                }
                else if (stepEvidence is not null)
                {
                    writer.WriteString("outputDigest", stepEvidence.OutputDigest);
                }

                WriteError(writer, attempt.Error?.Code, attempt.Error?.Summary);

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("validations");
            foreach (var validation in report.Validations.OrderBy(item => item.Id, StringComparer.Ordinal))
            {
                var evidenceId = validation.Id == "whole-payload-secret-scan"
                    ? "secret-scan"
                    : validation.Id;
                var validationEvidence = checkpoint.Evidence.FirstOrDefault(item =>
                    item.Kind is ExecutionEvidenceKind.Validator or ExecutionEvidenceKind.SecretScan
                    && StringComparer.Ordinal.Equals(item.Id, evidenceId));
                var validator = checkpoint.Plan.Validators.FirstOrDefault(item =>
                    StringComparer.Ordinal.Equals(item.Id, validation.Id));
                var required = validator?.Required ?? true;
                writer.WriteStartObject();
                writer.WriteString("id", validation.Id);
                writer.WriteString("status", validation.Status.ToString());
                writer.WriteString(
                    "checkpointStatus",
                    validationEvidence?.Status.ToString() ?? validation.Status.ToString());
                writer.WriteNumber(
                    "durationMilliseconds",
                    validationEvidence is null
                        ? 0
                        : DurationMilliseconds(
                            validationEvidence.StartedAt,
                            validationEvidence.CompletedAt));
                if (validationEvidence is not null)
                {
                    writer.WriteString("outputDigest", validationEvidence.OutputDigest);
                }

                writer.WriteString("severity", required ? "blocking" : "advisory");
                writer.WriteBoolean("required", required);
                writer.WriteString("summary", validation.Summary);
                WriteError(
                    writer,
                    validationEvidence?.ErrorCode,
                    validationEvidence?.ErrorSummary?.Value);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("warnings");
            foreach (var warning in report.Warnings
                .OrderBy(item => item.Code, StringComparer.Ordinal)
                .ThenBy(item => item.Message.Value, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("code", warning.Code);
                writer.WriteString("message", warning.Message.Value);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("errors");
            foreach (var error in report.Errors
                .OrderBy(item => item.Code, StringComparer.Ordinal)
                .ThenBy(item => item.Summary, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("code", error.Code);
                writer.WriteString("summary", error.Summary);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("artifacts");
            foreach (var artifact in report.GeneratedArtifacts.Order(StringComparer.Ordinal))
            {
                writer.WriteStringValue(artifact.Replace('\\', '/'));
            }

            writer.WriteEndArray();
            writer.WriteStartObject("artifactSummary");
            writer.WriteNumber("count", report.GeneratedArtifacts.Length);
            writer.WriteStartArray("paths");
            foreach (var artifact in report.GeneratedArtifacts.Order(StringComparer.Ordinal))
            {
                writer.WriteStringValue(artifact.Replace('\\', '/'));
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndObject();
        });

    private static byte[] WritePolicy(RunCheckpoint checkpoint) =>
        WriteJson(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("schema", "devforge-policy-snapshot-v1");
            writer.WriteString("planHash", checkpoint.PlanHash);
            writer.WriteStartArray("features");
            var preview = RequirePreview(checkpoint);
            foreach (var feature in preview.EnabledFeatures.Order(StringComparer.Ordinal))
            {
                writer.WriteStringValue(feature);
            }

            writer.WriteEndArray();
            writer.WriteStartArray("tools");
            foreach (var tool in preview.RequiredTools.OrderBy(item => item.Id, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("id", tool.Id);
                writer.WriteString("versionRange", tool.VersionRange);
                writer.WriteBoolean("required", tool.Required);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("dependencies");
            foreach (var dependency in preview.Dependencies.OrderBy(item => item.Id, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("id", dependency.Id);
                writer.WriteString("version", dependency.Version);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            WritePlanItems(writer, "steps", checkpoint.Plan.Steps.Select(step =>
                (step.Id, step.Handler, step.Inputs, Required: true)));
            WritePlanItems(writer, "validators", checkpoint.Plan.Validators.Select(validator =>
                (validator.Id, validator.Handler, validator.Inputs, validator.Required)));
            writer.WriteEndObject();
        });

    private static void WritePlanItems(
        Utf8JsonWriter writer,
        string name,
        IEnumerable<(string Id, string Handler, ImmutableDictionary<string, PlanValue> Inputs, bool Required)> items)
    {
        writer.WriteStartArray(name);
        foreach (var item in items)
        {
            writer.WriteStartObject();
            writer.WriteString("id", item.Id);
            writer.WriteString("handler", item.Handler);
            writer.WriteBoolean("required", item.Required);
            writer.WriteStartObject("inputs");
            foreach (var input in item.Inputs.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(input.Key);
                WritePlanValue(writer, input.Value);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WritePlanValue(Utf8JsonWriter writer, PlanValue value)
    {
        switch (value.Kind)
        {
            case PlanValueKind.Text:
                writer.WriteStringValue(value.StringValue);
                break;
            case PlanValueKind.Boolean:
                writer.WriteBooleanValue(value.BooleanValue);
                break;
            case PlanValueKind.WholeNumber:
                writer.WriteNumberValue(value.IntegerValue);
                break;
            case PlanValueKind.Sequence:
                writer.WriteStartArray();
                foreach (var item in value.ArrayValue)
                {
                    WritePlanValue(writer, item);
                }

                writer.WriteEndArray();
                break;
            case PlanValueKind.Map:
                writer.WriteStartObject();
                foreach (var item in value.ObjectValue.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(item.Key);
                    WritePlanValue(writer, item.Value);
                }

                writer.WriteEndObject();
                break;
            default:
                throw new InvalidDataException("The plan contains an unsupported evidence value.");
        }
    }

    private static void WriteBlueprint(Utf8JsonWriter writer, RunCheckpoint checkpoint)
    {
        writer.WriteStartObject("blueprint");
        writer.WriteString("id", checkpoint.Blueprint.Id);
        writer.WriteString("version", checkpoint.Blueprint.Version);
        writer.WriteString("checksum", checkpoint.BlueprintFingerprint.AggregateChecksum);
        writer.WriteEndObject();
    }

    private static void WriteEngineProvenance(Utf8JsonWriter writer, RunCheckpoint checkpoint)
    {
        if (checkpoint.Plan.TemplateContext.TryGetValue("engine.version", out var engineVersion))
        {
            writer.WriteString("engineVersion", engineVersion);
            writer.WriteString("engineVersionStatus", "recorded");
        }
        else
        {
            writer.WriteNull("engineVersion");
            writer.WriteString("engineVersionStatus", "not-recorded");
        }
    }

    private static long DurationMilliseconds(DateTimeOffset? startedAt, DateTimeOffset? completedAt) =>
        startedAt is null || completedAt is null
            ? 0
            : checked((long)(completedAt.Value - startedAt.Value).TotalMilliseconds);

    private static string ToEvidenceStatus(StepAttemptOutcome outcome) => outcome switch
    {
        StepAttemptOutcome.Succeeded => ExecutionEvidenceStatus.Passed.ToString(),
        StepAttemptOutcome.Failed or StepAttemptOutcome.Cancelled => ExecutionEvidenceStatus.Failed.ToString(),
        _ => "Running",
    };

    private static void WriteError(Utf8JsonWriter writer, string? code, string? summary)
    {
        if (code is null || summary is null)
        {
            writer.WriteNull("error");
            return;
        }

        writer.WriteStartObject("error");
        writer.WriteString("code", code);
        writer.WriteString("summary", summary);
        writer.WriteEndObject();
    }

    private static PlanPreview RequirePreview(RunCheckpoint checkpoint) =>
        checkpoint.Preview
        ?? throw new InvalidDataException("Canonical project evidence requires the persisted plan preview.");

    private static string RequireTemplateContext(RunCheckpoint checkpoint, string key) =>
        checkpoint.Plan.TemplateContext.TryGetValue(key, out var value)
            ? value
            : throw new InvalidDataException("Canonical project evidence requires deterministic engine context.");

    private static async Task<byte[]> ReadBoundedAsync(
        IWorkspaceFileSystem workspace,
        WorkspaceRelativePath path,
        CancellationToken cancellationToken)
    {
        await using var input = await workspace.OpenReadAsync(path, cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        var total = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return output.ToArray();
            }

            total += read;
            if (total > MaximumEvidenceFileBytes)
            {
                throw new InvalidDataException("Existing project evidence exceeds the supported bound.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private static byte[] WriteJson(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true });
        write(writer);
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private static string Quote(string value) => JsonSerializer.Serialize(value);

    private static string Digest(byte[] bytes) =>
        $"sha256:{Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}";

    private static string ToYaml(PlanValue value) => value.Kind switch
    {
        PlanValueKind.Text => Quote(value.StringValue!),
        PlanValueKind.Boolean => value.BooleanValue ? "true" : "false",
        PlanValueKind.WholeNumber => value.IntegerValue.ToString(CultureInfo.InvariantCulture),
        _ => Quote(JsonSerializer.Serialize(ToJsonCompatible(value))),
    };

    private static object ToJsonCompatible(PlanValue value) => value.Kind switch
    {
        PlanValueKind.Text => value.StringValue!,
        PlanValueKind.Boolean => value.BooleanValue,
        PlanValueKind.WholeNumber => value.IntegerValue,
        PlanValueKind.Sequence => value.ArrayValue.Select(ToJsonCompatible).ToArray(),
        PlanValueKind.Map => value.ObjectValue.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => ToJsonCompatible(pair.Value), StringComparer.Ordinal),
        _ => throw new InvalidDataException("The plan contains an unsupported evidence value."),
    };

    private static bool IsExpectedFailure(Exception exception) =>
        exception is IOException
            or InfrastructureOperationException
            or UnauthorizedAccessException
            or System.Security.SecurityException
            or NotSupportedException
            or ArgumentException
            or InvalidDataException;

    private static ExecutionOperationResult<ProjectEvidenceWriteReceipt> Failure()
    {
        var error = DevForgeError.Create(
            "DF-FINAL-001",
            "Canonical project evidence could not be persisted.",
            RedactedText.FromTrustedRedaction(
                "The guarded staging payload rejected missing, existing, oversized, or non-canonical engine evidence.").Value,
            "project-evidence",
            null,
            true,
            ["Retry from an owned staging workspace without blueprint-authored evidence files."],
            []).Value;
        return ExecutionOperationResult.Failure<ProjectEvidenceWriteReceipt>(error);
    }
}
