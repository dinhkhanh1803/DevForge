using DevForge.Domain.Execution;

namespace DevForge.UnitTests.Domain;

public sealed class PlanValueTests
{
    [Fact]
    public void ScalarFactoriesPreserveExactTypedValues()
    {
        var text = PlanValue.FromString("  Unicode tiếng Việt 🚀  ");
        var boolean = PlanValue.FromBoolean(true);
        var integer = PlanValue.FromInteger(long.MinValue);

        Assert.True(text.IsValid);
        Assert.Equal(PlanValueKind.Text, text.Value.Kind);
        Assert.Equal("  Unicode tiếng Việt 🚀  ", text.Value.StringValue);
        Assert.Equal(PlanValueKind.Boolean, boolean.Kind);
        Assert.True(boolean.BooleanValue);
        Assert.Equal(PlanValueKind.WholeNumber, integer.Kind);
        Assert.Equal(long.MinValue, integer.IntegerValue);
    }

    [Fact]
    public void FromArraySnapshotsCallerSequenceExactlyOnce()
    {
        var source = new SingleUseEnumerable<PlanValue?>(
        [
            PlanValue.FromString("first").Value,
            PlanValue.FromInteger(2),
        ]);

        var result = PlanValue.FromArray(source);

        Assert.True(result.IsValid);
        Assert.Equal(1, source.EnumerationCount);
        Assert.Equal(2, result.Value.ArrayValue.Length);
        Assert.Equal("first", result.Value.ArrayValue[0].StringValue);
    }

    [Fact]
    public void FromObjectNormalizesKeysAndSnapshotsValues()
    {
        var source = new List<KeyValuePair<string, PlanValue?>>
        {
            KeyValuePair.Create<string, PlanValue?>(" path ", PlanValue.FromString("src").Value),
            KeyValuePair.Create<string, PlanValue?>("enabled", PlanValue.FromBoolean(true)),
        };

        var result = PlanValue.FromObject(source);
        source.Clear();

        Assert.True(result.IsValid);
        Assert.Equal(2, result.Value.ObjectValue.Count);
        Assert.Equal("src", result.Value.ObjectValue["path"].StringValue);
        Assert.True(result.Value.ObjectValue["enabled"].BooleanValue);
    }

    [Fact]
    public void FactoriesAggregateNullDuplicatePrivacyAndBoundsIssues()
    {
        var oversizedArray = Enumerable.Repeat<PlanValue?>(
            PlanValue.FromBoolean(false),
            PlanValue.MaximumCollectionItems + 1);
        var array = PlanValue.FromArray(oversizedArray.Append(null));
        var objectValue = PlanValue.FromObject(
        [
            KeyValuePair.Create<string, PlanValue?>(" path ", PlanValue.FromString("a").Value),
            KeyValuePair.Create<string, PlanValue?>("path", PlanValue.FromString("b").Value),
            KeyValuePair.Create<string, PlanValue?>("apiToken", PlanValue.FromString("safe").Value),
            KeyValuePair.Create<string, PlanValue?>("missing", null),
        ]);
        var credential = PlanValue.FromString("Authorization: Bearer abcdefghijklmnop");

        Assert.False(array.IsValid);
        Assert.Contains(array.Issues, issue => issue.Code == "plan.value.collection.too-large");
        Assert.Contains(array.Issues, issue => issue.Code == "plan.value.item.required");
        Assert.False(objectValue.IsValid);
        Assert.Contains(objectValue.Issues, issue => issue.Code == "plan.value.key.duplicate");
        Assert.Contains(objectValue.Issues, issue => issue.Code == "plan.value.key.secret-shaped");
        Assert.Contains(objectValue.Issues, issue => issue.Code == "plan.value.item.required");
        Assert.False(credential.IsValid);
        Assert.Contains(credential.Issues, issue => issue.Code == "plan.value.string.secret-shaped");
    }

    [Fact]
    public void FromArrayRejectsNestedValueBeyondMaximumDepth()
    {
        var value = PlanValue.FromBoolean(true);
        for (var depth = 0; depth < PlanValue.MaximumDepth; depth++)
        {
            value = PlanValue.FromArray([value]).Value;
        }

        var result = PlanValue.FromArray([value]);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "plan.value.depth.exceeded");
    }

    [Fact]
    public void ExecutionStepSnapshotsTypedInputs()
    {
        var inputs = new List<KeyValuePair<string, PlanValue?>>
        {
            KeyValuePair.Create<string, PlanValue?>(
                "arguments",
                PlanValue.FromArray([PlanValue.FromString("--flag").Value]).Value),
        };

        var result = ExecutionStep.Create(
            "step",
            "Step",
            "run-process",
            inputs,
            TimeSpan.FromMinutes(1),
            RetryPolicy.None);
        inputs.Clear();

        Assert.True(result.IsValid);
        Assert.Single(result.Value.Inputs);
        Assert.Equal("--flag", result.Value.Inputs["arguments"].ArrayValue[0].StringValue);
    }

    private sealed class SingleUseEnumerable<T>(IEnumerable<T> values) : IEnumerable<T>
    {
        private readonly IEnumerable<T> _values = values;

        public int EnumerationCount { get; private set; }

        public IEnumerator<T> GetEnumerator()
        {
            EnumerationCount++;
            if (EnumerationCount > 1)
            {
                throw new InvalidOperationException("The sequence was enumerated more than once.");
            }

            return _values.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
