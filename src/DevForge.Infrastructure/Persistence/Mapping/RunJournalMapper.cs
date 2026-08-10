using System.Text;
using System.Text.Json;
using DevForge.Domain.Diagnostics;
using DevForge.Domain.Privacy;
using DevForge.Domain.Runs;
using DevForge.Domain.Validation;
using DevForge.Infrastructure.Persistence.Entities;

namespace DevForge.Infrastructure.Persistence.Mapping;

internal static class RunJournalMapper
{
    private const int MaximumErrorsJsonBytes = 65_536;
    private const int MaximumErrorActionsJsonBytes = 16_384;
    private const int MaximumErrorContextJsonBytes = 16_384;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        MaxDepth = 32,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static ProjectRunEntity CreateEntity(ProjectRun run, DateTimeOffset now)
    {
        ValidateRunIdentity(run);
        return new ProjectRunEntity
        {
            Id = run.Id,
            RecipeId = run.RecipeId,
            Status = run.Status.ToString(),
            CurrentStepId = run.CurrentStepId,
            CreatedAtUnixMs = now.ToUnixTimeMilliseconds(),
            UpdatedAtUnixMs = now.ToUnixTimeMilliseconds(),
            CompletedAtUnixMs = IsTerminal(run.Status) ? now.ToUnixTimeMilliseconds() : null,
            ErrorsJson = SerializeErrors(run.Errors),
        };
    }

    public static void UpdateEntity(ProjectRunEntity entity, ProjectRun run, DateTimeOffset now)
    {
        ValidateRunIdentity(run);
        entity.RecipeId = run.RecipeId;
        entity.Status = run.Status.ToString();
        entity.CurrentStepId = run.CurrentStepId;
        entity.UpdatedAtUnixMs = now.ToUnixTimeMilliseconds();
        entity.CompletedAtUnixMs = IsTerminal(run.Status)
            ? entity.CompletedAtUnixMs ?? now.ToUnixTimeMilliseconds()
            : null;
        entity.ErrorsJson = SerializeErrors(run.Errors);
    }

    public static IReadOnlyList<RunStepEntity> CreateStepEntities(ProjectRun run)
    {
        return run.Attempts.Select(attempt => CreateStepEntity(run.Id, attempt)).ToArray();
    }

    public static ProjectRun ToModel(ProjectRunEntity entity)
    {
        EnsureBounded(entity.Id, 128);
        EnsureBounded(entity.RecipeId, 128);
        if (!TryParseDefined(entity.Status, out RunStatus status))
        {
            throw new PersistenceDataException();
        }

        if (entity.CurrentStepId is not null)
        {
            EnsureBounded(entity.CurrentStepId, 128);
        }

        _ = ToTimestamp(entity.CreatedAtUnixMs);
        var createdAt = ToTimestamp(entity.CreatedAtUnixMs);
        var updatedAt = ToTimestamp(entity.UpdatedAtUnixMs);
        if (updatedAt < createdAt)
        {
            throw new PersistenceDataException();
        }

        if (entity.CompletedAtUnixMs is not null)
        {
            var completedAt = ToTimestamp(entity.CompletedAtUnixMs.Value);
            if (completedAt < createdAt || !IsTerminal(status))
            {
                throw new PersistenceDataException();
            }
        }
        else if (IsTerminal(status))
        {
            throw new PersistenceDataException();
        }

        if (entity.StagingPath is not null || entity.TargetPath is not null)
        {
            throw new PersistenceDataException();
        }

        var attempts = entity.Steps
            .OrderBy(step => step.StepId, StringComparer.Ordinal)
            .ThenBy(step => step.AttemptNumber)
            .Select(ToModel)
            .ToArray();
        var errors = DeserializeErrors(entity.ErrorsJson);
        return RequireValid(ProjectRun.Rehydrate(
            entity.Id,
            entity.RecipeId,
            status,
            entity.CurrentStepId,
            attempts,
            errors));
    }

    private static RunStepEntity CreateStepEntity(string runId, StepAttempt attempt)
    {
        EnsureBounded(attempt.StepId, 128);
        var error = attempt.Error is null ? null : ToDto(attempt.Error);
        if (error is not null
            && !string.Equals(error.StepId, attempt.StepId, StringComparison.Ordinal))
        {
            throw new PersistenceDataException();
        }

        return new RunStepEntity
        {
            RunId = runId,
            StepId = attempt.StepId,
            AttemptNumber = attempt.AttemptNumber,
            Outcome = attempt.Outcome.ToString(),
            StartedAtUnixMs = attempt.StartedAt.ToUnixTimeMilliseconds(),
            CompletedAtUnixMs = attempt.CompletedAt?.ToUnixTimeMilliseconds(),
            ExitCode = attempt.ExitCode,
            ErrorCode = error?.Code,
            ErrorSummary = error?.Summary,
            ErrorTechnicalDetail = error?.TechnicalDetail,
            ErrorPhase = error?.Phase,
            ErrorIsRetryable = error?.IsRetryable,
            ErrorSuggestedActionsJson = error is null
                ? null
                : SerializeBounded(error.SuggestedActions, MaximumErrorActionsJsonBytes),
            ErrorContextJson = error is null
                ? null
                : SerializeBounded(error.Context, MaximumErrorContextJsonBytes),
        };
    }

