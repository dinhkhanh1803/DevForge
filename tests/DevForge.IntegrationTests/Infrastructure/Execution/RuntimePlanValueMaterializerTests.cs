using DevForge.Application.Contracts;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Execution;
using DevForge.Infrastructure.Execution;

namespace DevForge.IntegrationTests.Infrastructure.Execution;

public sealed class RuntimePlanValueMaterializerTests
{
    [Fact]
    public void MaterializesTypedRuntimePlaceholdersRecursivelyWithoutReparsing()
    {
        var context = RuntimePlanValueContext.Create(
            "run-123",
            WorkspaceRoot.Create(@"C:\Work\Project\.devforge-staging\run-123\payload").Value,
            null,
            RuntimeValueAvailability.PreFinalization,
            BlueprintTrust.TrustedLocal).Value;
        var nested = Map(("path", Placeholder("runtime.staging-path")), ("enabled", PlanValue.FromBoolean(true)));
        var source = new Dictionary<string, PlanValue?>
        {
            ["run"] = Placeholder("runtime.run-id"),
            ["nested"] = nested,
            ["literal"] = Text("{{ runtime.run-id }}"),
            ["count"] = PlanValue.FromInteger(3),
        };

        var result = RuntimePlanValueMaterializer.Materialize(source, context);

        Assert.True(result.IsValid);
        Assert.Equal("run-123", result.Value["run"].StringValue);
        Assert.Equal(
            @"C:\Work\Project\.devforge-staging\run-123\payload",
            result.Value["nested"].ObjectValue["path"].StringValue);
        Assert.True(result.Value["nested"].ObjectValue["enabled"].BooleanValue);
        Assert.Equal("{{ runtime.run-id }}", result.Value["literal"].StringValue);
        Assert.Equal(3, result.Value["count"].IntegerValue);
    }

    [Fact]
    public void TargetPathIsUnavailableBeforeFinalization()
    {
        var context = RuntimePlanValueContext.Create(
            "run-123",
            WorkspaceRoot.Create(@"C:\Work\Staging").Value,
            null,
            RuntimeValueAvailability.PreFinalization,
            BlueprintTrust.BuiltIn).Value;

        var result = RuntimePlanValueMaterializer.Materialize(
            Pair("target", Placeholder("project.target-path")),
            context);

        Assert.False(result.IsValid);
        Assert.Equal("DF-EXEC-001", Assert.Single(result.Issues).Code);
    }

    [Fact]
    public void TargetPathRequiresBuiltInPostFinalizationContext()
    {
        var rejected = RuntimePlanValueContext.Create(
            "run-123",
            WorkspaceRoot.Create(@"C:\Work\Staging").Value,
            WorkspaceRoot.Create(@"C:\Work\Target").Value,
            RuntimeValueAvailability.PostFinalizationBuiltIn,
            BlueprintTrust.TrustedLocal);
        var accepted = RuntimePlanValueContext.Create(
            "run-123",
            WorkspaceRoot.Create(@"C:\Work\Staging").Value,
            WorkspaceRoot.Create(@"C:\Work\Target").Value,
            RuntimeValueAvailability.PostFinalizationBuiltIn,
            BlueprintTrust.BuiltIn);

        Assert.False(rejected.IsValid);
        Assert.True(accepted.IsValid);
        var materialized = RuntimePlanValueMaterializer.Materialize(
            Pair("target", Placeholder("project.target-path")),
            accepted.Value);
        Assert.Equal(@"C:\Work\Target", materialized.Value["target"].StringValue);
    }

    [Theory]
    [InlineData("unknown.placeholder")]
    [InlineData("project.target-path")]
    public void RejectsUnavailableOrUnknownPlaceholder(string identifier)
    {
        var context = RuntimePlanValueContext.Create(
            "run-123",
            WorkspaceRoot.Create(@"C:\Work\Staging").Value,
            null,
            RuntimeValueAvailability.PreFinalization,
            BlueprintTrust.BuiltIn).Value;

        var result = RuntimePlanValueMaterializer.Materialize(
            Pair("value", Placeholder(identifier)),
            context);

        Assert.False(result.IsValid);
        Assert.Equal("DF-EXEC-001", Assert.Single(result.Issues).Code);
    }

