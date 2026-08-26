using System.Collections.Immutable;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Infrastructure.Blueprints;

namespace DevForge.IntegrationTests.Infrastructure.Security;

public sealed class M10HostileInputMatrixTests
{
    [Theory]
    [InlineData("..\\outside")]
    [InlineData("C:\\outside")]
    [InlineData("\\\\server\\share")]
    [InlineData("\\\\?\\GLOBALROOT\\Device\\HarddiskVolumeShadowCopy1")]
    [InlineData("\\??\\C:\\outside")]
    [InlineData("src/forward-slash")]
    [InlineData("src\\child.")]
    [InlineData("src\\COM1")]
    [InlineData("src\\.env")]
    public void UnsafeOutputPathsAreRejectedBeforeExecution(string path)
    {
        var action = Action("create-directory", ("path", Text(path)));

        var issue = Assert.Single(BlueprintActionPolicy.Validate(action, BlueprintTrust.BuiltIn));

        Assert.Equal("DF-BP-003", issue.Code);
        Assert.DoesNotContain(path, issue.Summary, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    [InlineData("cmd")]
    [InlineData("bash")]
    [InlineData("curl")]
    [InlineData("msiexec")]
    public void ShellDownloadAndInstallerIdentitiesAreRejectedBeforeExecution(string executable)
    {
        var action = Action(
            "run-process",
            ("executable", Text(executable)),
            ("arguments", Sequence(Text("ignored"))),
            ("workingDirectory", Text(".")),
            ("allowedExitCodes", Sequence(BlueprintValue.FromInteger(0))));

        var issue = Assert.Single(BlueprintActionPolicy.Validate(action, BlueprintTrust.BuiltIn));

        Assert.Equal("DF-BP-003", issue.Code);
        Assert.DoesNotContain(executable, issue.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("registry-operation")]
    [InlineData("firewall-operation")]
    [InlineData("service-operation")]
    [InlineData("require-administrator")]
    [InlineData("download-executable")]
    public void PrivilegedAndDownloadHandlersAreOutsideTheClosedVocabulary(string handler)
    {
        var issue = Assert.Single(BlueprintActionPolicy.Validate(
            Action(handler),
            BlueprintTrust.BuiltIn));

        Assert.Equal("DF-BP-003", issue.Code);
        Assert.DoesNotContain(handler, issue.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(BlueprintTrust.Untrusted)]
    [InlineData(BlueprintTrust.Quarantined)]
    public void NonExecutableTrustFailsBeforeActionInspection(BlueprintTrust trust)
    {
        var issue = Assert.Single(BlueprintActionPolicy.Validate(
            Action("create-directory", ("path", Text("src"))),
            trust));

        Assert.Equal("DF-BP-002", issue.Code);
    }

    [Fact]
    public void SecretShapedNestedPayloadKeysAreRejectedWithoutEcho()
    {
        const string secretKey = "github-token";
        var result = BlueprintValue.FromObject(
        [
            KeyValuePair.Create<string, BlueprintValue?>(
                secretKey,
                Text("not-a-real-secret")),
        ]);

        var issue = Assert.Single(result.Issues);

        Assert.Equal("blueprint.value.key.secret-shaped", issue.Code);
        Assert.DoesNotContain(secretKey, issue.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static BlueprintActionDefinition Action(
        string handler,
        params (string Key, BlueprintValue Value)[] parameters) =>
        new(
            "hostile-fixture",
            handler,
            parameters.ToImmutableDictionary(
                item => item.Key,
                item => item.Value,
                StringComparer.Ordinal),
            TimeSpan.FromMinutes(1));

    private static BlueprintValue Text(string value) => BlueprintValue.FromString(value).Value;

    private static BlueprintValue Sequence(params BlueprintValue[] values) =>
        BlueprintValue.FromArray(values).Value;
}
