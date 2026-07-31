using DevForge.Domain.Projects;

namespace DevForge.UnitTests.Domain;

public sealed class ProjectRecipeTests
{
    [Fact]
    public void CreateAggregatesInvalidIdentityAndTargetIssues()
    {
        var result = ProjectRecipe.Create(
            new ProjectRecipeDraft(
                " ",
                "relative",
                " ",
                " ",
                new Dictionary<string, string?>(),
                []));

        Assert.False(result.IsValid);
        Assert.Equal(
            ["project.name.required", "project.target.absolute", "blueprint.id.required", "blueprint.version.required"],
            result.Issues.Select(issue => issue.Code));
    }

    [Fact]
    public void CreateRejectsSecretShapedInputNames()
    {
        var inputs = new Dictionary<string, string?>
        {
            ["api_token"] = "redacted",
            ["databasePassword"] = "redacted",
            ["private-key"] = "redacted",
        };

        var result = ProjectRecipe.Create(ValidDraft(inputs));

        Assert.False(result.IsValid);
        Assert.All(result.Issues, issue => Assert.Equal("project.input.secret-name", issue.Code));
        Assert.Equal(["inputs.api_token", "inputs.databasePassword", "inputs.private-key"], result.Issues.Select(issue => issue.Location));
    }

    [Fact]
    public void CreateAggregatesNullBoundaryCollectionsWithoutThrowing()
    {
        var result = ProjectRecipe.Create(
            new ProjectRecipeDraft(null, null, null, null, null, null));

        Assert.False(result.IsValid);
        Assert.Equal(
            [
                "project.name.required",
                "project.target.absolute",
                "blueprint.id.required",
                "blueprint.version.required",
                "project.inputs.required",
                "project.features.required",
            ],
            result.Issues.Select(issue => issue.Code));
    }

    [Fact]
    public void CreateAggregatesMalformedInputAndFeatureEntries()
    {
        var inputs = new Dictionary<string, string?>
        {
            [""] = "value",
            ["framework"] = null,
            ["api_token"] = "redacted",
        };
        var features = new string?[] { "", null };

        var result = ProjectRecipe.Create(ValidDraft(inputs) with { Features = features });

        Assert.False(result.IsValid);
        Assert.Equal(
            [
                "project.input.name.required",
                "project.input.value.required",
                "project.input.secret-name",
                "project.feature.invalid",
                "project.feature.invalid",
            ],
            result.Issues.Select(issue => issue.Code));
        Assert.Equal(
            ["inputs", "inputs.framework", "inputs.api_token", "features[0]", "features[1]"],
            result.Issues.Select(issue => issue.Location));
    }

    [Fact]
    public void CreateTrimsIdentityAndSnapshotsInputsAndFeatures()
    {
        var inputs = new Dictionary<string, string?> { ["framework"] = "net10.0" };
        var features = new List<string?> { "tests" };
        var result = ProjectRecipe.Create(
            ValidDraft(inputs) with
            {
                Name = "  Sample  ",
                BlueprintId = "  desktop.csharp-wpf-tool  ",
                BlueprintVersion = "  1.0.0  ",
                Features = features,
            });

        Assert.True(result.IsValid);
        inputs["framework"] = "changed";
        features[0] = "changed";

        Assert.Equal("Sample", result.Value.Name);
        Assert.Equal("desktop.csharp-wpf-tool", result.Value.BlueprintId);
        Assert.Equal("1.0.0", result.Value.BlueprintVersion);

        Assert.Equal("net10.0", result.Value.Inputs["framework"]);
        Assert.Equal(["tests"], result.Value.Features.ToArray());
    }
    [Fact]
    public void CreateEnumeratesBoundaryCollectionsExactlyOnce()
    {
        var inputs = new SingleEnumerationDictionary(
            new Dictionary<string, string?> { ["framework"] = "net10.0" });
        var features = new SingleEnumerationCollection<string?>(["tests"]);

        var result = ProjectRecipe.Create(ValidDraft(inputs) with { Features = features });

        Assert.True(result.IsValid);
        Assert.Equal(1, inputs.EnumerationCount);
        Assert.Equal(1, features.EnumerationCount);
        Assert.Equal("net10.0", result.Value.Inputs["framework"]);
    }

    [Fact]
    public void FailedValidationResultDoesNotExposeAValue()
    {
        var result = ProjectRecipe.Create(ValidDraft() with { Name = "" });

        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    private static ProjectRecipeDraft ValidDraft(IReadOnlyDictionary<string, string?>? inputs = null)
    {
        return new ProjectRecipeDraft(
            "Sample",
            Path.GetFullPath("generated-project"),
            "desktop.csharp-wpf-tool",
            "1.0.0",
            inputs ?? new Dictionary<string, string?>(),
            ["tests"],
            TeamProfile.Create("team.standard", "Team Standard", new Dictionary<string, string?>()).Value,
            GitOptions.Create().Value,
            CompletionOptions.Create().Value);
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

    private sealed class SingleEnumerationDictionary(IReadOnlyDictionary<string, string?> values)
        : IReadOnlyDictionary<string, string?>
    {
        public string? this[string key] => values[key];

        public IEnumerable<string> Keys => values.Keys;

        public IEnumerable<string?> Values => values.Values;

        public int Count => values.Count;

        public int EnumerationCount { get; private set; }

        public bool ContainsKey(string key)
        {
            return values.ContainsKey(key);
        }

        public IEnumerator<KeyValuePair<string, string?>> GetEnumerator()
        {
            EnumerationCount++;
            if (EnumerationCount > 1)
            {
                throw new InvalidOperationException("Dictionary was enumerated more than once.");
            }

            return values.GetEnumerator();
        }

        public bool TryGetValue(string key, out string? value)
        {
            return values.TryGetValue(key, out value);
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
