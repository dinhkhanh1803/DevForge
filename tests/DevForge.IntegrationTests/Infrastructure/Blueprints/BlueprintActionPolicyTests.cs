using System.Collections.Immutable;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Infrastructure.Blueprints;

namespace DevForge.IntegrationTests.Infrastructure.Blueprints;

public sealed class BlueprintActionPolicyTests
{
    public static TheoryData<BlueprintActionDefinition> BuiltInActions =>
        new()
        {
            Action("create-directory", ("path", Text("src"))),
            Action("render-template", ("source", Text("templates\\app.txt")), ("target", Text("src\\App.cs"))),
            Action("copy-overlay", ("source", Text("overlays\\base")), ("target", Text("src"))),
            Action("patch-json", ("target", Text("appsettings.json")), ("operations", Sequence())),
            Action("patch-yaml", ("target", Text("config.yaml")), ("operations", Sequence())),
            Action("patch-xml", ("target", Text("App.csproj")), ("operations", Sequence())),
            Action(
                "run-process",
                ("executable", Text("dotnet")),
                ("arguments", Sequence(Text("restore"))),
                ("workingDirectory", Text(".")),
                ("allowedExitCodes", Sequence(BlueprintValue.FromInteger(0)))),
            Action(
                "package-install",
                ("packageManager", Text("dotnet")),
                ("arguments", Sequence(Text("add"), Text("package"))),
                ("workingDirectory", Text("src"))),
            Action(
                "validate-command",
                ("executable", Text("dotnet")),
                ("arguments", Sequence(Text("build"))),
                ("workingDirectory", Text(".")),
                ("allowedExitCodes", Sequence(BlueprintValue.FromInteger(0))),
                ("required", BlueprintValue.FromBoolean(true))),
            Action(
                "validate-file-exists",
                ("path", Text("README.md")),
                ("required", BlueprintValue.FromBoolean(true))),
            Action(
                "validate-file-content",
                ("path", Text("README.md")),
                ("contains", Text("Framework: net10.0")),
                ("required", BlueprintValue.FromBoolean(true))),
            Action("git-operation", ("operation", Text("initialize")), ("payload", Map())),
            Action("github-operation", ("operation", Text("create-repository")), ("payload", Map())),
            Action("finalize-workspace"),
        };

    [Theory]
    [MemberData(nameof(BuiltInActions))]
    public void BuiltInTrustAllowsOnlyTheClosedTypedActionCatalog(BlueprintActionDefinition action)
    {
        var issues = BlueprintActionPolicy.Validate(action, BlueprintTrust.BuiltIn);

        Assert.Empty(issues);
    }

    [Theory]
    [InlineData("git-operation")]
    [InlineData("github-operation")]
    [InlineData("finalize-workspace")]
    public void TrustedLocalCannotUseBuiltInOnlyActions(string handler)
    {
        var action = handler switch
        {
            "git-operation" => Action(handler, ("operation", Text("initialize")), ("payload", Map())),
            "github-operation" => Action(
                handler,
                ("operation", Text("create-repository")),
                ("payload", Map())),
            _ => Action(handler),
        };

        var issues = BlueprintActionPolicy.Validate(action, BlueprintTrust.TrustedLocal);

        Assert.Equal("DF-BP-003", Assert.Single(issues).Code);
    }

    [Theory]
    [InlineData(BlueprintTrust.Untrusted)]
    [InlineData(BlueprintTrust.Quarantined)]
    public void NonExecutableTrustCannotValidateActions(BlueprintTrust trust)
    {
        var issues = BlueprintActionPolicy.Validate(
            Action("create-directory", ("path", Text("src"))),
            trust);

        Assert.Equal("DF-BP-002", Assert.Single(issues).Code);
    }

