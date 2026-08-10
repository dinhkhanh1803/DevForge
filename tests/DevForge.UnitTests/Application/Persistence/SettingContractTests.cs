using DevForge.Application.Contracts.Persistence;

namespace DevForge.UnitTests.Application.Persistence;

public sealed class SettingContractTests
{
    [Fact]
    public void TypedValuesExposeOnlyTheirDeclaredKind()
    {
        var text = AppSettingValue.CreateString("dark").Value;
        var boolean = AppSettingValue.CreateBoolean(true);
        var integer = AppSettingValue.CreateInteger(90);

        Assert.Equal(AppSettingValueKind.Text, text.Kind);
        Assert.Equal("dark", text.StringValue);
        Assert.Equal(AppSettingValueKind.BooleanFlag, boolean.Kind);
        Assert.True(boolean.BooleanValue);
        Assert.Equal(AppSettingValueKind.WholeNumber, integer.Kind);
        Assert.Equal(90, integer.IntegerValue);
        Assert.DoesNotContain("dark", text.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("githubToken")]
    [InlineData("databasepassword")]
    [InlineData("Connection_String")]
    public void SettingRejectsSecretShapedKeys(string key)
    {
        var result = AppSetting.Create(
            key,
            AppSettingValue.CreateBoolean(true),
            DateTimeOffset.UnixEpoch);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "persistence.setting.key.secret-shaped");
    }

    [Theory]
    [InlineData("Bearer abcdefghijklmnop")]
    [InlineData("sk-proj-abcdefghijklmnop")]
    [InlineData("AKIAABCDEFGHIJKLMNOP")]
    public void StringSettingRejectsCredentialShapedValues(string value)
    {
        var result = AppSettingValue.CreateString(value);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "persistence.setting.value.secret-shaped");
    }

    [Fact]
    public void SettingNormalizesIdentityAndPreservesSafeStringValue()
    {
        var setting = AppSetting.Create(
            "  ui.theme  ",
            AppSettingValue.CreateString("  dark  ").Value,
            DateTimeOffset.UnixEpoch).Value;

        Assert.Equal("ui.theme", setting.Key);
        Assert.Equal("  dark  ", setting.Value.StringValue);
    }
}
