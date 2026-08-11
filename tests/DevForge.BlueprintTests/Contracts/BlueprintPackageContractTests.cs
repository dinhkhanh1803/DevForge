using System.Collections.Immutable;
using DevForge.Blueprints.Abstractions.Models;

namespace DevForge.BlueprintTests.Contracts;

public sealed class BlueprintPackageContractTests
{
    [Fact]
    public void ClosedEnumsUseOnlyDocumentedNonzeroValues()
    {
        Assert.Equal([1, 2, 3, 4, 5], Enum.GetValues<BlueprintValueKind>().Select(value => (int)value));
        Assert.Equal([1, 2], Enum.GetValues<CompatibilityRuleSeverity>().Select(value => (int)value));
        Assert.Equal([1], Enum.GetValues<CompatibilityRuleOverride>().Select(value => (int)value));
    }

    [Fact]
    public void BlueprintValueSnapshotsNestedTypedPayloads()
    {
        var source = new List<BlueprintValue?>
        {
            BlueprintValue.FromBoolean(true),
            BlueprintValue.FromInteger(10),
        };
        var sequence = BlueprintValue.FromArray(source);
        Assert.True(sequence.IsValid);

        var entries = new List<KeyValuePair<string, BlueprintValue?>>
        {
            new(" arguments ", sequence.Value),
        };
        var map = BlueprintValue.FromObject(entries);
        Assert.True(map.IsValid);

        source.Clear();
        entries.Clear();

        Assert.Equal(BlueprintValueKind.Map, map.Value.Kind);
        var arguments = Assert.Single(map.Value.ObjectValue);
        Assert.Equal("arguments", arguments.Key);
        Assert.Equal(2, arguments.Value.ArrayValue.Length);
    }

    [Fact]
    public void InputDefinitionGuardsTypedDefaultsAndConstraints()
    {
        var valid = BlueprintInputPropertyDefinition.Create(
            new BlueprintInputPropertyDraft(
                " framework ",
                BlueprintInputKind.Text,
                true,
                BlueprintValue.FromString("net10.0").Value,
                ["net10.0", "net11.0"],
                3,
                16,
                null,
                null));

        Assert.True(valid.IsValid);
        Assert.Equal("framework", valid.Value.Id);
        Assert.Equal(["net10.0", "net11.0"], valid.Value.AllowedValues.ToArray());

        var invalid = BlueprintInputPropertyDefinition.Create(
            new BlueprintInputPropertyDraft(
                "count",
                BlueprintInputKind.WholeNumber,
                false,
                BlueprintValue.FromString("ten").Value,
                [],
                5,
                2,
                10,
                1));

        Assert.Equal(
            [
                "blueprint.input.default.kind-mismatch",
                "blueprint.input.length.not-applicable",
                "blueprint.input.numeric-range.invalid",
            ],
            invalid.Issues.Select(issue => issue.Code));
    }

    [Fact]
    public void InputDefinitionRejectsDefaultsOutsideTheClosedChoiceSet()
    {
        var result = BlueprintInputPropertyDefinition.Create(
            new BlueprintInputPropertyDraft(
                "framework",
                BlueprintInputKind.Choice,
                true,
                BlueprintValue.FromString("net12.0").Value,
                ["net10.0", "net11.0"],
                null,
                null,
                null,
                null));

        Assert.Equal("blueprint.input.default.not-allowed", Assert.Single(result.Issues).Code);
    }

    [Fact]
    public void InputDefinitionRejectsIdentifiersAndCollectionsOutsideStableBounds()
    {
        var longIdentifier = $"a{new string('b', 128)}";
        var invalidIdentifier = BlueprintInputPropertyDefinition.Create(
            new BlueprintInputPropertyDraft(
                longIdentifier,
                BlueprintInputKind.Text,
                false,
                null,
                [],
                null,
                null,
                null,
                null));
        var tooManyChoices = BlueprintInputPropertyDefinition.Create(
            new BlueprintInputPropertyDraft(
                "framework",
                BlueprintInputKind.Choice,
                false,
                null,
                Enumerable.Range(0, BlueprintValue.MaximumCollectionItems + 1)
                    .Select(index => $"value-{index}")
                    .ToArray(),
                null,
                null,
                null,
                null));

        Assert.Equal("blueprint.input.id.invalid", Assert.Single(invalidIdentifier.Issues).Code);
        Assert.Contains(
            tooManyChoices.Issues,
            issue => issue.Code == "blueprint.input.choices.too-large");
    }

