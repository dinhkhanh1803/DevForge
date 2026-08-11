using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using DevForge.Application.Contracts;
using DevForge.Domain.Diagnostics;
using DevForge.Domain.Execution;
using DevForge.Domain.Privacy;

namespace DevForge.Infrastructure.Execution;

internal abstract class FileExecutionHandlerBase(string id) : IExecutionHandler
{
    internal const int MaximumFileBytes = 4 * 1024 * 1024;
    internal static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public string Id { get; } = id;

    public ExecutionResumeBehavior ResumeBehavior => ExecutionResumeBehavior.RevalidatePostcondition;

    public Task<ExecutionHandlerResult> PrepareAsync(
        ExecutionHandlerRequest request,
        CancellationToken cancellationToken) => RunPhaseAsync(
            request,
            ExecutionPhase.Prepare,
            (_, _) => Task.FromResult(HandlerEffect.Empty),
            cancellationToken);

    public Task<ExecutionHandlerResult> CheckPreconditionsAsync(
        ExecutionHandlerRequest request,
        CancellationToken cancellationToken) => RunPhaseAsync(
            request,
            ExecutionPhase.Precondition,
            CheckPreconditionsCoreAsync,
            cancellationToken);

    public Task<ExecutionHandlerResult> ExecuteAsync(
        ExecutionHandlerRequest request,
        IProgress<ExecutionProgressLine>? progress,
        CancellationToken cancellationToken) => RunPhaseAsync(
            request,
            ExecutionPhase.Execute,
            ExecuteCoreAsync,
            cancellationToken);

    public Task<ExecutionHandlerResult> CheckPostconditionsAsync(
        ExecutionHandlerRequest request,
        CancellationToken cancellationToken) => RunPhaseAsync(
            request,
            ExecutionPhase.Postcondition,
            CheckPostconditionsCoreAsync,
            cancellationToken);

    public Task<ExecutionHandlerResult> CleanupForRetryAsync(
        ExecutionHandlerRequest request,
        CancellationToken cancellationToken) => RunPhaseAsync(
            request,
            ExecutionPhase.Prepare,
            CleanupForRetryCoreAsync,
            cancellationToken);

    protected virtual Task<HandlerEffect> CheckPreconditionsCoreAsync(
        HandlerExecutionContext context,
        CancellationToken cancellationToken) => Task.FromResult(HandlerEffect.Empty);

    protected abstract Task<HandlerEffect> ExecuteCoreAsync(
        HandlerExecutionContext context,
        CancellationToken cancellationToken);

    protected abstract Task<HandlerEffect> CheckPostconditionsCoreAsync(
        HandlerExecutionContext context,
        CancellationToken cancellationToken);

    protected virtual Task<HandlerEffect> CleanupForRetryCoreAsync(
        HandlerExecutionContext context,
        CancellationToken cancellationToken) => Task.FromResult(HandlerEffect.Empty);

    protected abstract ImmutableHashSet<string> RequiredInputNames { get; }

    protected static WorkspaceRelativePath PathInput(
        HandlerExecutionContext context,
        string name)
    {
        if (!context.Inputs.TryGetValue(name, out var value)
            || value.Kind != PlanValueKind.Text)
        {
            throw new HandlerInputException();
        }

        var path = WorkspaceRelativePath.Create(value.StringValue);
        if (!path.IsValid || !IsSafePath(path.Value))
        {
            throw new HandlerInputException();
        }

        return path.Value;
    }

    protected static PlanValue ValueInput(HandlerExecutionContext context, string name)
    {
        return context.Inputs.TryGetValue(name, out var value)
            ? value
            : throw new HandlerInputException();
    }

