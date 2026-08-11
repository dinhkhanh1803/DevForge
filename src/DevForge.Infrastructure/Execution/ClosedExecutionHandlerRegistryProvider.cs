using System.Collections.Immutable;
using DevForge.Application.Contracts;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Diagnostics;
using DevForge.Domain.Privacy;

namespace DevForge.Infrastructure.Execution;

internal sealed class ClosedExecutionHandlerRegistryProvider : IExecutionHandlerRegistryProvider
{
    private readonly ImmutableArray<IExecutionHandler?> _handlers;

    public ClosedExecutionHandlerRegistryProvider(IEnumerable<IExecutionHandler?>? handlers)
    {
        _handlers = handlers?.ToImmutableArray() ?? [];
    }

    public ExecutionOperationResult<IExecutionHandlerRegistry> Create(BlueprintTrust trust)
    {
        var selected = trust == BlueprintTrust.TrustedLocal
            ? _handlers.Where(handler => handler?.Id != "finalize-workspace")
            : _handlers;
        var result = ClosedExecutionHandlerRegistry.Create(trust, selected);
        if (result.IsValid)
        {
            return ExecutionOperationResult.Success<IExecutionHandlerRegistry>(result.Value);
        }

        var detail = RedactedText.FromTrustedRedaction(
            "The closed execution handler set did not match the reopened blueprint trust.");
        var error = DevForgeError.Create(
            "DF-EXEC-001",
            "The trusted execution handler set is unavailable.",
            detail.Value,
            "execution-handler",
            null,
            false,
            ["Verify the application execution composition."],
            []);
        return ExecutionOperationResult.Failure<IExecutionHandlerRegistry>(error.Value);
    }
}
