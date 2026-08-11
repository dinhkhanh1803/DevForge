namespace DevForge.Infrastructure.Templates;

internal static class TemplateRenderFailures
{
    public static InfrastructureOperationException Parse()
    {
        return new InfrastructureOperationException(
            "template.parse.invalid",
            "The template syntax is invalid.");
    }

    public static InfrastructureOperationException Policy()
    {
        return new InfrastructureOperationException(
            "template.policy.forbidden",
            "The template uses a forbidden construct.");
    }

    public static InfrastructureOperationException MissingVariable()
    {
        return new InfrastructureOperationException(
            "template.variable.missing",
            "A required template variable is missing.");
    }

    public static InfrastructureOperationException OutputTooLarge()
    {
        return new InfrastructureOperationException(
            "template.output.too-large",
            "The rendered template exceeds the output limit.");
    }

    public static InfrastructureOperationException RenderFailed()
    {
        return new InfrastructureOperationException(
            "template.render.failed",
            "The template could not be rendered safely.");
    }

    public static bool IsMissingVariable(Exception exception)
    {
        return Contains<MissingTemplateVariableException>(exception);
    }

    public static bool IsOutputLimit(Exception exception)
    {
        return Contains<TemplateOutputLimitExceededException>(exception);
    }

    private static bool Contains<TException>(Exception? exception)
        where TException : Exception
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is TException)
            {
                return true;
            }
        }

        return false;
    }
}

internal sealed class ForbiddenTemplateConstructException : Exception;

internal sealed class MissingTemplateVariableException : Exception;

internal sealed class TemplateOutputLimitExceededException : Exception;