    [Fact]
    public void ManifestRejectsActionParameterKeysThatCollideAfterNormalization()
    {
        var parameters = ImmutableDictionary<string, BlueprintValue>.Empty
            .Add("path", BlueprintValue.FromString("src").Value)
            .Add(" path ", BlueprintValue.FromString("tests").Value);
        var result = BlueprintManifest.Create(
            ValidDraft() with
            {
                Actions =
                [
                    new BlueprintActionDefinition(
                        "create",
                        "create-directory",
                        parameters,
                        TimeSpan.FromMinutes(1)),
                ],
            },
            new BlueprintTrustAssignment(BlueprintTrust.BuiltIn));

        Assert.Equal("blueprint.action.parameter.key.duplicate", Assert.Single(result.Issues).Code);
    }

    [Fact]
    public void ManifestSnapshotsAndNormalizesM4PackageCollections()
    {
        var features = new List<BlueprintFeatureDefinition?>
        {
            new(" diagnostics ", true),
        };
        var actionParameters = ImmutableDictionary.CreateRange(
            StringComparer.Ordinal,
            new Dictionary<string, BlueprintValue>
            {
                [" path "] = BlueprintValue.FromString("src").Value,
            });
        var actions = new List<BlueprintActionDefinition?>
        {
            new(" create-source ", " create-directory ", actionParameters, TimeSpan.FromMinutes(1)),
        };
        var dependencies = new List<BlueprintDependency?>
        {
            new("microsoft.extensions.hosting", "10.0.0"),
        };
        var artifacts = new List<BlueprintArtifact?>
        {
            new(" src/App.csproj "),
        };

        var result = BlueprintManifest.Create(
            ValidDraft() with
            {
                Name = " Desktop Tool ",
                Features = features,
                Actions = actions,
                Dependencies = dependencies,
                Artifacts = artifacts,
                CompatibilityRules =
                [
                    new CompatibilityRule(
                        " windows-only ",
                        "runtime.os == 'windows'",
                        CompatibilityRuleSeverity.Blocking,
                        " Windows is required. ",
                        " Install Windows. ",
                        CompatibilityRuleOverride.None),
                ],
            },
            new BlueprintTrustAssignment(BlueprintTrust.TrustedLocal));

        Assert.True(result.IsValid);
        features.Clear();
        actions.Clear();
        dependencies.Clear();
        artifacts.Clear();

        Assert.Equal("Desktop Tool", result.Value.Name);
        Assert.Equal(new BlueprintFeatureDefinition("diagnostics", true), Assert.Single(result.Value.Features));
        Assert.Equal("create-source", Assert.Single(result.Value.Actions).Id);
        Assert.Equal("src", Assert.Single(Assert.Single(result.Value.Actions).Parameters).Value.StringValue);
        Assert.Equal("path", Assert.Single(Assert.Single(result.Value.Actions).Parameters).Key);
        Assert.Equal("microsoft.extensions.hosting", Assert.Single(result.Value.Dependencies).Id);
        Assert.Equal("src/App.csproj", Assert.Single(result.Value.Artifacts).Path);
        var rule = Assert.Single(result.Value.CompatibilityRules);
        Assert.Equal("windows-only", rule.Id);
        Assert.Equal(CompatibilityRuleSeverity.Blocking, rule.Severity);
        Assert.Equal(CompatibilityRuleOverride.None, rule.Override);
        Assert.Equal(BlueprintTrust.TrustedLocal, result.Value.Trust);
    }