    protected static async Task<byte[]> ReadBoundedAsync(
        IWorkspaceFileSystem workspace,
        WorkspaceRelativePath path,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = await workspace.OpenReadAsync(path, cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream(Math.Min(maximumBytes, 64 * 1024));
        var buffer = new byte[16 * 1024];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return output.ToArray();
            }

            if (output.Length + read > maximumBytes)
            {
                throw new HandlerInputException();
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    protected static async Task EnsureParentDirectoryAsync(
        IWorkspaceFileSystem workspace,
        WorkspaceRelativePath path,
        CancellationToken cancellationToken)
    {
        var separator = path.Value.LastIndexOf('\\');
        if (separator > 0)
        {
            var parent = WorkspaceRelativePath.Create(path.Value[..separator]);
            if (!parent.IsValid)
            {
                throw new HandlerInputException();
            }

            await workspace.CreateDirectoryAsync(parent.Value, cancellationToken).ConfigureAwait(false);
        }
    }

    protected static Task WriteAtomicAsync(
        IWorkspaceFileSystem workspace,
        WorkspaceRelativePath path,
        ReadOnlyMemory<byte> content,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        return workspace is IAtomicFileWorkspaceFileSystem atomic
            ? atomic.WriteFileAtomicallyAsync(path, content, overwrite, cancellationToken)
            : throw new HandlerInputException();
    }

    protected static string Digest(ReadOnlySpan<byte> content) =>
        $"sha256:{Convert.ToHexStringLower(SHA256.HashData(content))}";

    protected static HandlerEffect Effect(
        IEnumerable<WorkspaceRelativePath> paths,
        ReadOnlySpan<byte> content) => new([.. paths], Digest(content));

    protected static HandlerEffect Effect(params WorkspaceRelativePath[] paths) =>
        new([.. paths], Digest([]));

    protected static bool IsSafePath(WorkspaceRelativePath path)
    {
        return !path.Value.Split('\\').Any(segment =>
            segment.Equals(".env", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<ExecutionHandlerResult> RunPhaseAsync(
        ExecutionHandlerRequest request,
        ExecutionPhase phase,
        Func<HandlerExecutionContext, CancellationToken, Task<HandlerEffect>> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!StringComparer.Ordinal.Equals(request.HandlerId, Id) || request.IsValidator)
            {
                throw new HandlerInputException();
            }

            var runtime = RuntimePlanValueContext.Create(
                request.RunId,
                request.Staging.PayloadWorkspace.Root,
                null,
                RuntimeValueAvailability.PreFinalization,
                request.BlueprintPackage.Blueprint.Manifest.Trust);
            if (!runtime.IsValid)
            {
                throw new HandlerInputException();
            }

            var materialized = RuntimePlanValueMaterializer.Materialize(
                request.Inputs.Select(item =>
                    KeyValuePair.Create<string, PlanValue?>(item.Key, item.Value)),
                runtime.Value,
                cancellationToken);
            if (!materialized.IsValid
                || !RequiredInputNames.SetEquals(materialized.Value.Keys))
            {
                throw new HandlerInputException();
            }

            var context = new HandlerExecutionContext(request, materialized.Value);
            var effect = await action(context, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return ExecutionHandlerResult.Create(
                phase,
                ExecutionHandlerOutcome.Succeeded,
                null,
                effect.OutputDigest,
                null,
                effect.AffectedPaths).Value;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            return Failure(phase, IsTransientFailure(exception));
        }
    }

    private static bool IsExpectedFailure(Exception exception)
    {
        return exception is HandlerInputException
            or InfrastructureOperationException
            or InvalidDataException
            or IOException
            or DecoderFallbackException
            or ArgumentException
            or FormatException
            or System.Xml.XmlException
            or System.Text.Json.JsonException
            or YamlDotNet.Core.YamlException;
    }

    private static bool IsTransientFailure(Exception exception) =>
        exception is IOException
        || exception is InfrastructureOperationException { Code: "DF-FS-002" };

    private static ExecutionHandlerResult Failure(ExecutionPhase phase, bool isRetryable)
    {
        var detail = RedactedText.FromTrustedRedaction(
            "A guarded file handler rejected its input, source content, workspace state, or postcondition.");
        var error = DevForgeError.Create(
            "DF-EXEC-001",
            "A project file operation could not be completed safely.",
            detail.Value,
            "execution-handler",
            null,
            isRetryable,
            ["Review the blueprint action and retry from the owned staging workspace."],
            []);
        return ExecutionHandlerResult.Create(
            phase,
            ExecutionHandlerOutcome.Failed,
            null,
            null,
            error.Value,
            []).Value;
    }

    protected sealed record HandlerExecutionContext(
        ExecutionHandlerRequest Request,
        ImmutableDictionary<string, PlanValue> Inputs)
    {
        public IWorkspaceFileSystem Payload => Request.Staging.PayloadWorkspace;

        public IWorkspaceFileSystem Package => Request.BlueprintPackage.PackageWorkspace;
    }

    protected sealed record HandlerEffect(
        ImmutableArray<WorkspaceRelativePath> AffectedPaths,
        string? OutputDigest)
    {
        public static HandlerEffect Empty { get; } = new([], null);
    }

    protected sealed class HandlerInputException : Exception;
}

internal sealed record StructuredPatchOperation(
    string Operation,
    ImmutableArray<string> Segments,
    PlanValue? Value)
{
    internal static ImmutableArray<StructuredPatchOperation> Parse(PlanValue value)
    {
        if (value.Kind != PlanValueKind.Sequence || value.ArrayValue.Length > 256)
        {
            throw new InvalidDataException();
        }

        return [.. value.ArrayValue.Select(ParseOne)];
    }

    private static StructuredPatchOperation ParseOne(PlanValue value)
    {
        if (value.Kind != PlanValueKind.Map
            || !value.ObjectValue.TryGetValue("op", out var operation)
            || operation.Kind != PlanValueKind.Text
            || !value.ObjectValue.TryGetValue("path", out var path)
            || path.Kind != PlanValueKind.Text)
        {
            throw new InvalidDataException();
        }

        var isSet = operation.StringValue == "set";
        var isRemove = operation.StringValue == "remove";
        if (!isSet && !isRemove
            || isSet && (value.ObjectValue.Count != 3
                || !value.ObjectValue.TryGetValue("value", out _))
            || isRemove && value.ObjectValue.Count != 2)
        {
            throw new InvalidDataException();
        }

        var segments = ParsePath(path.StringValue!);
        return new StructuredPatchOperation(
            operation.StringValue!,
            segments,
            isSet ? value.ObjectValue["value"] : null);
    }

    private static ImmutableArray<string> ParsePath(string path)
    {
        if (path.Length is 0 or > 4096 || path[0] != '/')
        {
            throw new InvalidDataException();
        }

        var raw = path[1..].Split('/');
        if (raw.Length is 0 or > 64 || raw.Any(segment => segment.Length is 0 or > 256))
        {
            throw new InvalidDataException();
        }

        return [.. raw.Select(DecodeSegment)];
    }

    private static string DecodeSegment(string segment)
    {
        var output = new StringBuilder(segment.Length);
        for (var index = 0; index < segment.Length; index++)
        {
            if (segment[index] != '~')
            {
                output.Append(segment[index]);
                continue;
            }

            if (++index >= segment.Length)
            {
                throw new InvalidDataException();
            }

            output.Append(segment[index] switch
            {
                '0' => '~',
                '1' => '/',
                _ => throw new InvalidDataException(),
            });
        }

        var value = output.ToString();
        var privacyName = value.TrimStart('@');
        return value is "." or ".."
            || value.Contains('\0')
            || string.IsNullOrEmpty(privacyName)
            || RedactedText.IsSecretShapedKey(privacyName)
            ? throw new InvalidDataException()
            : value;
    }
}
