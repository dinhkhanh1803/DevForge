using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Execution;
using DevForge.Domain.Projects;

namespace DevForge.Application.Planning;

internal sealed record PlanHashInput(
    string BlueprintId,
    string BlueprintVersion,
    string BlueprintChecksum,
    ImmutableSortedDictionary<string, PlanValue> EffectiveInputs,
    ImmutableArray<string> EnabledFeatures,
    ImmutableSortedDictionary<string, string> TeamStandards,
    GitOptions Git,
    CompletionOptions Completion,
    ImmutableArray<ExecutionStep> Steps,
    ImmutableArray<ExecutionValidator> Validators,
    ImmutableArray<ToolRequirement> RequiredTools,
    ImmutableArray<BlueprintDependency> Dependencies,
    ImmutableArray<BlueprintArtifact> Artifacts);

internal interface ICanonicalPlanSerializer
{
    byte[] Serialize(PlanHashInput input);
}

internal sealed class CanonicalPlanSerializer : ICanonicalPlanSerializer
{
    public byte[] Serialize(PlanHashInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = false,
            SkipValidation = false,
        }))
        {
            writer.WriteStartObject();
            writer.WriteString("blueprintId", input.BlueprintId);
            writer.WriteString("blueprintVersion", input.BlueprintVersion);
            writer.WriteString("blueprintChecksum", input.BlueprintChecksum);
            WritePlanValueMap(writer, "effectiveInputs", input.EffectiveInputs);
            WriteStrings(writer, "enabledFeatures", input.EnabledFeatures);
            writer.WritePropertyName("teamStandards");
            writer.WriteStartObject();
            foreach (var item in input.TeamStandards.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                writer.WriteString(item.Key, item.Value);
            }

            writer.WriteEndObject();
            WriteGit(writer, input.Git);
            WriteCompletion(writer, input.Completion);
            WriteSteps(writer, input.Steps);
            WriteValidators(writer, input.Validators);
            writer.WritePropertyName("requiredTools");
            writer.WriteStartArray();
            foreach (var tool in input.RequiredTools)
            {
                writer.WriteStartObject();
                writer.WriteString("id", tool.Id);
                writer.WriteString("versionRange", tool.VersionRange);
                writer.WriteBoolean("required", tool.Required);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WritePropertyName("dependencies");
            writer.WriteStartArray();
            foreach (var dependency in input.Dependencies)
            {
                writer.WriteStartObject();
                writer.WriteString("id", dependency.Id);
                writer.WriteString("version", dependency.Version);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WritePropertyName("artifacts");
            writer.WriteStartArray();
            foreach (var artifact in input.Artifacts)
            {
                writer.WriteStringValue(artifact.Path);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static void WriteGit(Utf8JsonWriter writer, GitOptions git)
    {
        writer.WritePropertyName("git");
        writer.WriteStartObject();
        writer.WriteBoolean("initializeRepository", git.InitializeRepository);
        writer.WriteString("primaryBranch", git.PrimaryBranch);
        writer.WriteBoolean("useDevelopBranch", git.UseDevelopBranch);
        writer.WriteBoolean("publishToGitHub", git.PublishToGitHub);
        writer.WriteBoolean("isPrivate", git.IsPrivate);
        writer.WriteEndObject();
    }

    private static void WriteCompletion(Utf8JsonWriter writer, CompletionOptions completion)
    {
        writer.WritePropertyName("completion");
        writer.WriteStartObject();
        writer.WriteBoolean("writeGenerationReport", completion.WriteGenerationReport);
        writer.WriteBoolean("writeHandoffDocument", completion.WriteHandoffDocument);
        writer.WriteBoolean("openIde", completion.OpenIde);
        if (completion.IdeId is null)
        {
            writer.WriteNull("ideId");
        }
        else
        {
            writer.WriteString("ideId", completion.IdeId);
        }

        writer.WriteEndObject();
    }

    private static void WriteSteps(Utf8JsonWriter writer, ImmutableArray<ExecutionStep> steps)
    {
        writer.WritePropertyName("steps");
        writer.WriteStartArray();
        foreach (var step in steps)
        {
            writer.WriteStartObject();
            writer.WriteString("id", step.Id);
            writer.WriteString("name", step.Name);
            writer.WriteString("handler", step.Handler);
            WritePlanValueMap(writer, "inputs", step.Inputs);
            writer.WriteNumber("timeoutTicks", step.Timeout.Ticks);
            writer.WritePropertyName("retry");
            writer.WriteStartObject();
            writer.WriteString(
                "mode",
                step.RetryPolicy.Mode switch
                {
                    RetryMode.None => "none",
                    RetryMode.Manual => "manual",
                    RetryMode.AutomaticLimited => "automaticLimited",
                    _ => throw new InvalidOperationException("The retry mode is not supported."),
                });
            writer.WriteNumber("maxAttempts", step.RetryPolicy.MaxAttempts);
            writer.WriteNumber("delayTicks", step.RetryPolicy.Delay.Ticks);
            writer.WriteNumber("backoffMultiplier", step.RetryPolicy.BackoffMultiplier);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteValidators(
        Utf8JsonWriter writer,
        ImmutableArray<ExecutionValidator> validators)
    {
        writer.WritePropertyName("validators");
        writer.WriteStartArray();
        foreach (var validator in validators)
        {
            writer.WriteStartObject();
            writer.WriteString("id", validator.Id);
            writer.WriteString("handler", validator.Handler);
            WritePlanValueMap(writer, "inputs", validator.Inputs);
            writer.WriteNumber("timeoutTicks", validator.Timeout.Ticks);
            writer.WriteBoolean("required", validator.Required);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteStrings(
        Utf8JsonWriter writer,
        string name,
        ImmutableArray<string> values)
    {
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

    private static void WritePlanValueMap(
        Utf8JsonWriter writer,
        string name,
        IEnumerable<KeyValuePair<string, PlanValue>> values)
    {
        writer.WritePropertyName(name);
        writer.WriteStartObject();
        foreach (var item in values.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            writer.WritePropertyName(item.Key);
            WritePlanValue(writer, item.Value);
        }

        writer.WriteEndObject();
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
                foreach (var item in value.ObjectValue.OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(item.Key);
                    WritePlanValue(writer, item.Value);
                }

                writer.WriteEndObject();
                break;
            default:
                throw new InvalidOperationException("The plan value kind is not supported.");
        }
    }
}

internal sealed class PlanHasher(ICanonicalPlanSerializer serializer)
{
    public string Compute(PlanHashInput input)
    {
        var digest = SHA256.HashData(serializer.Serialize(input));
        return $"sha256:{Convert.ToHexStringLower(digest)}";
    }
}
