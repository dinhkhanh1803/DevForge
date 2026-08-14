using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using DevForge.Application.Contracts;
using DevForge.Domain.Diagnostics;
using DevForge.Domain.Privacy;
using DevForge.Domain.Reports;

namespace DevForge.Infrastructure.Execution;

public sealed class CanonicalGenerationReportWriter : IGenerationReportWriter
{
    public const int MaximumReportBytes = 1024 * 1024;
    private static readonly UTF8Encoding _utf8 = new(false, true);

    public async Task<ExecutionOperationResult<ReportWriteReceipt>> WriteAsync(
        RunCheckpoint checkpoint,
        GenerationReport report,
        IWorkspaceFileSystem runArtifactWorkspace,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(runArtifactWorkspace);
        cancellationToken.ThrowIfCancellationRequested();
        if (!StringComparer.Ordinal.Equals(checkpoint.Run.Id, report.RunId)
            || !checkpoint.RunArtifacts.Root.Equals(runArtifactWorkspace.Root)
            || runArtifactWorkspace is not IAtomicFileWorkspaceFileSystem atomicWorkspace)
        {
            return Failure();
        }

        try
        {
            var json = WriteJson(checkpoint, report);
            var markdown = _utf8.GetBytes(WriteMarkdown(checkpoint, report));
            if (json.Length > MaximumReportBytes || markdown.Length > MaximumReportBytes)
            {
                return Failure();
            }

            var directory = WorkspaceRelativePath.Create("reports").Value;
            var jsonPath = WorkspaceRelativePath.Create($"reports\\{report.RunId}.json").Value;
            var markdownPath = WorkspaceRelativePath.Create($"reports\\{report.RunId}.md").Value;
            await runArtifactWorkspace.CreateDirectoryAsync(
                directory,
                cancellationToken).ConfigureAwait(false);
            await atomicWorkspace.WriteFileAtomicallyAsync(
                jsonPath,
                json,
                overwrite: true,
                cancellationToken).ConfigureAwait(false);
            await atomicWorkspace.WriteFileAtomicallyAsync(
                markdownPath,
                markdown,
                overwrite: true,
                cancellationToken).ConfigureAwait(false);
            return ExecutionOperationResult.Success(
                ReportWriteReceipt.Create(jsonPath, markdownPath).Value);
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

    private static byte[] WriteJson(RunCheckpoint checkpoint, GenerationReport report)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteString("schema", "devforge-generation-report-v1");
        writer.WriteString("runId", report.RunId);
        writer.WriteString("planHash", checkpoint.PlanHash);
        writer.WriteString("blueprintId", checkpoint.Blueprint.Id);
        writer.WriteString("blueprintVersion", checkpoint.Blueprint.Version);
        writer.WriteString("generatedAt", report.GeneratedAt.ToUniversalTime());
        writer.WriteStartArray("attempts");
        foreach (var attempt in checkpoint.Run.Attempts)
        {
            writer.WriteStartObject();
            writer.WriteString("stepId", attempt.StepId);
            writer.WriteNumber("attempt", attempt.AttemptNumber);
            writer.WriteString("outcome", attempt.Outcome.ToString());
            writer.WriteString("startedAt", attempt.StartedAt.ToUniversalTime());
            if (attempt.CompletedAt is { } completedAt)
            {
                writer.WriteString("completedAt", completedAt.ToUniversalTime());
            }

            if (attempt.ExitCode is { } exitCode)
            {
                writer.WriteNumber("exitCode", exitCode);
            }

            if (attempt.OutputDigest is not null)
            {
                writer.WriteString("outputDigest", attempt.OutputDigest);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteStartArray("validations");
        foreach (var validation in report.Validations)
        {
            writer.WriteStartObject();
            writer.WriteString("id", validation.Id);
            writer.WriteString("status", validation.Status.ToString());
            writer.WriteString("summary", validation.Summary);
            if (validation.Detail is not null)
            {
                writer.WriteString("detail", validation.Detail.Value);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteStartArray("toolStatuses");
        foreach (var tool in report.ToolStatuses)
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
        writer.WriteStartArray("warnings");
        foreach (var warning in report.Warnings)
        {
            writer.WriteStartObject();
            writer.WriteString("code", warning.Code);
            writer.WriteString("message", warning.Message.Value);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteStartArray("artifacts");
        foreach (var artifact in report.GeneratedArtifacts)
        {
            writer.WriteStringValue(artifact);
        }

        writer.WriteEndArray();
        writer.WriteStartArray("errors");
        foreach (var error in report.Errors)
        {
            writer.WriteStartObject();
            writer.WriteString("code", error.Code);
            writer.WriteString("message", error.Summary);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private static string WriteMarkdown(RunCheckpoint checkpoint, GenerationReport report)
    {
        var text = new StringBuilder();
        text.AppendLine("# DevForge Generation Report");
        text.AppendLine();
        text.Append("- Run: `").Append(report.RunId).AppendLine("`");
        text.Append("- Plan: `").Append(checkpoint.PlanHash).AppendLine("`");
        text.Append("- Blueprint: `").Append(checkpoint.Blueprint.Id).Append('@')
            .Append(checkpoint.Blueprint.Version).AppendLine("`");
        text.Append("- Generated: ").AppendLine(
            report.GeneratedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        text.AppendLine();
        text.AppendLine("## Validation");
        text.AppendLine();
        foreach (var validation in report.Validations)
        {
            text.Append("- `").Append(validation.Id).Append("`: ")
                .Append(validation.Status).Append(" - ")
                .AppendLine(validation.Summary.ReplaceLineEndings(" "));
        }

        text.AppendLine();
        text.AppendLine("## Tool status");
        text.AppendLine();
        foreach (var tool in report.ToolStatuses)
        {
            text.Append("- `").Append(tool.Id).Append("`: ")
                .Append(tool.IsAvailable && tool.IsCompatible ? "ready" : "unavailable")
                .AppendLine();
        }

        if (!report.Warnings.IsEmpty)
        {
            text.AppendLine();
            text.AppendLine("## Warnings");
            text.AppendLine();
            foreach (var warning in report.Warnings)
            {
                text.Append("- ").Append(warning.Code).Append(": ")
                    .AppendLine(warning.Message.Value.ReplaceLineEndings(" "));
            }
        }

        text.AppendLine();
        text.AppendLine("## Generated artifacts");
        text.AppendLine();
        foreach (var artifact in report.GeneratedArtifacts)
        {
            text.Append("- `").Append(artifact).AppendLine("`");
        }

        if (!report.Errors.IsEmpty)
        {
            text.AppendLine();
            text.AppendLine("## Errors");
            text.AppendLine();
            foreach (var error in report.Errors)
            {
                text.Append("- ").Append(error.Code).Append(": ")
                    .AppendLine(error.Summary.ReplaceLineEndings(" "));
            }
        }

        return text.ToString();
    }

    private static bool IsExpectedFailure(Exception exception) =>
        exception is IOException
            or InfrastructureOperationException
            or UnauthorizedAccessException
            or System.Security.SecurityException
            or NotSupportedException
            or ArgumentException;

    private static ExecutionOperationResult<ReportWriteReceipt> Failure()
    {
        var detail = RedactedText.FromTrustedRedaction(
            "The bounded generation report could not be persisted in the guarded run-artifact workspace.").Value;
        var error = DevForgeError.Create(
            "DF-FINAL-001",
            "The generation report could not be saved.",
            detail,
            "report",
            null,
            true,
            ["Retry report persistence after verifying the run-artifact workspace."],
            []).Value;
        return ExecutionOperationResult.Failure<ReportWriteReceipt>(error);
    }
}
