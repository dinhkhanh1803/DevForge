using DevForge.Application.Contracts;
using DevForge.Application.Contracts.Persistence;
using DevForge.Application.Creation;

namespace DevForge.UnitTests.Application.Creation;

public sealed class ProjectCreationPresetCodecTests
{
    [Fact]
    public void EncodeIsDeterministicAndRoundTripsEveryDynamicKind()
    {
        var preset = ProjectCreationPresetDraft.Create(
            BlueprintReference.Create("sample.local", "1.2.3").Value,
            new Dictionary<string, DynamicInputValue?>
            {
                ["zeta"] = DynamicInputValue.WholeNumber(42).Value,
                ["alpha"] = DynamicInputValue.Text("hello").Value,
                ["enabled"] = DynamicInputValue.Boolean(true).Value,
            },
            ["zeta-feature", "alpha-feature"],
            "vscode",
            initializeRepository: true,
            branchPolicy: DevForge.Domain.Projects.GitBranchPolicy.MainAndDevelop,
            publishToGitHub: true,
            isPrivate: false,
            githubAccount: "octocat",
            githubRepository: "sample-project").Value;
        var sut = new ProjectCreationPresetCodec();

        var first = sut.Encode(preset);
        var second = sut.Encode(preset);
        var decoded = sut.Decode(first.Value);

        Assert.True(first.IsValid);
        Assert.Equal(first.Value.Value, second.Value.Value);
        Assert.True(decoded.IsValid);
        Assert.Equal("hello", decoded.Value.Inputs["alpha"].TextValue);
        Assert.True(decoded.Value.Inputs["enabled"].BooleanValue);
        Assert.Equal(42, decoded.Value.Inputs["zeta"].WholeNumberValue);
        Assert.Equal(["alpha-feature", "zeta-feature"], decoded.Value.Features.ToArray());
        Assert.Equal("vscode", decoded.Value.IdeId);
        Assert.Equal(DevForge.Domain.Projects.GitBranchPolicy.MainAndDevelop, decoded.Value.Git.BranchPolicy);
        Assert.True(decoded.Value.Git.PublishToGitHub);
        Assert.False(decoded.Value.Git.IsPrivate);
        Assert.Equal("octocat", decoded.Value.Git.GitHubAccount);
        Assert.Equal("sample-project", decoded.Value.Git.GitHubRepository);
        Assert.Contains("\"schemaVersion\":2", first.Value.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyVersionOnePresetUpgradesToSafeGitDefaults()
    {
        var legacy = PersistableJson.Create(
            "{\"schemaVersion\":1,\"blueprint\":{\"id\":\"sample.local\",\"version\":\"1.0.0\"},\"inputs\":{},\"features\":[],\"ideId\":\"none\"}").Value;

        var result = new ProjectCreationPresetCodec().Decode(legacy);

        Assert.True(result.IsValid);
        Assert.True(result.Value.Git.InitializeRepository);
        Assert.Equal(DevForge.Domain.Projects.GitBranchPolicy.Main, result.Value.Git.BranchPolicy);
        Assert.False(result.Value.Git.PublishToGitHub);
        Assert.True(result.Value.Git.IsPrivate);
        Assert.Null(result.Value.Git.GitHubAccount);
        Assert.Null(result.Value.Git.GitHubRepository);
    }

    [Theory]
    [InlineData("{\"initializeRepository\":false,\"branchPolicy\":\"main\",\"publishToGitHub\":true,\"isPrivate\":true,\"githubAccount\":\"octocat\",\"githubRepository\":\"sample\"}", "git.publish.requires-initialization")]
    [InlineData("{\"initializeRepository\":true,\"branchPolicy\":\"unsupported\",\"publishToGitHub\":false,\"isPrivate\":true,\"githubAccount\":null,\"githubRepository\":null}", "creation.preset.git.branch-policy.invalid")]
    public void VersionTwoPresetRejectsInvalidGitCombinations(string git, string expectedCode)
    {
        var json = $$"""
            {"schemaVersion":2,"blueprint":{"id":"sample.local","version":"1.0.0"},"inputs":{},"features":[],"ideId":"none","git":{{git}}}
            """;

        var result = new ProjectCreationPresetCodec().Decode(PersistableJson.Create(json).Value);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == expectedCode);
    }

    [Fact]
    public void PersistenceBoundaryRejectsDuplicateGitProperties()
    {
        const string json = "{\"schemaVersion\":2,\"blueprint\":{\"id\":\"sample.local\",\"version\":\"1.0.0\"},\"inputs\":{},\"features\":[],\"ideId\":\"none\",\"git\":{\"initializeRepository\":true,\"branchPolicy\":\"main\",\"publishToGitHub\":false,\"publishToGitHub\":true,\"isPrivate\":true,\"githubAccount\":\"octocat\",\"githubRepository\":\"sample\"}}";

        var result = PersistableJson.Create(json);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "persistence.json.property.duplicate");
    }

