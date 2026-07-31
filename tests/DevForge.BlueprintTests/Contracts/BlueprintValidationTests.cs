using DevForge.Blueprints.Abstractions.Validation;

namespace DevForge.BlueprintTests.Contracts;

public sealed class BlueprintValidationTests
{
    [Theory]
    [InlineData("", "Message", null)]
    [InlineData("code", "", null)]
    [InlineData("code", "Message", " ")]
    public void ValidationIssueRejectsMalformedProgrammingInput(
        string code,
        string message,
        string? location)
    {
        Assert.Throws<ArgumentException>(() => BlueprintValidationIssue.Create(code, message, location));
    }

    [Fact]
    public void ValidationIssueHasValueEqualityAfterNormalization()
    {
        var first = BlueprintValidationIssue.Create(" code ", " Message ", " field ");
        var same = BlueprintValidationIssue.Create("code", "Message", "field");
        var different = BlueprintValidationIssue.Create("other", "Message", "field");

        Assert.Equal(first, same);
        Assert.NotEqual(first, different);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
    }

    [Fact]
    public void ValidationResultRejectsNullIssuesAndEnumeratesOnce()
    {
        IEnumerable<BlueprintValidationIssue?> nullIssues = [null];
        var issues = new SingleEnumerationEnumerable<BlueprintValidationIssue?>(
            [BlueprintValidationIssue.Create("code", "Message")]);

        Assert.Throws<ArgumentException>(() => BlueprintValidationResult.Failure<string>(nullIssues));

        var result = BlueprintValidationResult.Failure<string>(issues);
        Assert.False(result.IsValid);
        Assert.Equal(1, issues.EnumerationCount);
    }

    private sealed class SingleEnumerationEnumerable<T>(IEnumerable<T> values) : IEnumerable<T>
    {
        public int EnumerationCount { get; private set; }

        public IEnumerator<T> GetEnumerator()
        {
            EnumerationCount++;
            if (EnumerationCount > 1)
            {
                throw new InvalidOperationException("Enumerable was enumerated more than once.");
            }

            return values.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