    [Fact]
    public void ManifestRejectsDuplicateM4IdentifiersAndUndefinedRulePolicy()
    {
        var result = BlueprintManifest.Create(
            ValidDraft() with
            {
                Features =
                [
                    new BlueprintFeatureDefinition("diagnostics", false),
                    new BlueprintFeatureDefinition(" diagnostics ", true),
                ],
                Actions =
                [
                    new BlueprintActionDefinition("render", "render-template", [], TimeSpan.FromMinutes(1)),
                    new BlueprintActionDefinition(" render ", "copy-overlay", [], TimeSpan.FromMinutes(1)),
                ],
                CompatibilityRules =
                [
                    new CompatibilityRule(
                        "rule",
                        "runtime.os == 'windows'",
                        default,
                        "Windows is required.",
                        null,
                        default),
                ],
            },
            new BlueprintTrustAssignment(BlueprintTrust.BuiltIn));

        Assert.Equal(
            [
                "blueprint.feature.id.duplicate",
                "blueprint.rule.severity.invalid",
                "blueprint.rule.override.invalid",
                "blueprint.action.id.duplicate",
            ],
            result.Issues.Select(issue => issue.Code));
    }

    [Fact]
    public void ManifestRejectsDuplicatesAcrossAllNamedM4Collections()
    {
        var result = BlueprintManifest.Create(
            ValidDraft() with
            {
                Tools =
                [
                    new ToolRequirement("dotnet", ">=10.0.0"),
                    new ToolRequirement(" dotnet ", ">=10.0.0"),
                ],
                Validators =
                [
                    new ValidatorDefinition("build", "validate-command", TimeSpan.FromMinutes(1)),
                    new ValidatorDefinition(" build ", "validate-command", TimeSpan.FromMinutes(1)),
                ],
                Dependencies =
                [
                    new BlueprintDependency("example.package", "1.0.0"),
                    new BlueprintDependency(" example.package ", "1.0.0"),
                ],
                Artifacts =
                [
                    new BlueprintArtifact("src/App.csproj"),
                    new BlueprintArtifact(" src/App.csproj "),
                ],
            },
            new BlueprintTrustAssignment(BlueprintTrust.BuiltIn));

        Assert.Equal(
            [
                "blueprint.tool.id.duplicate",
                "blueprint.validator.id.duplicate",
                "blueprint.dependency.id.duplicate",
                "blueprint.artifact.path.duplicate",
            ],
            result.Issues.Select(issue => issue.Code));
    }

    [Fact]
    public void ValidatorsCarryTypedParametersAndRequiredPolicy()
    {
        var parameters = ImmutableDictionary<string, BlueprintValue>.Empty
            .Add("executable", BlueprintValue.FromString("dotnet").Value);
        var result = BlueprintManifest.Create(
            ValidDraft() with
            {
                Validators =
                [
                    new ValidatorDefinition(
                        "build",
                        "validate-command",
                        TimeSpan.FromMinutes(5),
                        parameters,
                        false),
                ],
            },
            new BlueprintTrustAssignment(BlueprintTrust.BuiltIn));

        Assert.True(result.IsValid);
        var validator = Assert.Single(result.Value.Validators);
        Assert.False(validator.Required);
        Assert.Equal("dotnet", Assert.Single(validator.Parameters).Value.StringValue);
    }

    private static BlueprintManifestDraft ValidDraft()
    {
        return new BlueprintManifestDraft(
            "desktop.csharp-wpf-tool",
            "1.0.0",
            ">=1.0.0 <2.0.0",
            [new ToolRequirement("dotnet", ">=10.0.0 <11.0.0")],
            [new InputDefinition("framework", BlueprintInputKind.Text, true, "net10.0")],
            [
                new CompatibilityRule(
                    "windows-only",
                    "runtime.os == 'windows'",
                    CompatibilityRuleSeverity.Blocking,
                    "Windows is required."),
            ],
            [new BlueprintStepDefinition("render", "render-template", TimeSpan.FromMinutes(2))],
            [new ValidatorDefinition("build", "validate-command", TimeSpan.FromMinutes(5))]);
    }
}
