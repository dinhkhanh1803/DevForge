using DevForge.Blueprints.Abstractions.Models;

namespace DevForge.BlueprintTests.Contracts;

public sealed class SemanticVersionTests
{
    private static readonly string[] _prereleasePrecedence =
    [
        "1.0.0-alpha",
        "1.0.0-alpha.1",
        "1.0.0-alpha.beta",
        "1.0.0-beta",
        "1.0.0-beta.2",
        "1.0.0-beta.11",
        "1.0.0-rc.1",
        "1.0.0",
    ];

    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData(" 1.2.3-alpha.1+build.7 ", "1.2.3-alpha.1+build.7")]
    public void TryParseNormalizesSupportedSemanticVersions(string source, string expected)
    {
        Assert.True(SemanticVersion.TryParse(source, out var version));
        Assert.Equal(expected, version.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1.2")]
    [InlineData("01.2.3")]
    [InlineData("1.02.3")]
    [InlineData("1.2.03")]
    [InlineData("1.2.3-01")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3+")]
    [InlineData("v1.2.3")]
    public void TryParseRejectsMalformedOrNonCanonicalVersions(string? source)
    {
        Assert.False(SemanticVersion.TryParse(source, out var version));
        Assert.Null(version);
    }

    [Fact]
    public void CompareToUsesNumericCoreOrdering()
    {
        var two = Parse("2.0.0");
        var ten = Parse("10.0.0");

        Assert.True(two.CompareTo(ten) < 0);
        Assert.True(ten.CompareTo(two) > 0);
    }

    [Fact]
    public void CompareToImplementsSemanticVersionPrereleasePrecedence()
    {
        var ordered = _prereleasePrecedence.Select(Parse).ToArray();

        for (var index = 1; index < ordered.Length; index++)
        {
            Assert.True(ordered[index - 1].CompareTo(ordered[index]) < 0);
        }
    }

    [Fact]
    public void CompareToIgnoresBuildMetadataForPrecedence()
    {
        var first = Parse("1.2.3+build.1");
        var second = Parse("1.2.3+build.99");

        Assert.Equal(0, first.CompareTo(second));
        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData("1.2.3", "1.2.3", true)]
    [InlineData("1.2.3", "1.2.4", false)]
    [InlineData(">=1.0.0 <2.0.0", "1.9.9", true)]
    [InlineData(">=1.0.0 <2.0.0", "2.0.0", false)]
    [InlineData("<1.0.0 || >=2.0.0 <3.0.0", "2.5.0", true)]
    [InlineData("<1.0.0 || >=2.0.0 <3.0.0", "1.5.0", false)]
    [InlineData(">=1.0.0-alpha <1.0.0", "1.0.0-rc.1", true)]
    public void RangeContainsEvaluatesExactAndComparatorGroups(
        string rangeExpression,
        string versionExpression,
        bool expected)
    {
        Assert.True(SemanticVersionRange.TryParse(rangeExpression, out var range));

        Assert.Equal(expected, range.Contains(Parse(versionExpression)));
    }

    private static SemanticVersion Parse(string value)
    {
        Assert.True(SemanticVersion.TryParse(value, out var version));
        return version;
    }
}