    [Theory]
    [InlineData("{}", "creation.preset.schema-version.invalid")]
    [InlineData("{\"schemaVersion\":3}", "creation.preset.schema-version.invalid")]
    [InlineData("{\"schemaVersion\":1,\"unknown\":true}", "creation.preset.property.unknown")]
    public void DecodeRejectsUnsupportedSchemaOrFields(string json, string expectedCode)
    {
        var document = PersistableJson.Create(json);
        Assert.True(document.IsValid);

        var result = new ProjectCreationPresetCodec().Decode(document.Value);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == expectedCode);
    }

    [Theory]
    [InlineData("text", "true")]
    [InlineData("boolean", "\"true\"")]
    [InlineData("whole-number", "1.5")]
    [InlineData("unknown", "1")]
    public void DecodeRejectsMismatchedOrUnknownDynamicKinds(string kind, string value)
    {
        var json = $$"""
            {
              "schemaVersion": 1,
              "blueprint": { "id": "sample.local", "version": "1.0.0" },
              "inputs": { "sample": { "kind": "{{kind}}", "value": {{value}} } },
              "features": [],
              "ideId": "none"
            }
            """;
        var document = PersistableJson.Create(json);
        Assert.True(document.IsValid);

        var result = new ProjectCreationPresetCodec().Decode(document.Value);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "creation.preset.input.invalid");
    }

    [Theory]
    [InlineData("databasepassword")]
    [InlineData("clientsecret")]
    [InlineData("githubtoken")]
    [InlineData("apitoken")]
    public void PersistenceBoundaryRejectsSensitiveInputIdentifiers(string key)
    {
        var json = $"{{\"schemaVersion\":1,\"inputs\":{{\"{key}\":{{\"kind\":\"text\",\"value\":\"x\"}}}}}}";

        var document = PersistableJson.Create(json);

        Assert.False(document.IsValid);
        Assert.Contains(document.Issues, issue => issue.Code == "persistence.json.secret-detected");
    }

    [Theory]
    [InlineData("token=not-safe")]
    [InlineData("-----BEGIN PRIVATE KEY-----")]
    [InlineData("Bearer abcdefghijklmnop")]
    [InlineData("sk-proj-abcdefghijklmnop")]
    public void PersistenceBoundaryRejectsCredentialShapedValues(string value)
    {
        var json = $"{{\"schemaVersion\":1,\"value\":\"{value}\"}}";

        Assert.False(PersistableJson.Create(json).IsValid);
    }

    [Fact]
    public void NullBoundariesReturnStableFailures()
    {
        var sut = new ProjectCreationPresetCodec();

        Assert.Contains(
            sut.Encode(null).Issues,
            issue => issue.Code == "creation.preset.required");
        Assert.Contains(
            sut.Decode(null).Issues,
            issue => issue.Code == "creation.preset.document.required");
    }
}
