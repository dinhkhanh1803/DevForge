using System.Text;
using DevForge.Infrastructure.Blueprints;

namespace DevForge.IntegrationTests.Infrastructure.Blueprints;

public sealed class BlueprintControlReaderTests
{
    private const string ValidManifest = """
        id: desktop.csharp-wpf-tool
        name: Desktop Tool
        version: 1.0.0
        engineVersion: ">=1.0.0 <2.0.0"
        tools: []
        features: []
        actions: []
        validators: []
        artifacts: []
        dependencies: []
        """;

    private const string ValidRules = """
        - id: windows-only
          condition: runtime.os == 'windows'
          severity: blocking
          message: Windows is required.
          remediation: Install Windows.
          override: none
        """;

    private const string ValidSchema = """
        {
          "type": "object",
          "properties": {
            "framework": {
              "type": "string",
              "enum": ["net10.0", "net11.0"],
              "default": "net10.0",
              "minLength": 3,
              "maxLength": 16
            },
            "count": {
              "type": "integer",
              "minimum": 1,
              "maximum": 10
            }
          },
          "required": ["framework"],
          "additionalProperties": false
        }
        """;

    [Fact]
    public async Task ManifestAndRulesReadersAcceptOnlyTheirClosedShapes()
    {
        var manifest = await ReadYamlAsync(BlueprintYamlDocumentKind.Manifest, ValidManifest);
        var rules = await ReadYamlAsync(BlueprintYamlDocumentKind.Rules, ValidRules);

        Assert.True(manifest.IsValid);
        Assert.NotNull(manifest.Value);
        Assert.True(rules.IsValid);
        Assert.NotNull(rules.Value);
    }

    public static TheoryData<string> InvalidManifestYaml =>
        new()
        {
            ValidManifest + "\nunknown: value",
            ValidManifest + "\nid: duplicate",
            "id: &identity desktop.csharp-wpf-tool\nname: *identity",
            "defaults: &defaults\n  name: Tool\n<<: *defaults",
            "id: !custom desktop.csharp-wpf-tool",
            "? [complex, key]\n: value",
            "---\nid: first\n---\nid: second",
        };

    [Theory]
    [MemberData(nameof(InvalidManifestYaml))]
    public async Task ManifestReaderRejectsUnsafeOrAmbiguousYaml(string yaml)
    {
        var result = await ReadYamlAsync(BlueprintYamlDocumentKind.Manifest, yaml);

        Assert.False(result.IsValid);
        Assert.Equal("DF-BP-001", Assert.Single(result.Issues).Code);
    }

    [Fact]
    public async Task YamlReaderRejectsScalarDepthAndControlFileBoundsWithoutEchoingInput()
    {
        const string sensitive = "password=do-not-echo";
        var longScalar = ValidManifest.Replace(
            "Desktop Tool",
            new string('x', BlueprintControlLimits.MaximumScalarCharacters + 1),
            StringComparison.Ordinal);
        var deep = "root: " + string.Concat(
            Enumerable.Repeat("[", BlueprintControlLimits.MaximumDepth + 1))
            + "value"
            + string.Concat(Enumerable.Repeat("]", BlueprintControlLimits.MaximumDepth + 1));
        var oversized = new string('x', BlueprintControlLimits.MaximumControlFileBytes + 1);

        var scalarResult = await ReadYamlAsync(BlueprintYamlDocumentKind.Manifest, longScalar);
        var depthResult = await ReadYamlAsync(BlueprintYamlDocumentKind.Manifest, deep);
        var sizeResult = await ReadYamlAsync(BlueprintYamlDocumentKind.Manifest, oversized + sensitive);

        Assert.Equal("DF-BP-004", Assert.Single(scalarResult.Issues).Code);
        Assert.Equal("DF-BP-004", Assert.Single(depthResult.Issues).Code);
        Assert.Equal("DF-BP-004", Assert.Single(sizeResult.Issues).Code);
        Assert.DoesNotContain(
            sensitive,
            string.Join(' ', sizeResult.Issues.Select(issue => issue.Summary)),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SchemaReaderAcceptsTheSupportedSubset()
    {
        var result = await ReadSchemaAsync(ValidSchema);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Value);
    }

    public static TheoryData<string> InvalidSchemas =>
        new()
        {
            """{"type":"object","type":"object","properties":{},"required":[],"additionalProperties":false}""",
            """{"type":"object","properties":{},"required":[],"additionalProperties":false,"unknown":true}""",
            """{"type":"object","properties":{"name":{"type":"string","pattern":".*"}},"required":[],"additionalProperties":false}""",
            """{"type":"object","properties":{"name":{"$ref":"https://example.test/schema"}},"required":[],"additionalProperties":false}""",
            """{"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","properties":{},"required":[],"additionalProperties":false}""",
            """{"type":"object","properties":{},"required":[],"additionalProperties":true}""",
        };

    [Theory]
    [MemberData(nameof(InvalidSchemas))]
    public async Task SchemaReaderRejectsDuplicatesRemoteFeaturesAndUnsupportedKeywords(string json)
    {
        var result = await ReadSchemaAsync(json);

        Assert.False(result.IsValid);
        Assert.Equal("DF-BP-001", Assert.Single(result.Issues).Code);
    }

    [Fact]
    public async Task SchemaReaderRejectsDepthAndFileBounds()
    {
        var deepValue = string.Concat(Enumerable.Repeat("[", BlueprintControlLimits.MaximumDepth + 1))
            + "0"
            + string.Concat(Enumerable.Repeat("]", BlueprintControlLimits.MaximumDepth + 1));
        var deep = """{"type":"object","properties":{"x":{"type":"string","default":VALUE}},"required":[],"additionalProperties":false}"""
            .Replace("VALUE", deepValue, StringComparison.Ordinal);
        var oversized = new string(' ', BlueprintControlLimits.MaximumControlFileBytes + 1);

        var depthResult = await ReadSchemaAsync(deep);
        var sizeResult = await ReadSchemaAsync(oversized);

        Assert.Equal("DF-BP-004", Assert.Single(depthResult.Issues).Code);
        Assert.Equal("DF-BP-004", Assert.Single(sizeResult.Issues).Code);
    }

    private static async Task<BlueprintLoadResult<BlueprintYamlDocument>> ReadYamlAsync(
        BlueprintYamlDocumentKind kind,
        string content)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        return await new BlueprintYamlReader(kind).ReadAsync(stream, CancellationToken.None);
    }

    private static async Task<BlueprintLoadResult<BlueprintInputSchemaDocument>> ReadSchemaAsync(
        string content)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        return await new BlueprintJsonSchemaReader().ReadAsync(stream, CancellationToken.None);
    }
}
