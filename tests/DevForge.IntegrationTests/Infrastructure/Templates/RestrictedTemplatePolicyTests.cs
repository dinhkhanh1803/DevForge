using DevForge.Application.Contracts;
using DevForge.Infrastructure;
using DevForge.Infrastructure.Templates;

namespace DevForge.IntegrationTests.Infrastructure.Templates;

public sealed class RestrictedTemplatePolicyTests
{
    private readonly RestrictedScribanTemplateRenderer _renderer = new();

    public static TheoryData<string, string, string> AllowedTemplates => new()
    {
        { "{{ if project.kind == \"api\" }}yes{{ else }}no{{ end }}", "api", "yes" },
        { "{{ if project.kind != \"api\" }}yes{{ else }}no{{ end }}", "worker", "yes" },
        { "{{ if project.kind == \"worker\" }}worker{{ else if project.kind == \"api\" }}api{{ else }}other{{ end }}", "api", "api" },
        { "{{ if (project.kind == \"api\") && !false }}yes{{ end }}", "api", "yes" },
        { "{{ if project.kind == \"api\" || project.kind == \"worker\" }}yes{{ end }}", "worker", "yes" },
        { "{{ if true == true }}{{ if project.kind == \"api\" }}nested{{ end }}{{ end }}", "api", "nested" },
        { "{{ if true != false }}yes{{ end }}", "unused", "yes" },
    };

    public static TheoryData<string, string> ForbiddenTemplates => new()
    {
        { "assignment", "{{ x = \"value\" }}" },
        { "increment", "{{ x++ }}" },
        { "for", "{{ for x in [1] }}{{ x }}{{ end }}" },
        { "while", "{{ while true }}{{ break }}{{ end }}" },
        { "tablerow", "{{ tablerow x in [1] }}{{ x }}{{ end }}" },
        { "case-when", "{{ case project.name }}{{ when \"x\" }}yes{{ end }}" },
        { "break", "{{ break }}" },
        { "continue", "{{ continue }}" },
        { "function-definition", "{{ func f }}x{{ end }}" },
        { "function-call", "{{ string.upcase project.name }}" },
        { "pipe", "{{ project.name | string.upcase }}" },
        { "eval", "{{ object.eval \"1 + 1\" }}" },
        { "eval-template", "{{ object.eval_template \"{{ 1 }}\" }}" },
        { "include", "{{ include \"other\" }}" },
        { "import", "{{ import project }}" },
        { "capture", "{{ capture x }}value{{ end }}" },
        { "wrap", "{{ wrap project }}value{{ end }}" },
        { "array", "{{ [project.name] }}" },
        { "object", "{{ { name: project.name } }}" },
        { "indexer", "{{ project[\"name\"] }}" },
        { "optional-member", "{{ project?.name }}" },
        { "interpolated-string", "{{ $\"{project.name}\" }}" },
        { "conditional-expression", "{{ project.name == \"x\" ? \"a\" : \"b\" }}" },
        { "local-variable", "{{ $x }}" },
        { "with", "{{ with project }}{{ name }}{{ end }}" },
        { "arithmetic", "{{ project.name + \"x\" }}" },
        { "greater-than", "{{ if project.name > \"x\" }}yes{{ end }}" },
        { "whitespace-control", "{{- project.name -}}" },
        { "escaped-code", "{%{ project.name }%}" },
    };

    [Theory]
    [MemberData(nameof(AllowedTemplates))]
    public async Task RenderAsyncAllowsClosedConditionalGrammar(
        string template,
        string kind,
        string expected)
    {
        var result = await _renderer.RenderAsync(
            CreateRequest(template, ("project.kind", kind)),
            CancellationToken.None);

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task RenderAsyncRejectsImplicitStringTruthiness()
    {
        var exception = await Assert.ThrowsAsync<InfrastructureOperationException>(
            () => _renderer.RenderAsync(
                CreateRequest("{{ if project.enabled }}yes{{ end }}", ("project.enabled", "true")),
                CancellationToken.None));

        Assert.Equal("template.policy.forbidden", exception.Code);
    }

    [Theory]
    [MemberData(nameof(ForbiddenTemplates))]
    public async Task RenderAsyncRejectsEveryForbiddenFamily(string caseName, string template)
    {
        var exception = await Assert.ThrowsAsync<InfrastructureOperationException>(
            () => _renderer.RenderAsync(
                CreateRequest(template, ("project.name", "x")),
                CancellationToken.None));

        Assert.True(
            exception.Code == "template.policy.forbidden",
            $"Forbidden case '{caseName}' returned '{exception.Code}'.");
    }

    private static TemplateRenderRequest CreateRequest(
        string template,
        params (string Name, string Value)[] context)
    {
        var result = TemplateRenderRequest.Create(
            template,
            context.Select(item => KeyValuePair.Create<string, string?>(item.Name, item.Value)));
        Assert.True(result.IsValid);
        return result.Value;
    }
}