    private static StepAttempt ToModel(RunStepEntity entity)
    {
        EnsureBounded(entity.StepId, 128);
        if (!TryParseDefined(entity.Outcome, out StepAttemptOutcome outcome))
        {
            throw new PersistenceDataException();
        }

        var hasErrorData = entity.ErrorSummary is not null
            || entity.ErrorTechnicalDetail is not null
            || entity.ErrorPhase is not null
            || entity.ErrorIsRetryable is not null
            || entity.ErrorSuggestedActionsJson is not null
            || entity.ErrorContextJson is not null;
        if (entity.ErrorCode is null && hasErrorData)
        {
            throw new PersistenceDataException();
        }

        var error = entity.ErrorCode is null ? null : ReadStepError(entity);
        return RequireValid(StepAttempt.Rehydrate(
            entity.StepId,
            entity.AttemptNumber,
            ToTimestamp(entity.StartedAtUnixMs),
            entity.CompletedAtUnixMs is null ? null : ToTimestamp(entity.CompletedAtUnixMs.Value),
            outcome,
            entity.ExitCode,
            error));
    }

    private static DevForgeError ReadStepError(RunStepEntity entity)
    {
        if (entity.ErrorSummary is null
            || entity.ErrorTechnicalDetail is null
            || entity.ErrorPhase is null
            || entity.ErrorIsRetryable is null
            || entity.ErrorSuggestedActionsJson is null
            || entity.ErrorContextJson is null)
        {
            throw new PersistenceDataException();
        }

        var dto = new ErrorDto
        {
            Code = entity.ErrorCode!,
            Summary = entity.ErrorSummary,
            TechnicalDetail = entity.ErrorTechnicalDetail,
            Phase = entity.ErrorPhase,
            StepId = entity.StepId,
            IsRetryable = entity.ErrorIsRetryable.Value,
            SuggestedActions = DeserializeStringArray(entity.ErrorSuggestedActionsJson),
            Context = DeserializeContext(entity.ErrorContextJson),
        };
        return FromDto(dto);
    }

    private static string SerializeErrors(IEnumerable<DevForgeError> errors)
    {
        return SerializeBounded(errors.Select(ToDto).ToArray(), MaximumErrorsJsonBytes);
    }

    private static DevForgeError[] DeserializeErrors(string json)
    {
        try
        {
            EnsureJsonBound(json, MaximumErrorsJsonBytes);
            var errors = JsonSerializer.Deserialize<ErrorDto[]>(json, _jsonOptions)
                ?? throw new PersistenceDataException();
            return errors.Select(FromDto).ToArray();
        }
        catch (JsonException)
        {
            throw new PersistenceDataException();
        }
    }

    private static ErrorDto ToDto(DevForgeError error)
    {
        EnsureSafeDiagnostic(error.Code, 64);
        EnsureSafeDiagnostic(error.Summary, 1_024);
        EnsureSafeDiagnostic(error.TechnicalDetail.Value, 4_096);
        EnsureSafeDiagnostic(error.Phase, 128);
        if (error.StepId is not null)
        {
            EnsureBounded(error.StepId, 128);
        }

        foreach (var action in error.SuggestedActions)
        {
            EnsureSafeDiagnostic(action, 1_024);
        }

        foreach (var item in error.RedactedContext)
        {
            EnsureBounded(item.Key, 128);
            EnsureSafeDiagnostic(item.Value.Value, 4_096);
        }

        return new ErrorDto
        {
            Code = error.Code,
            Summary = error.Summary,
            TechnicalDetail = error.TechnicalDetail.Value,
            Phase = error.Phase,
            StepId = error.StepId,
            IsRetryable = error.IsRetryable,
            SuggestedActions = [.. error.SuggestedActions],
            Context = error.RedactedContext.ToDictionary(
                item => item.Key,
                item => item.Value.Value,
                StringComparer.Ordinal),
        };
    }

