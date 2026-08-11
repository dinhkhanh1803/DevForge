using System.Collections;
using DevForge.Application.Contracts;

namespace DevForge.UnitTests.Application;

public sealed class TemplateRenderRequestTests
{
    [Fact]
    public void CreateSnapshotsContextExactlyOnceAndPreservesValuesVerbatim()
    {
        var context = new SingleUseEnumerable<KeyValuePair<string, string?>>(
        [
            KeyValuePair.Create<string, string?>(" project.name ", "  DevForge  "),
            KeyValuePair.Create<string, string?>("blank", ""),
        ]);

        var result = TemplateRenderRequest.Create("{{ project.name }}", context);

        Assert.True(result.IsValid);
        Assert.Equal(1, context.EnumerationCount);
        Assert.Equal("  DevForge  ", result.Value.Context["project.name"]);
        Assert.Equal("", result.Value.Context["blank"]);
    }

    [Fact]
    public void CreateSnapshotsCallerCollection()
    {
        var context = new List<KeyValuePair<string, string?>>
        {
            KeyValuePair.Create<string, string?>("project.name", "Original"),
        };
        var result = TemplateRenderRequest.Create("{{ project.name }}", context);

        context[0] = KeyValuePair.Create<string, string?>("project.name", "Mutated");
        context.Add(KeyValuePair.Create<string, string?>("extra", "value"));

        Assert.True(result.IsValid);
        Assert.Equal("Original", result.Value.Context["project.name"]);
        Assert.Single(result.Value.Context);
    }

    [Fact]
    public void CreateAcceptsEveryExactBoundary()
    {
        var template = new string('t', TemplateRenderRequest.MaxTemplateLength);
        var longestName = $"a{new string('b', TemplateRenderRequest.MaxContextNameLength - 1)}";
        var context = Enumerable.Range(0, 31)
            .Select(index => KeyValuePair.Create<string, string?>(
                $"value{index}",
                new string('v', TemplateRenderRequest.MaxContextValueLength)))
            .Append(KeyValuePair.Create<string, string?>(
                longestName,
                new string('v', TemplateRenderRequest.MaxContextValueLength)))
            .Concat(Enumerable.Range(32, TemplateRenderRequest.MaxContextEntries - 32)
                .Select(index => KeyValuePair.Create<string, string?>($"value{index}", "")))
            .ToArray();

        var result = TemplateRenderRequest.Create(template, context);

        Assert.True(result.IsValid);
        Assert.Equal(TemplateRenderRequest.MaxContextEntries, result.Value.Context.Count);
        Assert.Equal(TemplateRenderRequest.MaxTotalContextValueLength, result.Value.Context.Values.Sum(value => value.Length));
    }

    [Fact]
    public void CreateAggregatesTemplateAndCollectionBounds()
    {
        var context = Enumerable.Range(0, TemplateRenderRequest.MaxContextEntries + 1)
            .Select(index => KeyValuePair.Create<string, string?>($"value{index}", ""));

        var result = TemplateRenderRequest.Create(
            new string('t', TemplateRenderRequest.MaxTemplateLength + 1) + "\0",
            context);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "template.value.too-large");
        Assert.Contains(result.Issues, issue => issue.Code == "template.value.null-character");
        Assert.Contains(result.Issues, issue => issue.Code == "template.context.too-many");
    }

    [Fact]
    public void CreateAggregatesMalformedNamesAndMissingValues()
    {
        var longestInvalidName = $"a{new string('b', TemplateRenderRequest.MaxContextNameLength)}";
        var context = new SingleUseEnumerable<KeyValuePair<string, string?>>(
        [
            KeyValuePair.Create<string, string?>(" project.name ", "Example"),
            KeyValuePair.Create<string, string?>("project", "collision"),
            KeyValuePair.Create<string, string?>("project.name", "duplicate"),
            KeyValuePair.Create<string, string?>("apiToken", "safe"),
            KeyValuePair.Create<string, string?>("bad-name", null),
            KeyValuePair.Create<string, string?>(longestInvalidName, "value"),
            KeyValuePair.Create<string, string?>("control\u0001name", "value"),
        ]);

        var result = TemplateRenderRequest.Create("{{ project.name }}", context);

        Assert.False(result.IsValid);
        Assert.Equal(1, context.EnumerationCount);
        Assert.Contains(result.Issues, issue => issue.Code == "template.context.name.path-conflict");
        Assert.Contains(result.Issues, issue => issue.Code == "template.context.name.duplicate");
        Assert.Contains(result.Issues, issue => issue.Code == "template.context.name.secret-shaped");
        Assert.Contains(result.Issues, issue => issue.Code == "template.context.name.invalid");
        Assert.Contains(result.Issues, issue => issue.Code == "template.context.name.too-long");
        Assert.Contains(result.Issues, issue => issue.Code == "template.context.value.required");
    }

    [Fact]
    public void CreateAggregatesValueAndTotalContextBoundsWithoutLeakingValues()
    {
        const string credential = "Authorization: Bearer abcdefghijklmnop";
        var context = Enumerable.Range(0, 33)
            .Select(index => KeyValuePair.Create<string, string?>(
                $"value{index}",
                new string('v', TemplateRenderRequest.MaxContextValueLength)))
            .Append(KeyValuePair.Create<string, string?>(
                "oversized",
                new string('x', TemplateRenderRequest.MaxContextValueLength + 1)))
            .Append(KeyValuePair.Create<string, string?>("nullCharacter", "value\0"))
            .Append(KeyValuePair.Create<string, string?>("credentialValue", credential));

        var result = TemplateRenderRequest.Create("{{ value0 }}", context);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "template.context.value.too-large");
        Assert.Contains(result.Issues, issue => issue.Code == "template.context.value.null-character");
        Assert.Contains(result.Issues, issue => issue.Code == "template.context.value.secret-shaped");
        Assert.Contains(result.Issues, issue => issue.Code == "template.context.total.too-large");
        Assert.All(
            result.Issues,
            issue => Assert.DoesNotContain(credential, $"{issue.Code}|{issue.Message}|{issue.Location}", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateAggregatesRequiredInputsWithoutThrowing()
    {
        var result = TemplateRenderRequest.Create(null, null);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "template.value.required");
        Assert.Contains(result.Issues, issue => issue.Code == "template.context.required");
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

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
