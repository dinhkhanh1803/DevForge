using DevForge.Blueprints.Abstractions.Models;
using DevForge.Blueprints.Abstractions.Validation;

namespace DevForge.BlueprintTests.Contracts;

public sealed class BlueprintManifestTests
{
    [Fact]
    public void CreateRejectsANullDraftWithoutThrowing()
    {
        var result = BlueprintManifest.Create(null);

        var issue = Assert.Single(result.Issues);
        Assert.Equal("blueprint.manifest.required", issue.Code);
        Assert.Null(issue.Location);
    }

    [Fact]
    public void CreateNormalizesValuesAndSnapshotsEveryBoundaryCollection()
    {
        var tools = new List<ToolRequirement?>
        {
            new("  dotnet  ", "  >=10.0.0 <11.0.0  "),
        };
        var inputs = new List<InputDefinition?>
        {
            new("  framework  ", BlueprintInputKind.Text, true, "net10.0"),
        };
        var rules = new List<CompatibilityRule?>
        {
            new("  os == 'windows'  ", "  Windows is required.  "),
        };
        var steps = new List<BlueprintStepDefinition?>
        {
            new("  render-project  ", "  render-template  ", TimeSpan.FromMinutes(2)),
        };
        var validators = new List<ValidatorDefinition?>
        {
            new("  build  ", "  validate-command  ", TimeSpan.FromMinutes(5)),
        };

        var result = BlueprintManifest.Create(
            new BlueprintManifestDraft(
                "  desktop.csharp-wpf-tool  ",
                "  1.2.3-beta.1+build.7  ",
                "  >=1.0.0   <2.0.0  ",
                BlueprintTrustLevel.BuiltIn,
                tools,
                inputs,
                rules,
                steps,
                validators));

        Assert.True(result.IsValid);

        tools.Clear();
        inputs[0] = new InputDefinition("changed", BlueprintInputKind.Boolean, false);
        rules.Clear();
        steps.Clear();
        validators.Clear();

        var manifest = result.Value;
        Assert.Equal("desktop.csharp-wpf-tool", manifest.Id);
        Assert.Equal("1.2.3-beta.1+build.7", manifest.Version);
        Assert.Equal(">=1.0.0 <2.0.0", manifest.EngineVersionRange);
        Assert.Equal(BlueprintTrustLevel.BuiltIn, manifest.Trust);
        Assert.Equal(new ToolRequirement("dotnet", ">=10.0.0 <11.0.0"), Assert.Single(manifest.Tools));
        Assert.Equal(
            new InputDefinition("framework", BlueprintInputKind.Text, true, "net10.0"),
            Assert.Single(manifest.Inputs));
        Assert.Equal(
            new CompatibilityRule("os == 'windows'", "Windows is required."),
            Assert.Single(manifest.CompatibilityRules));
        Assert.Equal(
            new BlueprintStepDefinition("render-project", "render-template", TimeSpan.FromMinutes(2)),
            Assert.Single(manifest.Steps));
        Assert.Equal(
            new ValidatorDefinition("build", "validate-command", TimeSpan.FromMinutes(5)),
            Assert.Single(manifest.Validators));
    }

