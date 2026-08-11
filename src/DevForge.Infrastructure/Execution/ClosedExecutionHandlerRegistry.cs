using System.Collections.Immutable;
using DevForge.Application.Contracts;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Diagnostics;
using DevForge.Domain.Privacy;
using DevForge.Domain.Validation;

namespace DevForge.Infrastructure.Execution;

internal sealed class ClosedExecutionHandlerRegistry : IExecutionHandlerRegistry
{
    private static readonly ImmutableHashSet<string> _operationalHandlerIds =
        new[]
        {
            "create-directory",
            "render-template",
            "copy-overlay",
            "patch-json",
            "patch-yaml",
            "patch-xml",
            "run-process",
            "package-install",
            "validate-command",
        }.ToImmutableHashSet(StringComparer.Ordinal);

    private const string FinalizationHandlerId = "finalize-workspace";

    private readonly ImmutableDictionary<string, IExecutionHandler> _handlers;

    private ClosedExecutionHandlerRegistry(
        ImmutableDictionary<string, IExecutionHandler> handlers)
    {
        _handlers = handlers;
    }

    internal static ValidationResult<ClosedExecutionHandlerRegistry> Create(
        BlueprintTrust executionTrust,
        IEnumerable<IExecutionHandler?>? handlers)
    {
        var snapshot = handlers?.ToImmutableArray() ?? [];
        var issues = new List<ValidationIssue>();
        if (handlers is null)
        {
            issues.Add(Issue());
        }

        if (executionTrust is not (BlueprintTrust.BuiltIn or BlueprintTrust.TrustedLocal))
        {
            issues.Add(Issue());
        }

        var requiredIds = executionTrust == BlueprintTrust.BuiltIn
            ? _operationalHandlerIds.Add(FinalizationHandlerId)
            : _operationalHandlerIds;
        var registered = ImmutableDictionary.CreateBuilder<string, IExecutionHandler>(
            StringComparer.Ordinal);
        foreach (var handler in snapshot)
        {
            if (handler is null
                || !requiredIds.Contains(handler.Id)
                || !registered.TryAdd(handler.Id, handler))
            {
                issues.Add(Issue());
            }
        }

        if (!requiredIds.SetEquals(registered.Keys))
        {
            issues.Add(Issue());
        }

        if (issues.Count != 0)
        {
            return ValidationResult.Failure<ClosedExecutionHandlerRegistry>(issues);
        }

        if (executionTrust == BlueprintTrust.BuiltIn)
        {
            registered.Add("git-operation", new DeferredExecutionHandler("git-operation"));
            registered.Add("github-operation", new DeferredExecutionHandler("github-operation"));
        }

        return ValidationResult.Success(new ClosedExecutionHandlerRegistry(registered.ToImmutable()));
    }

    public IExecutionHandler? Resolve(string handlerId)
    {
        return !string.IsNullOrWhiteSpace(handlerId)
            && handlerId == handlerId.Trim()
            && _handlers.TryGetValue(handlerId, out var handler)
                ? handler
                : null;
    }

    private static ValidationIssue Issue()
    {
        return new ValidationIssue(
            "DF-EXEC-001",
            "The closed execution handler registration is incomplete or invalid.",
            "handlers");
    }

    private sealed class DeferredExecutionHandler(string id) : IExecutionHandler
    {
        public string Id { get; } = id;

        public Task<ExecutionHandlerResult> PrepareAsync(
            ExecutionHandlerRequest request,
            CancellationToken cancellationToken) => ResultAsync(ExecutionPhase.Prepare, cancellationToken);

        public Task<ExecutionHandlerResult> CheckPreconditionsAsync(
            ExecutionHandlerRequest request,
            CancellationToken cancellationToken) => ResultAsync(ExecutionPhase.Precondition, cancellationToken);

        public Task<ExecutionHandlerResult> ExecuteAsync(
            ExecutionHandlerRequest request,
            IProgress<ExecutionProgressLine>? progress,
            CancellationToken cancellationToken) => ResultAsync(ExecutionPhase.Execute, cancellationToken);

        public Task<ExecutionHandlerResult> CheckPostconditionsAsync(
            ExecutionHandlerRequest request,
            CancellationToken cancellationToken) => ResultAsync(ExecutionPhase.Postcondition, cancellationToken);

        public Task<ExecutionHandlerResult> CleanupForRetryAsync(
            ExecutionHandlerRequest request,
            CancellationToken cancellationToken) => ResultAsync(ExecutionPhase.Prepare, cancellationToken);

        private static Task<ExecutionHandlerResult> ResultAsync(
            ExecutionPhase phase,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var detail = RedactedText.FromTrustedRedaction(
                "The requested built-in integration is deferred beyond the current execution milestone.");
            var error = DevForgeError.Create(
                "DF-EXEC-001",
                "This execution handler is not available in the current milestone.",
                detail.Value,
                "execution-handler",
                null,
                isRetryable: false,
                ["Complete the project locally before using deferred integrations."],
                []);
            var result = ExecutionHandlerResult.Create(
                phase,
                ExecutionHandlerOutcome.Failed,
                null,
                null,
                error.Value,
                []);
            return Task.FromResult(result.Value);
        }
    }
}
