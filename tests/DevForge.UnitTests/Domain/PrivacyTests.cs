using DevForge.Domain.Privacy;

namespace DevForge.UnitTests.Domain;

public sealed class PrivacyTests
{
    [Theory]
    [InlineData("token=abc123")]
    [InlineData("password=hunter2")]
    [InlineData("Server=db;User Id=app;Password=secret;")]
    [InlineData("copied from .env")]
    [InlineData("ghp_1234567890abcdef")]
    [InlineData("github_pat_1234567890abcdef")]
    public void CreateRejectsCredentialShapedContent(string value)
    {
        var result = SanitizedText.Create(value);

        Assert.False(result.IsValid);
        Assert.Equal("privacy.value.secret-shaped", Assert.Single(result.Issues).Code);
    }

    [Fact]
    public void CreateAcceptsAndTrimsSanitizedContent()
    {
        var result = SanitizedText.Create("  [REDACTED]  ");

        Assert.True(result.IsValid);
        Assert.Equal("[REDACTED]", result.Value.Value);
    }

    [Theory]
    [InlineData("api_token")]
    [InlineData("databasePassword")]
    [InlineData("connection-string")]
    public void SecretShapedKeysAreDetected(string key)
    {
        Assert.True(SanitizedText.IsSecretShapedKey(key));
    }
}
