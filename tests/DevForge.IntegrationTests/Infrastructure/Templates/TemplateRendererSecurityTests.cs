using DevForge.Application.Contracts;
using DevForge.Infrastructure;
using DevForge.Infrastructure.Templates;

namespace DevForge.IntegrationTests.Infrastructure.Templates;

public sealed class TemplateRendererSecurityTests
{
    private const string PrivateMarker = "private-template-marker";
    private readonly RestrictedScribanTemplateRenderer _renderer = new();

    [Fact]
    public async Task RenderAsyncReturnsOnlyStableScrubbedParseFailure()
    {
        var exception = await Assert.ThrowsAsync<InfrastructureOperationException>(
            () => _renderer.RenderAsync(
                CreateRequest($"{PrivateMarker}: {{{{ if }}}}"),
                CancellationToken.None));

        AssertSafeFailure(
            exception,
            "template.parse.invalid",
            "The template syntax is invalid.",
            PrivateMarker,
            "<input>");
    }

    [Fact]
    public async Task RenderAsyncReturnsOnlyStableScrubbedPolicyFailure()
    {
        var exception = await Assert.ThrowsAsync<InfrastructureOperationException>(
            () => _renderer.RenderAsync(
                CreateRequest($"{PrivateMarker}: {{{{ for item in [1] }}}}x{{{{ end }}}}"),
                CancellationToken.None));

        AssertSafeFailure(
            exception,
            "template.policy.forbidden",
            "The template uses a forbidden construct.",
            PrivateMarker,
            "for item");
    }

    [Fact]
    public async Task RenderAsyncReturnsOnlyStableScrubbedMissingVariableFailure()
    {
        var exception = await Assert.ThrowsAsync<InfrastructureOperationException>(
            () => _renderer.RenderAsync(
                CreateRequest($"{PrivateMarker}: {{{{ missing.value }}}}"),
                CancellationToken.None));

        AssertSafeFailure(
            exception,
            "template.variable.missing",
            "A required template variable is missing.",
            PrivateMarker,
            "missing.value",
            "Invalid target function");
    }

    [Fact]
    public async Task RenderAsyncRejectsPolicyNodeLimit()
    {
        var template = string.Concat(Enumerable.Repeat("{{ value }}", 10_001));

        var exception = await Assert.ThrowsAsync<InfrastructureOperationException>(
            () => _renderer.RenderAsync(
                CreateRequest(template, ("value", "x")),
                CancellationToken.None));

        AssertSafeFailure(
            exception,
            "template.policy.forbidden",
            "The template uses a forbidden construct.");
    }

    [Fact]
    public async Task RenderAsyncRejectsPolicyDepthLimit()
    {
        var condition = $"{new string('(', 65)}true{new string(')', 65)}";

        var exception = await Assert.ThrowsAsync<InfrastructureOperationException>(
            () => _renderer.RenderAsync(
                CreateRequest($"{{{{ if {condition} }}}}yes{{{{ end }}}}"),
                CancellationToken.None));

        AssertSafeFailure(
            exception,
            "template.policy.forbidden",
            "The template uses a forbidden construct.");
    }

    [Fact]
    public async Task RenderAsyncRejectsOutputBeyondFourMebibytesWithoutPartialOutput()
    {
        const string partialMarker = "rendered-partial-marker";
        var value = partialMarker + new string('x', (64 * 1024) - partialMarker.Length);
        var template = string.Concat(Enumerable.Repeat("{{ value }}", 65));

        var exception = await Assert.ThrowsAsync<InfrastructureOperationException>(
            () => _renderer.RenderAsync(
                CreateRequest(template, ("value", value)),
                CancellationToken.None));

        AssertSafeFailure(
            exception,
            "template.output.too-large",
            "The rendered template exceeds the output limit.",
            partialMarker,
            value);
    }

    [Fact]
    public async Task RenderAsyncHonorsPreCancelledToken()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _renderer.RenderAsync(CreateRequest("text"), cancellation.Token));
    }

    [Fact]
    public async Task BoundedOutputHonorsCancellationBeforeEveryWrite()
    {
        using var cancellation = new CancellationTokenSource();
        var output = new BoundedTemplateOutput(16, cancellation.Token);
        output.Write("a", 0, 1);
        await cancellation.CancelAsync();

        Assert.ThrowsAny<OperationCanceledException>(() => output.Write("b", 0, 1));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await output.WriteAsync("c", 0, 1, CancellationToken.None));
        Assert.Equal("a", output.ToString());
    }

    [Fact]
    public async Task ConcurrentFailuresRemainIndependentAndScrubbed()
    {
        var tasks = Enumerable.Range(0, 32)
            .Select(index => Assert.ThrowsAsync<InfrastructureOperationException>(
                () => _renderer.RenderAsync(
                    CreateRequest($"private-{index}: {{{{ missing.value }}}}"),
                    CancellationToken.None)));

        var exceptions = await Task.WhenAll(tasks);

        Assert.All(
            exceptions,
            exception => AssertSafeFailure(
                exception,
                "template.variable.missing",
                "A required template variable is missing.",
                "private-",
                "missing.value"));
    }

    [Fact]
    public void RenderFailureFactoryReturnsOnlyStableScrubbedFailure()
    {
        var exception = TemplateRenderFailures.RenderFailed();

        AssertSafeFailure(
            exception,
            "template.render.failed",
            "The template could not be rendered safely.",
            PrivateMarker,
            "Invalid target function");
    }

    [Fact]
    public void FatalClassificationTraversesExceptionChainWithoutInspectingMessages()
    {
#pragma warning disable CA2201 // Intentional runtime-fatal classification fixture.
        var exception = new InvalidOperationException(
            PrivateMarker,
            new OutOfMemoryException(PrivateMarker));
#pragma warning restore CA2201

        Assert.True(TemplateRenderFailures.IsFatal(exception));
    }

    [Fact]
    public void RequestRejectsCredentialValueWithoutLeakingIt()
    {
        const string credential = "Authorization: Bearer abcdefghijklmnop";

        var result = TemplateRenderRequest.Create(
            "{{ value }}",
            [KeyValuePair.Create<string, string?>("value", credential)]);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "template.context.value.secret-shaped");
        Assert.All(
            result.Issues,
            issue => Assert.DoesNotContain(
                credential,
                $"{issue.Code}|{issue.Message}|{issue.Location}",
                StringComparison.Ordinal));
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

    private static void AssertSafeFailure(
        InfrastructureOperationException exception,
        string expectedCode,
        string expectedMessage,
        params string[] forbiddenValues)
    {
        Assert.Equal(expectedCode, exception.Code);
        Assert.Equal(expectedMessage, exception.Message);
        Assert.Null(exception.InnerException);
        Assert.All(
            forbiddenValues,
            value => Assert.DoesNotContain(
                value,
                exception.ToString(),
                StringComparison.OrdinalIgnoreCase));
    }
}