    [Fact]
    public void PolicyRejectsUnknownMissingAndShellShapedParameters()
    {
        var missing = Action("run-process", ("executable", Text("dotnet")));
        var unknown = Action(
            "run-process",
            ("executable", Text("dotnet")),
            ("arguments", Sequence()),
            ("workingDirectory", Text(".")),
            ("allowedExitCodes", Sequence(BlueprintValue.FromInteger(0))),
            ("commandLine", Text("dotnet build")),
            ("shell", BlueprintValue.FromBoolean(true)));
        var handler = Action("custom-script");

        Assert.All(
            [
                BlueprintActionPolicy.Validate(missing, BlueprintTrust.BuiltIn),
                BlueprintActionPolicy.Validate(unknown, BlueprintTrust.BuiltIn),
                BlueprintActionPolicy.Validate(handler, BlueprintTrust.BuiltIn),
            ],
            issues => Assert.Equal("DF-BP-003", Assert.Single(issues).Code));
    }

    [Theory]
    [InlineData("..\\outside")]
    [InlineData("C:\\outside")]
    [InlineData("\\\\server\\share")]
    [InlineData("src/forward")]
    [InlineData("CON")]
    [InlineData(".env")]
    [InlineData("src\\.env")]
    public void PolicyRejectsUnsafeOutputPaths(string path)
    {
        var issues = BlueprintActionPolicy.Validate(
            Action("create-directory", ("path", Text(path))),
            BlueprintTrust.BuiltIn);

        Assert.Equal("DF-BP-003", Assert.Single(issues).Code);
    }

    [Fact]
    public void PolicyAllowsEnvExampleButNotEnvTargets()
    {
        var issues = BlueprintActionPolicy.Validate(
            Action("create-directory", ("path", Text("config\\.env.example"))),
            BlueprintTrust.BuiltIn);

        Assert.Empty(issues);
    }

    [Fact]
    public void PolicyRejectsWrongTypedArgumentsAndExitCodes()
    {
        var arguments = Action(
            "run-process",
            ("executable", Text("dotnet")),
            ("arguments", Text("build")),
            ("workingDirectory", Text(".")),
            ("allowedExitCodes", Sequence(Text("zero"))));

        var issues = BlueprintActionPolicy.Validate(arguments, BlueprintTrust.BuiltIn);

        Assert.Equal("DF-BP-003", Assert.Single(issues).Code);
    }

    [Theory]
    [InlineData("{{ unknown.value }}")]
    [InlineData("{{ project.name | upper }}")]
    [InlineData("{{ project.name() }}")]
    [InlineData("{{ {{ project.name }} }}")]
    [InlineData("{{ project.token }}")]
    [InlineData("{{ project.name")]
    public void PolicyRejectsUnknownMalformedRecursiveOrSecretShapedVariables(string value)
    {
        var issues = BlueprintActionPolicy.Validate(
            Action("create-directory", ("path", Text(value))),
            BlueprintTrust.BuiltIn);

        Assert.Equal("DF-BP-003", Assert.Single(issues).Code);
        Assert.DoesNotContain(value, issues[0].Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void PolicyAcceptsKnownVariablesWithoutReparsingTheirFutureValues()
    {
        var issues = BlueprintActionPolicy.Validate(
            Action("create-directory", ("path", Text("src\\{{ project.safe-name }}"))),
            BlueprintTrust.BuiltIn);

        Assert.Empty(issues);
    }

    private static BlueprintActionDefinition Action(
        string handler,
        params (string Key, BlueprintValue Value)[] parameters)
    {
        return new BlueprintActionDefinition(
            "action",
            handler,
            parameters.ToImmutableDictionary(
                item => item.Key,
                item => item.Value,
                StringComparer.Ordinal),
            TimeSpan.FromMinutes(1));
    }

    private static BlueprintValue Text(string value)
    {
        return BlueprintValue.FromString(value).Value;
    }

    private static BlueprintValue Sequence(params BlueprintValue[] values)
    {
        return BlueprintValue.FromArray(values).Value;
    }

    private static BlueprintValue Map(params (string Key, BlueprintValue Value)[] values)
    {
        return BlueprintValue.FromObject(
            values.Select(item => KeyValuePair.Create<string, BlueprintValue?>(item.Key, item.Value))).Value;
    }
}
