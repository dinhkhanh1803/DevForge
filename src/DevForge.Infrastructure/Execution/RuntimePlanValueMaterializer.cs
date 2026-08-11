using System.Collections.Immutable;
using DevForge.Application.Contracts;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Execution;
using DevForge.Domain.Privacy;
using DevForge.Domain.Validation;

namespace DevForge.Infrastructure.Execution;

internal enum RuntimeValueAvailability
{
    PreFinalization = 1,
    PostFinalizationBuiltIn = 2,
}

internal sealed class RuntimePlanValueContext
{
    private RuntimePlanValueContext(
        string runId,
        string stagingPath,
        string? targetPath,
        RuntimeValueAvailability availability)
    {
        RunId = runId;
        StagingPath = stagingPath;
        TargetPath = targetPath;
        Availability = availability;
    }

    internal string RunId { get; }

    internal string StagingPath { get; }

    internal string? TargetPath { get; }

    internal RuntimeValueAvailability Availability { get; }

    internal static ValidationResult<RuntimePlanValueContext> Create(
        string? runId,
        WorkspaceRoot? stagingRoot,
        WorkspaceRoot? targetRoot,
        RuntimeValueAvailability availability,
        BlueprintTrust trust)
    {
        var valid = ExecutionContractValidation.IsBoundedIdentifier(runId)
            && stagingRoot is not null
            && Enum.IsDefined(availability)
            && trust is BlueprintTrust.BuiltIn or BlueprintTrust.TrustedLocal
            && (availability == RuntimeValueAvailability.PreFinalization && targetRoot is null
                || availability == RuntimeValueAvailability.PostFinalizationBuiltIn
                    && trust == BlueprintTrust.BuiltIn
                    && targetRoot is not null);
        return valid
            ? ValidationResult.Success(new RuntimePlanValueContext(
                runId!,
                stagingRoot!.RevealForFileSystem(),
                targetRoot?.RevealForFileSystem(),
                availability))
            : Failure<RuntimePlanValueContext>();
    }

    private static ValidationResult<T> Failure<T>()
    {
        return ValidationResult.Failure<T>(
        [
            new ValidationIssue(
                "DF-EXEC-001",
                "The runtime value context is invalid for the current execution phase.",
                "context"),
        ]);
    }
}

internal static class RuntimePlanValueMaterializer
{
    internal const int MaximumNodes = 8192;
    private const int MaximumTextCharacters = 1024 * 1024;

    internal static ValidationResult<ImmutableDictionary<string, PlanValue>> Materialize(
        IEnumerable<KeyValuePair<string, PlanValue?>>? inputs,
        RuntimePlanValueContext? context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = inputs?.ToImmutableArray() ?? [];
        if (inputs is null || context is null || snapshot.Length > PlanValue.MaximumCollectionItems)
        {
            return Failure();
        }

        try
        {
            var state = new MaterializationState(context, cancellationToken);
            var output = ImmutableDictionary.CreateBuilder<string, PlanValue>(StringComparer.Ordinal);
            foreach (var input in snapshot)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(input.Key)
                    || input.Key != input.Key.Trim()
                    || !state.AcceptKey(input.Key)
                    || input.Value is null
                    || !output.TryAdd(input.Key, state.Materialize(input.Value)))
                {
                    throw new MaterializationException();
                }
            }

            return ValidationResult.Success(output.ToImmutable());
        }
        catch (MaterializationException)
        {
            return Failure();
        }
    }

    private static ValidationResult<ImmutableDictionary<string, PlanValue>> Failure()
    {
        return ValidationResult.Failure<ImmutableDictionary<string, PlanValue>>(
        [
            new ValidationIssue(
                "DF-EXEC-001",
                "The typed execution values contain an unavailable, malformed, or excessive runtime value.",
                "inputs"),
        ]);
    }

    private sealed class MaterializationState(
        RuntimePlanValueContext context,
        CancellationToken cancellationToken)
    {
        private int _nodes;
        private int _textCharacters;

        internal bool AcceptKey(string key)
        {
            if (RedactedText.IsSecretShapedKey(key))
            {
                return false;
            }

            _textCharacters = checked(_textCharacters + key.Length);
            return _textCharacters <= MaximumTextCharacters;
        }

        internal PlanValue Materialize(PlanValue value)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++_nodes > MaximumNodes)
            {
                throw new MaterializationException();
            }

            return value.Kind switch
            {
                PlanValueKind.Text => CountText(value),
                PlanValueKind.Boolean or PlanValueKind.WholeNumber => value,
                PlanValueKind.Sequence => MaterializeSequence(value),
                PlanValueKind.Map => MaterializeMap(value),
                _ => throw new MaterializationException(),
            };
        }

        private PlanValue CountText(PlanValue value)
        {
            _textCharacters = checked(_textCharacters + value.StringValue!.Length);
            return _textCharacters <= MaximumTextCharacters
                ? value
                : throw new MaterializationException();
        }

        private PlanValue MaterializeSequence(PlanValue value)
        {
            var result = PlanValue.FromArray(value.ArrayValue.Select(Materialize));
            return result.IsValid ? result.Value : throw new MaterializationException();
        }

        private PlanValue MaterializeMap(PlanValue value)
        {
            if (value.ObjectValue.TryGetValue("placeholder", out var placeholder))
            {
                if (value.ObjectValue.Count != 1 || placeholder.Kind != PlanValueKind.Text)
                {
                    throw new MaterializationException();
                }

                return MaterializePlaceholder(placeholder.StringValue!);
            }

            var materialized = value.ObjectValue.Select(item =>
                AcceptKey(item.Key)
                    ? KeyValuePair.Create<string, PlanValue?>(item.Key, Materialize(item.Value))
                    : throw new MaterializationException());
            var result = PlanValue.FromObject(materialized);
            return result.IsValid ? result.Value : throw new MaterializationException();
        }

        private PlanValue MaterializePlaceholder(string identifier)
        {
            var materialized = identifier switch
            {
                "runtime.run-id" => context.RunId,
                "runtime.staging-path" => context.StagingPath,
                "project.target-path" when
                    context.Availability == RuntimeValueAvailability.PostFinalizationBuiltIn
                    && context.TargetPath is not null => context.TargetPath,
                _ => throw new MaterializationException(),
            };
            var value = PlanValue.FromString(materialized);
            return value.IsValid ? CountText(value.Value) : throw new MaterializationException();
        }
    }

    private sealed class MaterializationException : Exception;
}
