using System.Collections.Immutable;
using DevForge.Application.Contracts;
using DevForge.Domain.Execution;

namespace DevForge.Infrastructure.Execution;

internal sealed class FileExistsValidationHandler()
    : FileValidationHandlerBase("validate-file-exists")
{
    protected override ImmutableHashSet<string> RequiredInputNames { get; } =
        ImmutableHashSet.Create(StringComparer.Ordinal, "path");

    protected override async Task<HandlerEffect> ValidateAsync(
        HandlerExecutionContext context,
        CancellationToken cancellationToken)
    {
        var path = PathInput(context, "path");
        var content = await ReadBoundedAsync(
            context.Payload,
            path,
            MaximumFileBytes,
            cancellationToken).ConfigureAwait(false);
        return new HandlerEffect([], Digest(content));
    }
}

internal sealed class FileContentValidationHandler()
    : FileValidationHandlerBase("validate-file-content")
{
    protected override ImmutableHashSet<string> RequiredInputNames { get; } =
        ImmutableHashSet.Create(StringComparer.Ordinal, "contains", "path");

    protected override async Task<HandlerEffect> ValidateAsync(
        HandlerExecutionContext context,
        CancellationToken cancellationToken)
    {
        var path = PathInput(context, "path");
        var expected = ValueInput(context, "contains");
        if (expected.Kind != PlanValueKind.Text || string.IsNullOrEmpty(expected.StringValue))
        {
            throw new HandlerInputException();
        }

        var content = await ReadBoundedAsync(
            context.Payload,
            path,
            MaximumFileBytes,
            cancellationToken).ConfigureAwait(false);
        var text = StrictUtf8.GetString(content);
        if (!text.Contains(expected.StringValue, StringComparison.Ordinal))
        {
            throw new HandlerInputException();
        }

        return new HandlerEffect([], Digest(content));
    }
}

internal abstract class FileValidationHandlerBase(string id) : FileExecutionHandlerBase(id)
{
    protected sealed override bool HandlesValidators => true;

    protected sealed override Task<HandlerEffect> ExecuteCoreAsync(
        HandlerExecutionContext context,
        CancellationToken cancellationToken) => ValidateAsync(context, cancellationToken);

    protected sealed override Task<HandlerEffect> CheckPostconditionsCoreAsync(
        HandlerExecutionContext context,
        CancellationToken cancellationToken) => ValidateAsync(context, cancellationToken);

    protected abstract Task<HandlerEffect> ValidateAsync(
        HandlerExecutionContext context,
        CancellationToken cancellationToken);
}
