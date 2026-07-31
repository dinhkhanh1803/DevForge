using DevForge.Domain.Validation;

namespace DevForge.UnitTests.Domain;

public sealed class ValidationPrimitiveTests
{
    [Theory]
    [InlineData("", "Message", null)]
    [InlineData("code", "", null)]
    [InlineData("code", "Message", " ")]
    public void ValidationIssueRejectsMalformedProgrammingInput(string code, string message, string? location)
    {
        Assert.Throws<ArgumentException>(() => ValidationIssue.Create(code, message, location));
    }

    [Fact]
    public void ValidationResultRejectsNullIssues()
    {
        IEnumerable<ValidationIssue?> issues = [null];

        Assert.Throws<ArgumentException>(() => ValidationResult.Failure<string>(issues));
    }

    [Fact]
    public void ValidationIssueStoresTrimmedStableFields()
    {
        var issue = ValidationIssue.Create(" code ", " Message ", " field ");

        Assert.Equal("code", issue.Code);
        Assert.Equal("Message", issue.Message);
        Assert.Equal("field", issue.Location);
    }
}
