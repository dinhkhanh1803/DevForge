using System.Globalization;
using DevForge.Application.Contracts;
using DevForge.Infrastructure.Templates;

namespace DevForge.IntegrationTests.Infrastructure.Templates;

[Collection(TemplateRendererCultureGroup.Name)]
public sealed class RestrictedTemplateRendererTests
{
    private readonly RestrictedScribanTemplateRenderer _renderer = new();

    [Fact]
    public async Task RenderAsyncRendersScalarAndDottedValuesVerbatim()
    {
        var request = CreateRequest(
            "Name={{ project.name }}|Value={{ value }}\r\n",
            ("project.name", "DevForge"),
            ("value", "  exact  "));

        var result = await _renderer.RenderAsync(request, CancellationToken.None);

        Assert.Equal("Name=DevForge|Value=  exact  \r\n", result);
    }

    [Theory]
    [InlineData("Unicode: {{ value }}", "Tiếng Việt 🚀", "Unicode: Tiếng Việt 🚀")]
    [InlineData("before\nafter\n", "unused", "before\nafter\n")]
    [InlineData("before\r\nafter\r\n", "unused", "before\r\nafter\r\n")]
    [InlineData("first\n\n  {{ value }}\n", "indented", "first\n\n  indented\n")]
    [InlineData("{{ value }}", "  preserve me  ", "  preserve me  ")]
    [InlineData("{{ true }}|{{ \"literal\" }}", "unused", "true|literal")]
    public async Task RenderAsyncPreservesExactText(
        string template,
        string value,
        string expected)
    {
        var result = await _renderer.RenderAsync(
            CreateRequest(template, ("value", value)),
            CancellationToken.None);

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task RenderAsyncUsesIndependentContextsConcurrently()
    {
        var tasks = Enumerable.Range(0, 32)
            .Select(index => _renderer.RenderAsync(
                CreateRequest("{{ project.name }}", ("project.name", $"P{index}")),
                CancellationToken.None));

        Assert.Equal(
            Enumerable.Range(0, 32).Select(index => $"P{index}"),
            await Task.WhenAll(tasks));
    }

    [Fact]
    public async Task RenderAsyncIsIndependentOfCurrentCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var request = CreateRequest(
                "{{ project.name }}|{{ true }}",
                ("project.name", "IDENTIFIER"));
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");
            var turkish = await _renderer.RenderAsync(request, CancellationToken.None);

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            var english = await _renderer.RenderAsync(request, CancellationToken.None);

            Assert.Equal(turkish, english);
            Assert.Equal("IDENTIFIER|true", english);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
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

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TemplateRendererCultureGroup
{
    public const string Name = "Template renderer culture";
}