    [Fact]
    public void CreateAggregatesIdentityVersionEngineRangeAndTrustIssuesInStableOrder()
    {
        var result = BlueprintManifest.Create(
            ValidDraft() with
            {
                Id = "Invalid Id",
                Version = "01.2",
                EngineVersionRange = "latest",
                Trust = (BlueprintTrustLevel)42,
            });

        Assert.False(result.IsValid);
        Assert.Equal(
            [
                "blueprint.id.invalid",
                "blueprint.version.invalid",
                "blueprint.engine-range.invalid",
                "blueprint.trust.invalid",
            ],
            result.Issues.Select(issue => issue.Code));
        Assert.Equal(
            ["id", "version", "engineVersionRange", "trust"],
            result.Issues.Select(issue => issue.Location));
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("01.0.0")]
    [InlineData("1.0.0-")]
    [InlineData("1.0.0+")]
    [InlineData("v1.0.0")]
    public void CreateRejectsInvalidSemanticVersions(string version)
    {
        var result = BlueprintManifest.Create(ValidDraft() with { Version = version });

        var issue = Assert.Single(result.Issues);
        Assert.Equal("blueprint.version.invalid", issue.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData(">=1.0")]
    [InlineData("1.0.0 ||")]
    [InlineData(">=01.0.0")]
    [InlineData(">=1.0.0 nonsense")]
    public void CreateRejectsInvalidEngineRanges(string engineRange)
    {
        var result = BlueprintManifest.Create(ValidDraft() with { EngineVersionRange = engineRange });

        var issue = Assert.Single(result.Issues);
        Assert.Equal("blueprint.engine-range.invalid", issue.Code);
    }

    [Fact]
    public void CreateRejectsDuplicateInputAndStepIdentifiersAfterNormalization()
    {
        var result = BlueprintManifest.Create(
            ValidDraft() with
            {
                Inputs =
                [
                    new InputDefinition("framework", BlueprintInputKind.Text, true),
                    new InputDefinition(" framework ", BlueprintInputKind.Text, false),
                ],
                Steps =
                [
                    new BlueprintStepDefinition("render", "render-template", TimeSpan.FromMinutes(1)),
                    new BlueprintStepDefinition(" render ", "copy-overlay", TimeSpan.FromMinutes(1)),
                ],
            });

        Assert.False(result.IsValid);
        Assert.Equal(
            ["blueprint.input.id.duplicate", "blueprint.step.id.duplicate"],
            result.Issues.Select(issue => issue.Code));
        Assert.Equal(
            ["inputs[1].id", "steps[1].id"],
            result.Issues.Select(issue => issue.Location));
    }

    [Fact]
    public void CreateRejectsNonPositiveStepAndValidatorTimeouts()
    {
        var result = BlueprintManifest.Create(
            ValidDraft() with
            {
                Steps =
                [
                    new BlueprintStepDefinition("render", "render-template", TimeSpan.Zero),
                ],
                Validators =
                [
                    new ValidatorDefinition("build", "validate-command", TimeSpan.FromSeconds(-1)),
                ],
            });

        Assert.False(result.IsValid);
        Assert.Equal(
            ["blueprint.step.timeout.invalid", "blueprint.validator.timeout.invalid"],
            result.Issues.Select(issue => issue.Code));
        Assert.Equal(
            ["steps[0].timeout", "validators[0].timeout"],
            result.Issues.Select(issue => issue.Location));
    }

    [Fact]
    public void CreateRejectsUndefinedInputKinds()
    {
        var result = BlueprintManifest.Create(
            ValidDraft() with
            {
                Inputs =
                [
                    new InputDefinition("framework", (BlueprintInputKind)42, true),
                ],
            });

        var issue = Assert.Single(result.Issues);
        Assert.Equal("blueprint.input.kind.invalid", issue.Code);
        Assert.Equal("inputs[0].kind", issue.Location);
    }

    [Fact]
    public void CreateAggregatesMalformedNestedDefinitionsInStableOrder()
    {
        var result = BlueprintManifest.Create(
            ValidDraft() with
            {
                Tools = [new ToolRequirement(null!, "latest")],
                Inputs = [new InputDefinition("Bad Id", (BlueprintInputKind)42, true)],
                CompatibilityRules = [new CompatibilityRule(" ", " ")],
                Steps =
                [
                    new BlueprintStepDefinition("Bad Id", " ", TimeSpan.FromMinutes(1)),
                ],
                Validators =
                [
                    new ValidatorDefinition("Bad Id", " ", TimeSpan.FromMinutes(1)),
                ],
            });

        Assert.False(result.IsValid);
        Assert.Equal(
            [
                "blueprint.tool.id.invalid",
                "blueprint.tool.version-range.invalid",
                "blueprint.input.id.invalid",
                "blueprint.input.kind.invalid",
                "blueprint.compatibility-rule.expression.required",
                "blueprint.compatibility-rule.message.required",
                "blueprint.step.id.invalid",
                "blueprint.step.handler.required",
                "blueprint.validator.id.invalid",
                "blueprint.validator.handler.required",
            ],
            result.Issues.Select(issue => issue.Code));
    }

    [Fact]
    public void CreateRejectsSecretShapedInputNamesAndDefaultsWithoutEchoingValues()
    {
        const string sensitiveDefault = "password=do-not-echo";
        var result = BlueprintManifest.Create(
            ValidDraft() with
            {
                Inputs =
                [
                    new InputDefinition("api-token", BlueprintInputKind.Text, false),
                    new InputDefinition("database-url", BlueprintInputKind.Text, false, sensitiveDefault),
                ],
            });

        Assert.False(result.IsValid);
        Assert.Equal(
            ["blueprint.input.id.secret-shaped", "blueprint.input.default.secret-shaped"],
            result.Issues.Select(issue => issue.Code));
        Assert.DoesNotContain(
            result.Issues,
            issue => issue.Message.Contains(sensitiveDefault, StringComparison.Ordinal));
    }

    [Fact]
    public void CreateAggregatesNullCollectionsAndNullEntriesWithoutThrowing()
    {
        var nullCollections = BlueprintManifest.Create(
            ValidDraft() with
            {
                Tools = null,
                Inputs = null,
                CompatibilityRules = null,
                Steps = null,
                Validators = null,
            });

        Assert.Equal(
            [
                "blueprint.tools.required",
                "blueprint.inputs.required",
                "blueprint.compatibility-rules.required",
                "blueprint.steps.required",
                "blueprint.validators.required",
            ],
            nullCollections.Issues.Select(issue => issue.Code));

        var nullEntries = BlueprintManifest.Create(
            ValidDraft() with
            {
                Tools = [null],
                Inputs = [null],
                CompatibilityRules = [null],
                Steps = [null],
                Validators = [null],
            });

        Assert.Equal(
            [
                "blueprint.tool.required",
                "blueprint.input.required",
                "blueprint.compatibility-rule.required",
                "blueprint.step.required",
                "blueprint.validator.required",
            ],
            nullEntries.Issues.Select(issue => issue.Code));
    }

    [Fact]
    public void CreateEnumeratesEveryBoundaryCollectionExactlyOnce()
    {
        var tools = new SingleEnumerationCollection<ToolRequirement?>([new("dotnet", ">=10.0.0")]);
        var inputs = new SingleEnumerationCollection<InputDefinition?>(
            [new("framework", BlueprintInputKind.Text, true)]);
        var rules = new SingleEnumerationCollection<CompatibilityRule?>(
            [new("os == 'windows'", "Windows is required.")]);
        var steps = new SingleEnumerationCollection<BlueprintStepDefinition?>(
            [new("render", "render-template", TimeSpan.FromMinutes(1))]);
        var validators = new SingleEnumerationCollection<ValidatorDefinition?>(
            [new("build", "validate-command", TimeSpan.FromMinutes(5))]);

        var result = BlueprintManifest.Create(
            ValidDraft() with
            {
                Tools = tools,
                Inputs = inputs,
                CompatibilityRules = rules,
                Steps = steps,
                Validators = validators,
            });

        Assert.True(result.IsValid);
        Assert.All(
            [
                tools.EnumerationCount,
                inputs.EnumerationCount,
                rules.EnumerationCount,
                steps.EnumerationCount,
                validators.EnumerationCount,
            ],
            count => Assert.Equal(1, count));
    }

    [Fact]
    public void TrustLevelDefinesOnlyTheApprovedValues()
    {
        Assert.Equal(
            ["BuiltIn", "TrustedLocal", "Untrusted", "Quarantined"],
            Enum.GetNames<BlueprintTrustLevel>());
    }

    [Fact]
    public void FailedValidationResultDoesNotExposeAValue()
    {
        var result = BlueprintManifest.Create(ValidDraft() with { Id = null });

        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    private static BlueprintManifestDraft ValidDraft()
    {
        return new BlueprintManifestDraft(
            "desktop.csharp-wpf-tool",
            "1.0.0",
            ">=1.0.0 <2.0.0",
            BlueprintTrustLevel.BuiltIn,
            [new ToolRequirement("dotnet", ">=10.0.0 <11.0.0")],
            [new InputDefinition("framework", BlueprintInputKind.Text, true, "net10.0")],
            [new CompatibilityRule("os == 'windows'", "Windows is required.")],
            [new BlueprintStepDefinition("render", "render-template", TimeSpan.FromMinutes(2))],
            [new ValidatorDefinition("build", "validate-command", TimeSpan.FromMinutes(5))]);
    }

    private sealed class SingleEnumerationCollection<T>(IReadOnlyCollection<T> values) : IReadOnlyCollection<T>
    {
        public int Count => values.Count;

        public int EnumerationCount { get; private set; }

        public IEnumerator<T> GetEnumerator()
        {
            EnumerationCount++;
            if (EnumerationCount > 1)
            {
                throw new InvalidOperationException("Collection was enumerated more than once.");
            }

            return values.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
