using System.Collections.Immutable;
using DevForge.Application.Planning;
using DevForge.Domain.Execution;

namespace DevForge.UnitTests.Application.Planning;

public sealed class VariableTemplateResolverTests
{
    [Fact]
    public void ExactReferencePreservesTypedValueAndInlineReferencesProduceText()
    {
        var context = Context(
            ("recipe.input.enabled", PlanningVariableValue.FromValue(PlanValue.FromBoolean(true)).Value),
            ("project.name", Text("Sample")),
            ("blueprint.id", Text("sample.blueprint")));
        var exact = BlueprintText("{{ recipe.input.enabled }}");
        var inline = BlueprintText("{{ project.name }}-{{ blueprint.id }}");

        var exactResult = new VariableTemplateResolver().Resolve(exact, context);
        var inlineResult = new VariableTemplateResolver().Resolve(inline, context);

        Assert.Equal(PlanValueKind.Boolean, exactResult.Value.Kind);
        Assert.True(exactResult.Value.BooleanValue);
        Assert.Equal("Sample-sample.blueprint", inlineResult.Value.StringValue);
    }

    [Fact]
    public void ResolutionIsSinglePassAndDoesNotExpandReplacementContent()
    {
        var context = Context(
            ("project.name", Text("{{ blueprint.id }}")),
            ("blueprint.id", Text("sample.blueprint")));

        var result = new VariableTemplateResolver().Resolve(
            BlueprintText("{{ project.name }}"),
            context);

        Assert.True(result.IsValid);
        Assert.Equal("{{ blueprint.id }}", result.Value.StringValue);
    }

    [Fact]
    public void RuntimeValuesRemainTypedPlaceholdersAndCannotBeEmbeddedInText()
    {
        var context = Context(
            ("runtime.staging-path", PlanningVariableValue.Placeholder("runtime.staging-path").Value));

        var exact = new VariableTemplateResolver().Resolve(
            BlueprintText("{{ runtime.staging-path }}"),
            context);
        var embedded = new VariableTemplateResolver().Resolve(
            BlueprintText("prefix/{{ runtime.staging-path }}"),
            context);

        Assert.True(exact.IsValid);
        Assert.Equal(PlanValueKind.Map, exact.Value.Kind);
        Assert.Equal(
            "runtime.staging-path",
            exact.Value.ObjectValue["placeholder"].StringValue);
        Assert.False(embedded.IsValid);
    }

    [Theory]
    [InlineData("{{ unknown.value }}")]
    [InlineData("{{ project.name | upper }}")]
    [InlineData("{{ function(project.name) }}")]
    [InlineData("{{ project.password }}")]
    [InlineData("{{ project.name }")]
    [InlineData("project.name }}")]
    [InlineData("{{ {{ project.name }} }}")]
    public void ResolveRejectsUnknownSecretShapedMalformedFunctionOrRecursiveReferences(string template)
    {
        var result = new VariableTemplateResolver().Resolve(
            BlueprintText(template),
            Context(("project.name", Text("Sample"))));

        Assert.False(result.IsValid);
        Assert.Equal("DF-PLAN-001", Assert.Single(result.Issues).Code);
    }

    [Fact]
    public void ResolveRecursesAcrossImmutableArraysAndMapsWithoutMutatingKeys()
    {
        var source = DevForge.Blueprints.Abstractions.Models.BlueprintValue.FromObject(
        [
            KeyValuePair.Create<string, DevForge.Blueprints.Abstractions.Models.BlueprintValue?>(
                "name",
                BlueprintText("{{ project.name }}")),
            KeyValuePair.Create<string, DevForge.Blueprints.Abstractions.Models.BlueprintValue?>(
                "arguments",
                DevForge.Blueprints.Abstractions.Models.BlueprintValue.FromArray(
                [
                    BlueprintText("--name"),
                    BlueprintText("{{ project.name }}"),
                ]).Value),
        ]).Value;

        var result = new VariableTemplateResolver().Resolve(
            source,
            Context(("project.name", Text("Sample"))));

        Assert.True(result.IsValid);
        Assert.Equal("Sample", result.Value.ObjectValue["name"].StringValue);
        Assert.Equal("Sample", result.Value.ObjectValue["arguments"].ArrayValue[1].StringValue);
    }

    [Fact]
    public void VariableContextSnapshotsOnceAndRejectsDuplicatesAndInvalidPlaceholders()
    {
        var source = new SingleUseEnumerable<KeyValuePair<string, PlanningVariableValue?>>(
        [
            KeyValuePair.Create<string, PlanningVariableValue?>("project.name", Text("Sample")),
        ]);
        var valid = PlanningVariableContext.Create(source);
        var duplicate = PlanningVariableContext.Create(
        [
            KeyValuePair.Create<string, PlanningVariableValue?>("project.name", Text("A")),
            KeyValuePair.Create<string, PlanningVariableValue?>("project.name", Text("B")),
        ]);
        var invalidPlaceholder = PlanningVariableValue.Placeholder("project.name");

        Assert.True(valid.IsValid);
        Assert.Equal(1, source.EnumerationCount);
        Assert.False(duplicate.IsValid);
        Assert.False(invalidPlaceholder.IsValid);
    }

    [Fact]
    public void ResolveRejectsExcessiveTokenCountAndExpandedOutput()
    {
        var context = Context((
            "project.name",
            Text(new string('x', DevForge.Blueprints.Abstractions.Models.BlueprintValue.MaximumTextLength))));
        var tooManyTokens = string.Concat(Enumerable.Repeat(
            "{{project.name}}",
            VariableTemplateResolver.MaximumTokens + 1));

        var tokenResult = new VariableTemplateResolver().Resolve(
            BlueprintText(tooManyTokens),
            context);
        var outputResult = new VariableTemplateResolver().Resolve(
            BlueprintText("{{project.name}}{{project.name}}"),
            context);

        Assert.False(tokenResult.IsValid);
        Assert.False(outputResult.IsValid);
    }

    private static PlanningVariableContext Context(
        params (string Name, PlanningVariableValue Value)[] values)
    {
        return PlanningVariableContext.Create(values.Select(item =>
            KeyValuePair.Create<string, PlanningVariableValue?>(item.Name, item.Value))).Value;
    }

    private static PlanningVariableValue Text(string value)
    {
        return PlanningVariableValue.FromValue(PlanValue.FromString(value).Value).Value;
    }

    private static DevForge.Blueprints.Abstractions.Models.BlueprintValue BlueprintText(string value)
    {
        return DevForge.Blueprints.Abstractions.Models.BlueprintValue.FromString(value).Value;
    }

    private sealed class SingleUseEnumerable<T>(IEnumerable<T> values) : IEnumerable<T>
    {
        public int EnumerationCount { get; private set; }

        public IEnumerator<T> GetEnumerator()
        {
            EnumerationCount++;
            if (EnumerationCount > 1)
            {
                throw new InvalidOperationException("The source was enumerated more than once.");
            }

            return values.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