    private static DevForgeError FromDto(ErrorDto dto)
    {
        if (dto.Code is null
            || dto.Summary is null
            || dto.TechnicalDetail is null
            || dto.Phase is null
            || dto.SuggestedActions is null
            || dto.Context is null)
        {
            throw new PersistenceDataException();
        }

        EnsureSafeDiagnostic(dto.Code, 64);
        EnsureSafeDiagnostic(dto.Summary, 1_024);
        EnsureSafeDiagnostic(dto.TechnicalDetail, 4_096);
        EnsureSafeDiagnostic(dto.Phase, 128);
        if (dto.StepId is not null)
        {
            EnsureBounded(dto.StepId, 128);
        }

        var technicalDetail = RequireValid(RedactedText.FromTrustedRedaction(dto.TechnicalDetail));
        var actions = dto.SuggestedActions.Select(action =>
        {
            EnsureSafeDiagnostic(action, 1_024);
            return action;
        }).ToArray();
        var context = dto.Context.Select(item =>
        {
            EnsureBounded(item.Key, 128);
            EnsureSafeDiagnostic(item.Value, 4_096);
            return KeyValuePair.Create(
                item.Key,
                RequireValid(RedactedText.FromTrustedRedaction(item.Value)));
        }).ToArray();
        return RequireValid(DevForgeError.Create(
            dto.Code,
            dto.Summary,
            technicalDetail,
            dto.Phase,
            dto.StepId,
            dto.IsRetryable,
            actions,
            context));
    }

    private static string[] DeserializeStringArray(string json)
    {
        try
        {
            EnsureJsonBound(json, MaximumErrorActionsJsonBytes);
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new PersistenceDataException();
            }

            return document.RootElement.EnumerateArray().Select(item =>
                item.ValueKind == JsonValueKind.String
                    ? item.GetString()!
                    : throw new PersistenceDataException()).ToArray();
        }
        catch (JsonException)
        {
            throw new PersistenceDataException();
        }
    }

    private static Dictionary<string, string> DeserializeContext(string json)
    {
        try
        {
            EnsureJsonBound(json, MaximumErrorContextJsonBytes);
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new PersistenceDataException();
            }

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String
                    || !result.TryAdd(property.Name, property.Value.GetString()!))
                {
                    throw new PersistenceDataException();
                }
            }

            return result;
        }
        catch (JsonException)
        {
            throw new PersistenceDataException();
        }
    }

    private static string SerializeBounded<T>(T value, int maximumBytes)
    {
        var json = JsonSerializer.Serialize(value, _jsonOptions);
        EnsureJsonBound(json, maximumBytes);
        return json;
    }

    private static void EnsureJsonBound(string value, int maximumBytes)
    {
        if (string.IsNullOrWhiteSpace(value) || Encoding.UTF8.GetByteCount(value) > maximumBytes)
        {
            throw new PersistenceDataException();
        }
    }

    private static void ValidateRunIdentity(ProjectRun run)
    {
        EnsureBounded(run.Id, 128);
        EnsureBounded(run.RecipeId, 128);
        if (run.CurrentStepId is not null)
        {
            EnsureBounded(run.CurrentStepId, 128);
        }
    }

    private static void EnsureSafeDiagnostic(string value, int maximumLength)
    {
        EnsureBounded(value, maximumLength);
        if (!RedactedText.FromTrustedRedaction(value).IsValid)
        {
            throw new PersistenceDataException();
        }
    }

    private static void EnsureBounded(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength || value.Contains('\0'))
        {
            throw new PersistenceDataException();
        }
    }

    private static T RequireValid<T>(ValidationResult<T> result)
    {
        return result.IsValid ? result.Value : throw new PersistenceDataException();
    }

    private static DateTimeOffset ToTimestamp(long unixMilliseconds)
    {
        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new PersistenceDataException();
        }
    }

    private static bool TryParseDefined<TEnum>(string value, out TEnum parsed)
        where TEnum : struct, Enum
    {
        return Enum.TryParse(value, ignoreCase: false, out parsed)
            && Enum.IsDefined(parsed)
            && string.Equals(value, parsed.ToString(), StringComparison.Ordinal);
    }

    private static bool IsTerminal(RunStatus status) => status is RunStatus.PreflightFailed
        or RunStatus.ValidationFailed
        or RunStatus.Completed
        or RunStatus.Cancelled
        or RunStatus.Failed;

    private sealed class ErrorDto
    {
        public string? Code { get; set; }

        public string? Summary { get; set; }

        public string? TechnicalDetail { get; set; }

        public string? Phase { get; set; }

        public string? StepId { get; set; }

        public bool IsRetryable { get; set; }

        public string[]? SuggestedActions { get; set; }

        public Dictionary<string, string>? Context { get; set; }
    }
}
