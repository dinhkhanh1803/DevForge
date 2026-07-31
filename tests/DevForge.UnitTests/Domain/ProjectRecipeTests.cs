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
            new CompletionOptions());
    }
}
