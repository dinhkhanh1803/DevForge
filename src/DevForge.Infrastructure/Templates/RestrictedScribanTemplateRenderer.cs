using DevForge.Application.Contracts;
using Scriban;
using Scriban.Syntax;

namespace DevForge.Infrastructure.Templates;

public sealed class RestrictedScribanTemplateRenderer : ITemplateRenderer
{
    public const int MaximumOutputLength = 4 * 1024 * 1024;

    public async Task<string> RenderAsync(
        TemplateRenderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var template = Template.Parse(request.Template);
        if (template.HasErrors || template.Page is null)
        {
            throw TemplateRenderFailures.Parse();
        }

        try
        {
            RestrictedTemplatePolicy.Validate(template.Page);
        }
        catch (ForbiddenTemplateConstructException)
        {
            throw TemplateRenderFailures.Policy();
        }

        cancellationToken.ThrowIfCancellationRequested();
        var output = new BoundedTemplateOutput(MaximumOutputLength, cancellationToken);
        var context = RestrictedTemplateContextFactory.Create(request, output, cancellationToken);
        try
        {
            var result = await template.RenderAsync(context).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ScriptAbortException)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (ScriptRuntimeException exception) when (TemplateRenderFailures.IsOutputLimit(exception))
        {
            throw TemplateRenderFailures.OutputTooLarge();
        }
        catch (ScriptRuntimeException exception) when (TemplateRenderFailures.IsMissingVariable(exception))
        {
            throw TemplateRenderFailures.MissingVariable();
        }
        catch (ScriptRuntimeException)
        {
            throw TemplateRenderFailures.RenderFailed();
        }
    }
}
