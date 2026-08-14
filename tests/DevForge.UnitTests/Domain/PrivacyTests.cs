using System.Reflection;
using DevForge.Domain.Privacy;

namespace DevForge.UnitTests.Domain;

public sealed class PrivacyTests
{
    [Theory]
    [InlineData("using System; public sealed class GeneratedProject { }")]
    [InlineData("namespace Sample.Project;")]
    [InlineData("const tokenValue = GetValue();")]
    [InlineData("function generateProject() {")]
    [InlineData("<?xml version=\"1.0\"?>")]
    public void SourceShapedContentIsRejectedFromRedactedDisplayBoundary(string value)
    {
        Assert.True(RedactedText.IsSourceShapedContent(value));
        Assert.False(RedactedText.FromTrustedRedaction(value).IsValid);
    }

    [Theory]
    [InlineData("Source generation completed.")]
    [InlineData("The public API validation passed.")]
    [InlineData("The namespace setting was accepted.")]
    [InlineData("Foreign key: FK_ProjectRun")]
    public void SafeDiagnosticSentencesAreNotMisclassifiedAsSource(string value)
    {
        Assert.False(RedactedText.IsSourceShapedContent(value));
        Assert.True(RedactedText.FromTrustedRedaction(value).IsValid);
    }

    [Theory]
    [InlineData("public class library for internal tools")]
    [InlineData("namespace Sample is selected by the user")]
    public void SourceHeuristicDoesNotMakeOrdinaryProjectTextInvalid(string value)
    {
        Assert.True(DevForge.Application.Contracts.DynamicInputValue.Text(value).IsValid);
        var preset = DevForge.Application.Contracts.ProjectCreationPresetDraft.Create(
            DevForge.Application.Contracts.BlueprintReference.Create("sample.local", "1.0.0").Value,
            new Dictionary<string, DevForge.Application.Contracts.DynamicInputValue?>
            {
                ["description"] = DevForge.Application.Contracts.DynamicInputValue.Text(value).Value,
            },
            [],
            "none");
        Assert.True(preset.IsValid);
    }

    [Theory]
    [InlineData("token=abc123")]
    [InlineData("db_password : hunter2")]
    [InlineData("Server=db;User Id=app;Password=secret;")]
    [InlineData(".env contents: FEATURE_FLAG=true")]
    [InlineData("-----BEGIN PRIVATE KEY-----")]
    [InlineData("Authorization: Bearer abcdefghijklmnopqrstuvwxyz")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.signature123")]
    [InlineData("sk-proj-abcdefghijklmnopqrstuvwxyz123456")]
    [InlineData("AKIAIOSFODNN7EXAMPLE")]
    [InlineData("ghp_1234567890abcdef")]
    [InlineData("github_pat_1234567890abcdef")]
    [InlineData("{\"password\":\"fixture-json-secret\"}")]
    [InlineData("<ApiToken>fixture-xml-secret</ApiToken>")]
    public void TrustedRedactionBoundaryRejectsCredentialShapedContent(string value)
    {
        var result = RedactedText.FromTrustedRedaction(value);

        Assert.False(result.IsValid);
        Assert.Equal("privacy.value.secret-shaped", Assert.Single(result.Issues).Code);
    }

    [Theory]
    [InlineData("api_token = abc123")]
    [InlineData("openai-api-key: abc123")]
    [InlineData("connection_string = redacted")]
    [InlineData("aws_secret_access_key: abc123")]
    public void TrustedRedactionBoundaryRejectsAssignmentBypasses(string value)
    {
        Assert.False(RedactedText.FromTrustedRedaction(value).IsValid);
    }

    [Theory]
    [InlineData("  [REDACTED]  ", "[REDACTED]")]
    [InlineData("monkey=value", "monkey=value")]
    [InlineData("Foreign key: FK_ProjectRun", "Foreign key: FK_ProjectRun")]
    [InlineData("The .env file was not read", "The .env file was not read")]
    public void TrustedRedactionBoundaryAcceptsSafeContentWithoutIdentifierFalsePositives(
        string value,
        string expected)
    {
        var result = RedactedText.FromTrustedRedaction(value);

        Assert.True(result.IsValid);
        Assert.Equal(expected, result.Value.Value);
    }

    [Fact]
    public void RedactedTextHasValueEqualityAndNoImplicitRawStringConversion()
    {
        var first = RedactedText.FromTrustedRedaction("[REDACTED]").Value;
        var same = RedactedText.FromTrustedRedaction("[REDACTED]").Value;
        var different = RedactedText.FromTrustedRedaction("Safe diagnostic detail.").Value;

        Assert.Equal(first, same);
        Assert.NotEqual(first, different);
        Assert.DoesNotContain(
            typeof(RedactedText).GetMethods(BindingFlags.Public | BindingFlags.Static),
            method => method.Name is "op_Implicit" or "op_Explicit");
    }

    [Theory]
    [InlineData("api_token")]
    [InlineData("databasePassword")]
    [InlineData("connection-string")]
    public void SecretShapedKeysAreDetected(string key)
    {
        Assert.True(RedactedText.IsSecretShapedKey(key));
    }

    [Theory]
    [InlineData("Authorization: Bearer abcdefghijklmnop", true)]
    [InlineData("ghp_1234567890abcdef", true)]
    [InlineData("   ", false)]
    [InlineData("monkey=value", false)]
    [InlineData("Foreign key: FK_ProjectRun", false)]
    [InlineData("The .env file was not read", false)]
    public void SecretShapedValuesCanBeClassifiedWithoutCreatingRedactedText(
        string value,
        bool expected)
    {
        Assert.Equal(expected, RedactedText.IsSecretShapedValue(value));
    }
}
