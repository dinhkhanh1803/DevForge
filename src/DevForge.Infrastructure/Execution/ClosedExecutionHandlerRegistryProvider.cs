using System.Collections.Immutable;
using DevForge.Application.Contracts;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Diagnostics;
using DevForge.Domain.Privacy;
using DevForge.Infrastructure.Templates;

namespace DevForge.Infrastructure.Execution;

public sealed class ClosedExecutionHandlerRegistryProvider : IExecutionHandlerRegistryProvider
{
    private readonly ImmutableArray<IExecutionHandler?> _handlers;

    internal ClosedExecutionHandlerRegistryProvider(IEnumerable<IExecutionHandler?>? handlers)
    {
        _handlers = handlers?.ToImmutableArray() ?? [];
    }

    public ClosedExecutionHandlerRegistryProvider(
        ITemplateRenderer renderer,
        IProcessRunner runner)
        : this(CreateProductionHandlers(renderer, runner))
    {
    }

    public ClosedExecutionHandlerRegistryProvider(IProcessRunner runner)
        : this(new RestrictedScribanTemplateRenderer(), runner)
    {
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

    private static IEnumerable<IExecutionHandler> CreateProductionHandlers(
        ITemplateRenderer renderer,
        IProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(runner);
        return
        [
            new CreateDirectoryExecutionHandler(),
            new RenderTemplateExecutionHandler(renderer),
            new CopyOverlayExecutionHandler(),
            new JsonPatchExecutionHandler(),
            new YamlPatchExecutionHandler(),
            new XmlPatchExecutionHandler(),
            new RunProcessExecutionHandler(runner),
            new PackageInstallExecutionHandler(runner),
            new ValidateCommandExecutionHandler(runner),
            new FinalizationBoundaryHandler(),
        ];
    }

    private sealed class FinalizationBoundaryHandler : IExecutionHandler
    {
        public string Id => "finalize-workspace";

        public ExecutionResumeBehavior ResumeBehavior => ExecutionResumeBehavior.RevalidatePostcondition;

        public Task<ExecutionHandlerResult> PrepareAsync(
            ExecutionHandlerRequest request,
            CancellationToken cancellationToken) => BoundaryAsync(ExecutionPhase.Prepare, cancellationToken);

        public Task<ExecutionHandlerResult> CheckPreconditionsAsync(
            ExecutionHandlerRequest request,
            CancellationToken cancellationToken) => BoundaryAsync(ExecutionPhase.Precondition, cancellationToken);

        public Task<ExecutionHandlerResult> ExecuteAsync(
            ExecutionHandlerRequest request,
            IProgress<ExecutionProgressLine>? progress,
            CancellationToken cancellationToken) => BoundaryAsync(ExecutionPhase.Execute, cancellationToken);

        public Task<ExecutionHandlerResult> CheckPostconditionsAsync(
            ExecutionHandlerRequest request,
            CancellationToken cancellationToken) => BoundaryAsync(ExecutionPhase.Postcondition, cancellationToken);

        public Task<ExecutionHandlerResult> CleanupForRetryAsync(
            ExecutionHandlerRequest request,
            CancellationToken cancellationToken) => BoundaryAsync(ExecutionPhase.Prepare, cancellationToken);

        private static Task<ExecutionHandlerResult> BoundaryAsync(
            ExecutionPhase phase,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var detail = RedactedText.FromTrustedRedaction(
                "Workspace finalization must be coordinated by the durable completion boundary.");
            var error = DevForgeError.Create(
                "DF-EXEC-001",
                "The finalization boundary cannot be dispatched as an ordinary handler.",
                detail.Value,
                "finalize-workspace",
                null,
                false,
                ["Resume through the durable completion coordinator."],
                []);
            return Task.FromResult(ExecutionHandlerResult.Create(
                phase,
                ExecutionHandlerOutcome.Failed,
                null,
                null,
                error.Value,
                []).Value);
        }
    }
}
