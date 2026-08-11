using System.Collections.Immutable;
using System.Globalization;
using DevForge.Application.Contracts;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Execution;
using DevForge.Domain.Projects;
using DevForge.Domain.Validation;

namespace DevForge.Application.Planning;

public interface IInputSchemaValidator
{
    ValidationResult<EffectiveRecipeConfiguration> Validate(
        ProjectRecipe? recipe,
        ResolvedBlueprint? blueprint,
        CancellationToken cancellationToken);
}

public sealed class EffectiveRecipeConfiguration
{
    internal EffectiveRecipeConfiguration(
        ImmutableSortedDictionary<string, PlanValue> inputs,
        ImmutableArray<string> enabledFeatures)
    {
        Inputs = inputs;
        EnabledFeatures = enabledFeatures;
    }

    public ImmutableSortedDictionary<string, PlanValue> Inputs { get; }

    public ImmutableArray<string> EnabledFeatures { get; }
}

public sealed class InputSchemaValidator : IInputSchemaValidator
{
    public ValidationResult<EffectiveRecipeConfiguration> Validate(
        ProjectRecipe? recipe,
        ResolvedBlueprint? blueprint,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (recipe is null || blueprint is null)
        {
            return Failure<EffectiveRecipeConfiguration>(
                "A project recipe and resolved blueprint are required.",
                "recipe");
        }

        var issues = new List<ValidationIssue>();
        if (recipe.Inputs.Count > BlueprintValue.MaximumCollectionItems)
        {
            issues.Add(Issue("The recipe input collection exceeds the supported bound.", "inputs"));
        }

        if (recipe.Features.Length > BlueprintValue.MaximumCollectionItems)
        {
            issues.Add(Issue("The recipe feature collection exceeds the supported bound.", "features"));
        }

        if (!StringComparer.Ordinal.Equals(recipe.BlueprintId, blueprint.Manifest.Id)
            || !StringComparer.Ordinal.Equals(recipe.BlueprintVersion, blueprint.Manifest.Version))
        {
            issues.Add(Issue(
                "The recipe must reference the exact resolved blueprint identity.",
                "blueprint"));
        }

        var schemaById = blueprint.InputSchema.ToDictionary(item => item.Id, StringComparer.Ordinal);
        foreach (var input in recipe.Inputs.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!schemaById.ContainsKey(input.Key))
            {
                issues.Add(Issue("The recipe contains an unknown blueprint input.", $"inputs.{input.Key}"));
            }
        }

        var effectiveInputs = ImmutableSortedDictionary.CreateBuilder<string, PlanValue>(
            StringComparer.Ordinal);
        foreach (var definition in blueprint.InputSchema.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            PlanValue? value;
            if (recipe.Inputs.TryGetValue(definition.Id, out var rawValue))
            {
                value = Parse(rawValue, definition, issues);
            }
            else if (definition.DefaultValue is not null)
            {
                value = ConvertDefault(definition.DefaultValue, issues, definition.Id);
            }
            else
            {
                value = null;
                if (definition.Required)
                {
                    issues.Add(Issue("A required blueprint input is missing.", $"inputs.{definition.Id}"));
                }
            }

            if (value is not null && ValidateConstraints(value, definition, issues))
            {
                effectiveInputs.Add(definition.Id, value);
            }
        }