    [Fact]
    public void RejectsMalformedPlaceholderMapsAndNullBoundaries()
    {
        var context = RuntimePlanValueContext.Create(
            "run-123",
            WorkspaceRoot.Create(@"C:\Work\Staging").Value,
            null,
            RuntimeValueAvailability.PreFinalization,
            BlueprintTrust.BuiltIn).Value;
        var malformed = Map(
            ("placeholder", Text("runtime.run-id")),
            ("extra", Text("must-not-be-accepted")));

        var malformedResult = RuntimePlanValueMaterializer.Materialize(Pair("value", malformed), context);
        var nullInputs = RuntimePlanValueMaterializer.Materialize(null, context);
        var nullContext = RuntimePlanValueMaterializer.Materialize(Pair("value", Text("safe")), null);
        var secretKey = RuntimePlanValueMaterializer.Materialize(
            Pair("apiToken", Text("not-a-credential")),
            context);

        Assert.False(malformedResult.IsValid);
        Assert.False(nullInputs.IsValid);
        Assert.False(nullContext.IsValid);
        Assert.False(secretKey.IsValid);
        Assert.All(
            new[] { malformedResult, nullInputs, nullContext, secretKey },
            item => Assert.All(item.Issues, issue => Assert.Equal("DF-EXEC-001", issue.Code)));
    }

    [Fact]
    public void RejectsDuplicateInputsAndAggregateNodeOverflow()
    {
        var context = RuntimePlanValueContext.Create(
            "run-123",
            WorkspaceRoot.Create(@"C:\Work\Staging").Value,
            null,
            RuntimeValueAvailability.PreFinalization,
            BlueprintTrust.BuiltIn).Value;
        var duplicate = new[]
        {
            KeyValuePair.Create<string, PlanValue?>("value", Text("one")),
            KeyValuePair.Create<string, PlanValue?>("value", Text("two")),
        };
        var fullArray = PlanValue.FromArray(Enumerable.Range(0, PlanValue.MaximumCollectionItems)
            .Select(value => PlanValue.FromInteger(value))).Value;
        var oversized = Map(
            ("one", fullArray),
            ("two", fullArray),
            ("three", fullArray),
            ("four", fullArray));

        var duplicateResult = RuntimePlanValueMaterializer.Materialize(duplicate, context);
        var oversizedResult = RuntimePlanValueMaterializer.Materialize(
            Pair("value", oversized),
            context);

        Assert.False(duplicateResult.IsValid);
        Assert.False(oversizedResult.IsValid);
    }

    [Fact]
    public void CancellationIsObservedDuringTraversal()
    {
        var context = RuntimePlanValueContext.Create(
            "run-123",
            WorkspaceRoot.Create(@"C:\Work\Staging").Value,
            null,
            RuntimeValueAvailability.PreFinalization,
            BlueprintTrust.BuiltIn).Value;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => RuntimePlanValueMaterializer.Materialize(
            Pair("value", Text("safe")),
            context,
            cancellation.Token));
    }

    private static IEnumerable<KeyValuePair<string, PlanValue?>> Pair(string key, PlanValue value) =>
        [KeyValuePair.Create<string, PlanValue?>(key, value)];

    private static PlanValue Placeholder(string identifier) =>
        Map(("placeholder", Text(identifier)));

    private static PlanValue Map(params (string Key, PlanValue Value)[] values) =>
        PlanValue.FromObject(values.Select(item =>
            KeyValuePair.Create<string, PlanValue?>(item.Key, item.Value))).Value;

    private static PlanValue Text(string value) => PlanValue.FromString(value).Value;
}
