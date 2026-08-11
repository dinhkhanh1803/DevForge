using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevForge.Domain.Execution;
using DevForge.Domain.Validation;

namespace DevForge.Infrastructure.Persistence.Mapping;

internal static class CheckpointPlanCodec
{
    public const int MaximumPlanJsonBytes = 1_048_576;
    private static readonly UTF8Encoding _strictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        MaxDepth = 128,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static EncodedPlan Encode(ExecutionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(ToDto(plan), _jsonOptions);
            if (bytes.Length > MaximumPlanJsonBytes)
            {
                throw new PersistenceDataException();
            }

            return new EncodedPlan(
                _strictUtf8.GetString(bytes),
                ComputeDigest(bytes));
        }
        catch (Exception exception) when (IsDataException(exception))
        {
            throw new PersistenceDataException();
        }
    }

    public static ExecutionPlan Decode(
        string json,
        string expectedBodyChecksum,
        string expectedPlanHash)
    {
        try
        {
            var bytes = _strictUtf8.GetBytes(json);
            if (bytes.Length > MaximumPlanJsonBytes
                || !StringComparer.Ordinal.Equals(ComputeDigest(bytes), expectedBodyChecksum))
            {
                throw new PersistenceDataException();
            }

            using (var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 128,
            }))
            {
                RejectDuplicateProperties(document.RootElement);
            }

            var dto = JsonSerializer.Deserialize<PlanDto>(bytes, _jsonOptions)
                ?? throw new PersistenceDataException();
            var plan = FromDto(dto);
            if (!StringComparer.Ordinal.Equals(plan.Id, expectedPlanHash))
            {
                throw new PersistenceDataException();
            }

            var canonical = Encode(plan);
            if (!StringComparer.Ordinal.Equals(json, canonical.Json)
                || !StringComparer.Ordinal.Equals(expectedBodyChecksum, canonical.BodyChecksum))
            {
                throw new PersistenceDataException();
            }

            return plan;
        }
        catch (Exception exception) when (IsDataException(exception))
        {
            throw new PersistenceDataException();
        }
    }

    private static PlanDto ToDto(ExecutionPlan plan)
    {
        return new PlanDto
        {
            Id = plan.Id,
            Steps = [.. plan.Steps.Select(step => new StepDto
            {
                Id = step.Id,
                Name = step.Name,
                Handler = step.Handler,
                Inputs = [.. step.Inputs
                    .OrderBy(item => item.Key, StringComparer.Ordinal)
                    .Select(item => new NamedValueDto
                    {
                        Name = item.Key,
                        Value = ToDto(item.Value),
                    })],
                TimeoutTicks = step.Timeout.Ticks,
                Retry = new RetryDto
                {
                    Mode = step.RetryPolicy.Mode.ToString(),
                    MaxAttempts = step.RetryPolicy.MaxAttempts,
                    DelayTicks = step.RetryPolicy.Delay.Ticks,
                    BackoffMultiplier = step.RetryPolicy.BackoffMultiplier,
                },
            })],
            Validators = [.. plan.Validators.Select(validator => new ValidatorDto
            {
                Id = validator.Id,
                Handler = validator.Handler,
                Inputs = [.. validator.Inputs
                    .OrderBy(item => item.Key, StringComparer.Ordinal)
                    .Select(item => new NamedValueDto
                    {
                        Name = item.Key,
                        Value = ToDto(item.Value),
                    })],
                TimeoutTicks = validator.Timeout.Ticks,
                Required = validator.Required,
            })],
        };
    }

    private static ValueDto ToDto(PlanValue value)
    {
        return value.Kind switch
        {
            PlanValueKind.Text => new ValueDto
            {
                Kind = "text",
                Text = value.StringValue,
            },
            PlanValueKind.Boolean => new ValueDto
            {
                Kind = "boolean",
                Boolean = value.BooleanValue,
            },
            PlanValueKind.WholeNumber => new ValueDto
            {
                Kind = "wholeNumber",
                Integer = value.IntegerValue,
            },
            PlanValueKind.Sequence => new ValueDto
            {
                Kind = "sequence",
                Items = [.. value.ArrayValue.Select(ToDto)],
            },
            PlanValueKind.Map => new ValueDto
            {
                Kind = "map",
                Entries = [.. value.ObjectValue
                    .OrderBy(item => item.Key, StringComparer.Ordinal)
                    .Select(item => new NamedValueDto
                    {
                        Name = item.Key,
                        Value = ToDto(item.Value),
                    })],
            },
            _ => throw new PersistenceDataException(),
        };
    }

    private static ExecutionPlan FromDto(PlanDto dto)
    {
        if (dto.Steps is null || dto.Validators is null)
        {
            throw new PersistenceDataException();
        }

        var steps = dto.Steps.Select(FromDto).ToArray();
        var validators = dto.Validators.Select(FromDto).ToArray();
        return RequireValid(ExecutionPlan.Create(dto.Id, steps, validators));
    }

    private static ExecutionStep FromDto(StepDto? dto)
    {
        if (dto?.Inputs is null || dto.Retry is null)
        {
            throw new PersistenceDataException();
        }

        var retry = RequireValid(RetryPolicy.Create(
            ParseDefined<RetryMode>(dto.Retry.Mode),
            dto.Retry.MaxAttempts,
            TimeSpan.FromTicks(dto.Retry.DelayTicks),
            dto.Retry.BackoffMultiplier));
        return RequireValid(ExecutionStep.Create(
            dto.Id,
            dto.Name,
            dto.Handler,
            ReadInputs(dto.Inputs),
            TimeSpan.FromTicks(dto.TimeoutTicks),
            retry));
    }

    private static ExecutionValidator FromDto(ValidatorDto? dto)
    {
        if (dto?.Inputs is null)
        {
            throw new PersistenceDataException();
        }

        return RequireValid(ExecutionValidator.Create(
            dto.Id,
            dto.Handler,
            ReadInputs(dto.Inputs),
            TimeSpan.FromTicks(dto.TimeoutTicks),
            dto.Required));
    }

    private static IEnumerable<KeyValuePair<string, PlanValue?>> ReadInputs(
        IEnumerable<NamedValueDto?> inputs)
    {
        return inputs.Select(input => input is null
            ? default
            : KeyValuePair.Create<string, PlanValue?>(input.Name!, FromDto(input.Value)));
    }

    private static PlanValue FromDto(ValueDto? dto)
    {
        if (dto is null)
        {
            throw new PersistenceDataException();
        }

        return dto.Kind switch
        {
            "text" when dto.Text is not null && HasOnlyText(dto) =>
                RequireValid(PlanValue.FromString(dto.Text)),
            "boolean" when dto.Boolean is not null && HasOnlyBoolean(dto) =>
                PlanValue.FromBoolean(dto.Boolean.Value),
            "wholeNumber" when dto.Integer is not null && HasOnlyInteger(dto) =>
                PlanValue.FromInteger(dto.Integer.Value),
            "sequence" when dto.Items is not null && HasOnlyItems(dto) =>
                RequireValid(PlanValue.FromArray(dto.Items.Select(FromDto))),
            "map" when dto.Entries is not null && HasOnlyEntries(dto) =>
                RequireValid(PlanValue.FromObject(dto.Entries.Select(entry => entry is null
                    ? default
                    : KeyValuePair.Create<string, PlanValue?>(entry.Name!, FromDto(entry.Value))))),
            _ => throw new PersistenceDataException(),
        };
    }

    private static bool HasOnlyText(ValueDto value) =>
        value.Boolean is null && value.Integer is null && value.Items is null && value.Entries is null;

    private static bool HasOnlyBoolean(ValueDto value) =>
        value.Text is null && value.Integer is null && value.Items is null && value.Entries is null;

    private static bool HasOnlyInteger(ValueDto value) =>
        value.Text is null && value.Boolean is null && value.Items is null && value.Entries is null;

    private static bool HasOnlyItems(ValueDto value) =>
        value.Text is null && value.Boolean is null && value.Integer is null && value.Entries is null;

    private static bool HasOnlyEntries(ValueDto value) =>
        value.Text is null && value.Boolean is null && value.Integer is null && value.Items is null;

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

    private static string ComputeDigest(ReadOnlySpan<byte> bytes)
    {
        return $"sha256:{Convert.ToHexStringLower(SHA256.HashData(bytes))}";
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
            or DecoderFallbackException
            or EncoderFallbackException
            or ArgumentException
            or InvalidOperationException
            or OverflowException
            or PersistenceDataException;
    }

    internal sealed record EncodedPlan(string Json, string BodyChecksum);

    private sealed class PlanDto
    {
        public string? Id { get; set; }

        public StepDto?[]? Steps { get; set; }

        public ValidatorDto?[]? Validators { get; set; }
    }

    private sealed class StepDto
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public string? Handler { get; set; }

        public NamedValueDto?[]? Inputs { get; set; }

        public long TimeoutTicks { get; set; }

        public RetryDto? Retry { get; set; }
    }

    private sealed class ValidatorDto
    {
        public string? Id { get; set; }

        public string? Handler { get; set; }

        public NamedValueDto?[]? Inputs { get; set; }

        public long TimeoutTicks { get; set; }

        public bool Required { get; set; }
    }

    private sealed class RetryDto
    {
        public string? Mode { get; set; }

        public int MaxAttempts { get; set; }

        public long DelayTicks { get; set; }

        public double BackoffMultiplier { get; set; }
    }

    private sealed class NamedValueDto
    {
        public string? Name { get; set; }

        public ValueDto? Value { get; set; }
    }

    private sealed class ValueDto
    {
        public string? Kind { get; set; }

        public string? Text { get; set; }

        public bool? Boolean { get; set; }

        public long? Integer { get; set; }

        public ValueDto?[]? Items { get; set; }

        public NamedValueDto?[]? Entries { get; set; }
    }
}
