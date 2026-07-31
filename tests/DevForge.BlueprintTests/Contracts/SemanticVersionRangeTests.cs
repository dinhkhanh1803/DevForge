using DevForge.Blueprints.Abstractions.Models;

namespace DevForge.BlueprintTests.Contracts;

public sealed class SemanticVersionRangeTests
{
    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData(" >=1.0.0   <2.0.0 ", ">=1.0.0 <2.0.0")]
    [InlineData(">1.0.0 <=2.0.0", ">1.0.0 <=2.0.0")]
    [InlineData("1.0.0 || 2.0.0", "1.0.0 || 2.0.0")]
    [InlineData(
        ">=1.0.0-alpha.1+build.7 <2.0.0 || =3.0.0",
        ">=1.0.0-alpha.1+build.7 <2.0.0 || =3.0.0")]
    public void TryParseAcceptsAndNormalizesTheSupportedGrammar(
        string expression,
        string normalized)
    {
        var parsed = SemanticVersionRange.TryParse(expression, out var range);

        Assert.True(parsed);
        Assert.NotNull(range);
        Assert.Equal(normalized, range.Expression);
        Assert.Equal(normalized, range.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("^1.0.0")]
    [InlineData("~1.0.0")]
    [InlineData("1.0.*")]
    [InlineData("1.0.0 - 2.0.0")]
    [InlineData("[1.0.0,2.0.0)")]
    [InlineData("|| 1.0.0")]
    [InlineData("1.0.0 ||")]
    [InlineData(">=1.0")]
    [InlineData("1.0.0 >=2.0.0")]
    [InlineData(">=1.0.0 2.0.0")]
    [InlineData("1.0.0 2.0.0")]
    public void TryParseRejectsUnsupportedOrMalformedGrammar(string? expression)
    {
        var parsed = SemanticVersionRange.TryParse(expression, out var range);

        Assert.False(parsed);
        Assert.Null(range);
    }

    [Fact]
    public void ParsedRangesHaveValueEqualityAfterNormalization()
    {
        Assert.True(SemanticVersionRange.TryParse(" >=1.0.0   <2.0.0 ", out var first));
        Assert.True(SemanticVersionRange.TryParse(">=1.0.0 <2.0.0", out var same));
        Assert.True(SemanticVersionRange.TryParse(">=2.0.0", out var different));

        Assert.Equal(first, same);
        Assert.NotEqual(first, different);
        Assert.Equal(first!.GetHashCode(), same!.GetHashCode());
    }

    [Theory]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData(" >=10.0.0   <11.0.0 || =12.0.0 ", ">=10.0.0 <11.0.0 || =12.0.0")]
    public void ManifestAndToolRequirementsUseTheSameRangeGrammar(
        string expression,
        string normalized)
    {
        var result = BlueprintManifest.Create(
            ValidDraft() with
            {
                EngineVersionRange = expression,
                Tools = [new ToolRequirement("dotnet", expression)],
            },
            new BlueprintTrustAssignment(BlueprintTrust.BuiltIn));

        Assert.True(result.IsValid);
        Assert.Equal(normalized, result.Value.EngineVersionRange);
        Assert.Equal(normalized, Assert.Single(result.Value.Tools).VersionRange);
    }

    private static BlueprintManifestDraft ValidDraft()
    {
        return new BlueprintManifestDraft(
            "desktop.csharp-wpf-tool",
            "1.0.0",
            ">=1.0.0 <2.0.0",
            [new ToolRequirement("dotnet", ">=10.0.0 <11.0.0")],
            [new InputDefinition("framework", BlueprintInputKind.Text, true, "net10.0")],
            [new CompatibilityRule("os == 'windows'", "Windows is required.")],
            [new BlueprintStepDefinition("render", "render-template", TimeSpan.FromMinutes(2))],
            [new ValidatorDefinition("build", "validate-command", TimeSpan.FromMinutes(5))]);
    }
}
