using DevForge.Application.Contracts;
using DevForge.Application.Planning;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Execution;
using DevForge.Domain.Projects;

namespace DevForge.UnitTests.Application.Planning;

public sealed class InputSchemaValidatorTests
{
    [Fact]
    public void ValidateProducesTypedDefaultsAndDeterministicEffectiveFeatures()
    {
        var schema = StandardSchema();
        var recipe = Recipe(
            new Dictionary<string, string?>
            {
                ["mode"] = "worker",
                ["retries"] = "5",
                ["enabled"] = "false",
            },
            ["api"]);

        var result = new InputSchemaValidator().Validate(
            recipe,
            Blueprint(schema),
            CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal(["enabled", "mode", "project-name", "retries"], result.Value.Inputs.Keys);
        Assert.False(result.Value.Inputs["enabled"].BooleanValue);
        Assert.Equal("worker", result.Value.Inputs["mode"].StringValue);
        Assert.Equal("sample", result.Value.Inputs["project-name"].StringValue);
        Assert.Equal(5, result.Value.Inputs["retries"].IntegerValue);
        Assert.Equal(["api", "logging"], result.Value.EnabledFeatures.ToArray());
    }

    [Fact]
    public void ValidateDoesNotReplacePresentInvalidValueWithDefault()
    {
        var recipe = Recipe(
            new Dictionary<string, string?>
            {
                ["project-name"] = string.Empty,
                ["mode"] = "api",
            },
            []);

        var result = new InputSchemaValidator().Validate(
            recipe,
            Blueprint(StandardSchema()),
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.All(result.Issues, issue => Assert.Equal("DF-PLAN-001", issue.Code));
    }

    [Fact]
    public void ValidateAggregatesRequiredUnknownTypeRangeChoiceFeatureAndCredentialFailures()
    {
        var schema = StandardSchema(includeProjectDefault: false);
        var recipe = Recipe(
            new Dictionary<string, string?>
            {
                ["enabled"] = "yes",
                ["retries"] = "99",
                ["mode"] = "unsupported",
                ["unknown"] = "value",
                ["display-name"] = "Bearer abcdefghijklmnop",
            },
            ["missing-feature", "missing-feature"]);

        var result = new InputSchemaValidator().Validate(
            recipe,
            Blueprint(schema),
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.True(result.Issues.Length >= 7);
        Assert.All(result.Issues, issue => Assert.Equal("DF-PLAN-001", issue.Code));
    }

    [Fact]
    public void ValidateNormalizesEquivalentInputEnumerationToTheSameOrdinalMap()
    {
        var first = Recipe(
            new Dictionary<string, string?>
            {
                ["mode"] = "api",
                ["enabled"] = "true",
                ["retries"] = "3",
            },
            ["api"]);
        var second = Recipe(
            new Dictionary<string, string?>
            {
                ["retries"] = "3",
                ["enabled"] = "true",
                ["mode"] = "api",
            },
            ["api"]);
        var blueprint = Blueprint(StandardSchema());

        var firstResult = new InputSchemaValidator().Validate(first, blueprint, CancellationToken.None);
        var secondResult = new InputSchemaValidator().Validate(second, blueprint, CancellationToken.None);

        Assert.Equal(firstResult.Value.Inputs.Keys, secondResult.Value.Inputs.Keys);
        Assert.All(firstResult.Value.Inputs, item => Assert.Equal(item.Value, secondResult.Value.Inputs[item.Key]));
    }

    [Fact]
    public void ValidateHonorsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            new InputSchemaValidator().Validate(
                Recipe(new Dictionary<string, string?>(), []),
                Blueprint(StandardSchema()),
                cancellation.Token));
    }

    [Fact]
    public void ValidateRejectsTextBeyondTheGlobalBlueprintScalarBound()
    {
        var recipe = Recipe(
            new Dictionary<string, string?>
            {
                ["project-name"] = "sample",
                ["mode"] = "api",
                ["display-name"] = new string('x', BlueprintValue.MaximumTextLength + 1),
            },
            []);

        var result = new InputSchemaValidator().Validate(
            recipe,
            Blueprint(StandardSchema(boundDisplayName: false)),
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.All(result.Issues, issue => Assert.Equal("DF-PLAN-001", issue.Code));
    }

    private static ProjectRecipe Recipe(
        IReadOnlyDictionary<string, string?> inputs,
        IReadOnlyCollection<string?> features)
    {
        return ProjectRecipe.Create(
            new ProjectRecipeDraft(
                "Sample Project",
                "C:\\projects\\sample",
                "sample.blueprint",
                "1.0.0",
                inputs,
                features)).Value;
    }

    private static ResolvedBlueprint Blueprint(
        IReadOnlyCollection<BlueprintInputPropertyDefinition> schema)
    {
        var legacyInputs = schema.Select(item => new InputDefinition(
            item.Id,
            item.Kind,
            item.Required,
            FormatDefault(item.DefaultValue))).ToArray();
        var manifest = BlueprintManifest.Create(
            new BlueprintManifestDraft(
                "sample.blueprint",
                "1.0.0",
                ">=1.0.0 <2.0.0",
                [],
                legacyInputs,
                [],
                [],
                [],
                Features:
                [
                    new BlueprintFeatureDefinition("api", false),
                    new BlueprintFeatureDefinition("logging", true),
                ]),
            new BlueprintTrustAssignment(BlueprintTrust.BuiltIn)).Value;
        var fingerprint = BlueprintFingerprint.Create(
            "built-in",
            WorkspaceRelativePath.Create("sample.blueprint").Value,
            BlueprintTrust.BuiltIn,
            $"sha256:{new string('a', 64)}").Value;
        return ResolvedBlueprint.Create(manifest, schema, fingerprint).Value;
    }

    private static BlueprintInputPropertyDefinition[] StandardSchema(
        bool includeProjectDefault = true,
        bool boundDisplayName = true)
    {
        return
        [
            Input(
                "project-name",
                BlueprintInputKind.Text,
                required: true,
                includeProjectDefault ? BlueprintValue.FromString("sample").Value : null,
                minimumLength: 1,
                maximumLength: 80),
            Input(
                "display-name",
                BlueprintInputKind.Text,
                required: false,
                null,
                minimumLength: 1,
                maximumLength: boundDisplayName ? 80 : null),
            Input(
                "enabled",
                BlueprintInputKind.Boolean,
                required: false,
                BlueprintValue.FromBoolean(true)),
            Input(
                "retries",
                BlueprintInputKind.WholeNumber,
                required: false,
                BlueprintValue.FromInteger(3),
                minimum: 1,
                maximum: 5),
            Input(
                "mode",
                BlueprintInputKind.Choice,
                required: true,
                BlueprintValue.FromString("api").Value,
                ["api", "worker"]),
        ];
    }

    private static BlueprintInputPropertyDefinition Input(
        string id,
        BlueprintInputKind kind,
        bool required,
        BlueprintValue? defaultValue,
        IReadOnlyCollection<string?>? allowedValues = null,
        int? minimumLength = null,
        int? maximumLength = null,
        long? minimum = null,
        long? maximum = null)
    {
        return BlueprintInputPropertyDefinition.Create(
            new BlueprintInputPropertyDraft(
                id,
                kind,
                required,
                defaultValue,
                allowedValues ?? [],
                minimumLength,
                maximumLength,
                minimum,
                maximum)).Value;
    }

    private static string? FormatDefault(BlueprintValue? value)
    {
        return value?.Kind switch
        {
            null => null,
            BlueprintValueKind.Text => value.StringValue,
            BlueprintValueKind.Boolean => value.BooleanValue ? "true" : "false",
            BlueprintValueKind.WholeNumber => value.IntegerValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException(),
        };
    }
}