        var featureDefinitions = blueprint.Manifest.Features.ToDictionary(
            item => item.Id,
            StringComparer.Ordinal);
        var selectedFeatures = new HashSet<string>(StringComparer.Ordinal);
        foreach (var feature in recipe.Features)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!featureDefinitions.ContainsKey(feature))
            {
                issues.Add(Issue("The recipe contains an unknown blueprint feature.", "features"));
            }

            if (!selectedFeatures.Add(feature))
            {
                issues.Add(Issue("Recipe feature identifiers must be unique.", "features"));
            }
        }

        foreach (var feature in featureDefinitions.Values.Where(item => item.DefaultEnabled))
        {
            selectedFeatures.Add(feature.Id);
        }

        return issues.Count == 0
            ? ValidationResult.Success(new EffectiveRecipeConfiguration(
                effectiveInputs.ToImmutable(),
                [.. selectedFeatures.OrderBy(item => item, StringComparer.Ordinal)]))
            : ValidationResult.Failure<EffectiveRecipeConfiguration>(issues);
    }

    private static PlanValue? Parse(
        string rawValue,
        BlueprintInputPropertyDefinition definition,
        List<ValidationIssue> issues)
    {
        switch (definition.Kind)
        {
            case BlueprintInputKind.Text:
            case BlueprintInputKind.Choice:
                var text = rawValue.Length <= BlueprintValue.MaximumTextLength
                    ? PlanValue.FromString(rawValue)
                    : ValidationResult.Failure<PlanValue>(
                    [
                        Issue("A recipe input exceeds the supported text bound.", $"inputs.{definition.Id}"),
                    ]);
                if (text.IsValid)
                {
                    return text.Value;
                }

                break;
            case BlueprintInputKind.Boolean:
                if (rawValue == "true")
                {
                    return PlanValue.FromBoolean(true);
                }

                if (rawValue == "false")
                {
                    return PlanValue.FromBoolean(false);
                }

                break;
            case BlueprintInputKind.WholeNumber:
                if (rawValue == rawValue.Trim()
                    && long.TryParse(
                        rawValue,
                        NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture,
                        out var number))
                {
                    return PlanValue.FromInteger(number);
                }

                break;
        }

        issues.Add(Issue("A recipe input does not match its declared safe type.", $"inputs.{definition.Id}"));
        return null;
    }

    private static PlanValue? ConvertDefault(
        BlueprintValue value,
        List<ValidationIssue> issues,
        string id)
    {
        switch (value.Kind)
        {
            case BlueprintValueKind.Text:
                var text = PlanValue.FromString(value.StringValue);
                if (text.IsValid)
                {
                    return text.Value;
                }

                break;
            case BlueprintValueKind.Boolean:
                return PlanValue.FromBoolean(value.BooleanValue);
            case BlueprintValueKind.WholeNumber:
                return PlanValue.FromInteger(value.IntegerValue);
        }

        issues.Add(Issue("A blueprint input default is invalid.", $"inputs.{id}"));
        return null;
    }

    private static bool ValidateConstraints(
        PlanValue value,
        BlueprintInputPropertyDefinition definition,
        List<ValidationIssue> issues)
    {
        var valid = true;
        if (value.Kind == PlanValueKind.Text
            && (definition.MinimumLength is { } minimumLength
                    && value.StringValue!.Length < minimumLength
                || definition.MaximumLength is { } maximumLength
                    && value.StringValue!.Length > maximumLength))
        {
            issues.Add(Issue("A recipe input violates its supported text length.", $"inputs.{definition.Id}"));
            valid = false;
        }

        if (value.Kind == PlanValueKind.WholeNumber
            && (definition.Minimum is { } minimum
                    && value.IntegerValue < minimum
                || definition.Maximum is { } maximum
                    && value.IntegerValue > maximum))
        {
            issues.Add(Issue("A recipe input violates its supported numeric range.", $"inputs.{definition.Id}"));
            valid = false;
        }

        if (!definition.AllowedValues.IsEmpty
            && !definition.AllowedValues.Contains(Format(value), StringComparer.Ordinal))
        {
            issues.Add(Issue("A recipe input is not in its supported value set.", $"inputs.{definition.Id}"));
            valid = false;
        }

        return valid;
    }

    private static string Format(PlanValue value)
    {
        return value.Kind switch
        {
            PlanValueKind.Text => value.StringValue!,
            PlanValueKind.Boolean => value.BooleanValue ? "true" : "false",
            PlanValueKind.WholeNumber => value.IntegerValue.ToString(CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException("An input schema produced a non-scalar value."),
        };
    }

    private static ValidationIssue Issue(string message, string location)
    {
        return new ValidationIssue("DF-PLAN-001", message, location);
    }

    private static ValidationResult<T> Failure<T>(string message, string location)
    {
        return ValidationResult.Failure<T>([Issue(message, location)]);
    }
}
