using DevForge.Application.Contracts.Persistence;

namespace DevForge.UnitTests.Application.Persistence;

public sealed class PersistableJsonTests
{
    [Fact]
    public void CanonicalizesObjectsAndSnapshotsTheValue()
    {
        var result = PersistableJson.Create(" { \"b\" : 2, \"a\" : { \"d\" : 4, \"c\" : 3 } } ");

        Assert.True(result.IsValid);
        Assert.Equal("{\"a\":{\"c\":3,\"d\":4},\"b\":2}", result.Value.Value);
        Assert.Equal(result.Value.Value.Length, result.Value.Utf8ByteCount);
    }

    [Theory]
    [InlineData("not-json", "persistence.json.invalid")]
    [InlineData("[]", "persistence.json.root.invalid")]
    [InlineData("{\"name\":1,\"name\":2}", "persistence.json.property.duplicate")]
    [InlineData("{\"databasePassword\":\"value\"}", "persistence.json.secret-detected")]
    [InlineData("{\"log\":\"Authorization: Bearer abcdefghijklmnop\"}", "persistence.json.secret-detected")]
    [InlineData("{\"log\":\"-----BEGIN PRIVATE KEY-----\"}", "persistence.json.secret-detected")]
    [InlineData("{\"log\":\"ghp_abcdefghijklmnop\"}", "persistence.json.secret-detected")]
    [InlineData("{\"log\":\"contents of .env\"}", "persistence.json.secret-detected")]
    public void RejectsInvalidOrSecretBearingPayloads(string json, string expectedCode)
    {
        var result = PersistableJson.Create(json);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == expectedCode);
    }

    [Theory]
    [InlineData("{\"message\":\"Foreign key: FK_ProjectRun\"}")]
    [InlineData("{\"message\":\"The .env file was not read\"}")]
    [InlineData("{\"monkey\":\"value\"}")]
    public void AcceptsSafeDiagnosticFalsePositives(string json)
    {
        Assert.True(PersistableJson.Create(json).IsValid);
    }

    [Fact]
    public void RejectsPayloadOverTheUtf8Limit()
    {
        var json = "{\"value\":\"" + new string('é', PersistableJson.MaxUtf8ByteCount) + "\"}";

        var result = PersistableJson.Create(json);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "persistence.json.too-large");
    }
}
