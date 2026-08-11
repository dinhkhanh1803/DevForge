using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using DevForge.Application.Contracts;
using DevForge.Infrastructure.Templates;

namespace DevForge.Infrastructure.Execution;

internal sealed class CreateDirectoryExecutionHandler() :
    FileExecutionHandlerBase("create-directory")
{
    private static readonly ImmutableHashSet<string> _inputs =
        ImmutableHashSet.Create(StringComparer.Ordinal, "path");

    protected override ImmutableHashSet<string> RequiredInputNames => _inputs;

    protected override async Task<HandlerEffect> ExecuteCoreAsync(
        HandlerExecutionContext context,
        CancellationToken cancellationToken)
    {
        var path = PathInput(context, "path");
        await context.Payload.CreateDirectoryAsync(path, cancellationToken).ConfigureAwait(false);
        return Effect(path);
    }

    protected override async Task<HandlerEffect> CheckPostconditionsCoreAsync(
        HandlerExecutionContext context,
        CancellationToken cancellationToken)
    {
        var path = PathInput(context, "path");
        return await context.Payload.DirectoryExistsAsync(path, cancellationToken).ConfigureAwait(false)
            ? Effect(path)
            : throw new HandlerInputException();
    }
}

internal sealed class RenderTemplateExecutionHandler(ITemplateRenderer renderer) :
    FileExecutionHandlerBase("render-template")
{
    private static readonly ImmutableHashSet<string> _inputs =
        ImmutableHashSet.Create(StringComparer.Ordinal, "source", "target");
    private readonly ITemplateRenderer _renderer = renderer
        ?? throw new ArgumentNullException(nameof(renderer));

    protected override ImmutableHashSet<string> RequiredInputNames => _inputs;

    protected override async Task<HandlerEffect> CheckPreconditionsCoreAsync(
        HandlerExecutionContext context,
        CancellationToken cancellationToken)
    {
        var source = PathInput(context, "source");
        return await context.Package.FileExistsAsync(source, cancellationToken).ConfigureAwait(false)
            ? HandlerEffect.Empty
            : throw new HandlerInputException();
    }

    protected override async Task<HandlerEffect> ExecuteCoreAsync(
        HandlerExecutionContext context,
        CancellationToken cancellationToken)
    {
        var target = PathInput(context, "target");
        var bytes = await RenderAsync(context, cancellationToken).ConfigureAwait(false);
        await EnsureParentDirectoryAsync(context.Payload, target, cancellationToken).ConfigureAwait(false);
        await WriteAtomicAsync(
            context.Payload,
            target,
            bytes,
            overwrite: true,
            cancellationToken).ConfigureAwait(false);
        return Effect([target], bytes);
    }

    protected override async Task<HandlerEffect> CheckPostconditionsCoreAsync(
        HandlerExecutionContext context,
        CancellationToken cancellationToken)
    {
        var target = PathInput(context, "target");
        var expected = await RenderAsync(context, cancellationToken).ConfigureAwait(false);
        var actual = await ReadBoundedAsync(
            context.Payload,
            target,
            RestrictedScribanTemplateRenderer.MaximumOutputLength * 4,
            cancellationToken).ConfigureAwait(false);
        return CryptographicOperations.FixedTimeEquals(expected, actual)
            ? Effect([target], actual)
            : throw new HandlerInputException();
    }

    protected override async Task<HandlerEffect> CleanupForRetryCoreAsync(
        HandlerExecutionContext context,
        CancellationToken cancellationToken)
    {
        var target = PathInput(context, "target");
        if (await context.Payload.FileExistsAsync(target, cancellationToken).ConfigureAwait(false))
        {
            await context.Payload.DeleteFileAsync(target, cancellationToken).ConfigureAwait(false);
        }

        return HandlerEffect.Empty;
    }

    private async Task<byte[]> RenderAsync(
        HandlerExecutionContext context,
        CancellationToken cancellationToken)
    {
        var source = PathInput(context, "source");
        var sourceBytes = await ReadBoundedAsync(
            context.Package,
            source,
            TemplateRenderRequest.MaxTemplateLength + 1,
            cancellationToken).ConfigureAwait(false);
        var template = StrictUtf8.GetString(sourceBytes);
        var renderRequest = TemplateRenderRequest.Create(
            template,
            context.Request.TemplateContext.Select(item =>
                KeyValuePair.Create<string, string?>(item.Key, item.Value)));
        if (!renderRequest.IsValid)
        {
            throw new HandlerInputException();
        }

        var rendered = await _renderer.RenderAsync(renderRequest.Value, cancellationToken).ConfigureAwait(false);
        return StrictUtf8.GetBytes(rendered);
    }
}

internal sealed class CopyOverlayExecutionHandler() :
    FileExecutionHandlerBase("copy-overlay")
{
    private const int MaximumOverlayFiles = 2048;
    private const int MaximumOverlayBytes = 32 * 1024 * 1024;
    private static readonly ImmutableHashSet<string> _inputs =
        ImmutableHashSet.Create(StringComparer.Ordinal, "source", "target");

    protected override ImmutableHashSet<string> RequiredInputNames => _inputs;

    protected override async Task<HandlerEffect> CheckPreconditionsCoreAsync(
        HandlerExecutionContext context,
        CancellationToken cancellationToken)
    {
        var source = PathInput(context, "source");
        return await context.Package.DirectoryExistsAsync(source, cancellationToken).ConfigureAwait(false)
            ? HandlerEffect.Empty
            : throw new HandlerInputException();
    }

    protected override async Task<HandlerEffect> ExecuteCoreAsync(
        HandlerExecutionContext context,
        CancellationToken cancellationToken)
    {
        var files = await MapFilesAsync(context, cancellationToken).ConfigureAwait(false);
        var affected = ImmutableArray.CreateBuilder<WorkspaceRelativePath>(files.Length);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long totalBytes = 0;
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = MaximumOverlayBytes - totalBytes;
            if (remaining <= 0)
            {
                throw new HandlerInputException();
            }

            var content = await ReadBoundedAsync(
                context.Package,
                file.Source,
                checked((int)Math.Min(MaximumFileBytes, remaining)),
                cancellationToken).ConfigureAwait(false);
            totalBytes += content.Length;
            await EnsureParentDirectoryAsync(context.Payload, file.Target, cancellationToken).ConfigureAwait(false);
            await WriteAtomicAsync(
                context.Payload,
                file.Target,
                content,
                overwrite: true,
                cancellationToken).ConfigureAwait(false);
            affected.Add(file.Target);
            AppendDigest(digest, file.Target, content);
        }

        return new HandlerEffect(
            affected.ToImmutable(),
            $"sha256:{Convert.ToHexStringLower(digest.GetHashAndReset())}");
    }

    protected override async Task<HandlerEffect> CheckPostconditionsCoreAsync(
        HandlerExecutionContext context,
        CancellationToken cancellationToken)
    {
        var files = await MapFilesAsync(context, cancellationToken).ConfigureAwait(false);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in files)
        {
            var source = await ReadBoundedAsync(
                context.Package,
                file.Source,
                MaximumFileBytes,
                cancellationToken).ConfigureAwait(false);
            var target = await ReadBoundedAsync(
                context.Payload,
                file.Target,
                MaximumFileBytes,
                cancellationToken).ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(source, target))
            {
                throw new HandlerInputException();
            }

            AppendDigest(digest, file.Target, target);
        }

        return new HandlerEffect(
            [.. files.Select(file => file.Target)],
            $"sha256:{Convert.ToHexStringLower(digest.GetHashAndReset())}");
    }

    protected override async Task<HandlerEffect> CleanupForRetryCoreAsync(
        HandlerExecutionContext context,
        CancellationToken cancellationToken)
    {
        var files = await MapFilesAsync(context, cancellationToken).ConfigureAwait(false);
        foreach (var file in files.Reverse())
        {
            if (await context.Payload.FileExistsAsync(file.Target, cancellationToken).ConfigureAwait(false))
            {
                await context.Payload.DeleteFileAsync(file.Target, cancellationToken).ConfigureAwait(false);
            }
        }

        return HandlerEffect.Empty;
    }

    private static async Task<ImmutableArray<OverlayFile>> MapFilesAsync(
        HandlerExecutionContext context,
        CancellationToken cancellationToken)
    {
        var source = PathInput(context, "source");
        var target = PathInput(context, "target");
        var files = await context.Package.EnumerateFilesAsync(
            source,
            recursive: true,
            cancellationToken).ConfigureAwait(false);
        if (files.IsEmpty || files.Length > MaximumOverlayFiles)
        {
            throw new HandlerInputException();
        }

        var prefix = source.Value + '\\';
        var mapped = ImmutableArray.CreateBuilder<OverlayFile>(files.Length);
        foreach (var file in files)
        {
            if (!file.Value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new HandlerInputException();
            }

            var targetPath = WorkspaceRelativePath.Create(
                target.Value + '\\' + file.Value[prefix.Length..]);
            if (!targetPath.IsValid)
            {
                throw new HandlerInputException();
            }

            if (!IsSafePath(targetPath.Value))
            {
                throw new HandlerInputException();
            }

            mapped.Add(new OverlayFile(file, targetPath.Value));
        }

        return mapped
            .OrderBy(file => file.Source.Value, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static void AppendDigest(
        IncrementalHash digest,
        WorkspaceRelativePath path,
        ReadOnlySpan<byte> content)
    {
        var pathBytes = StrictUtf8.GetBytes(path.Value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, pathBytes.Length);
        digest.AppendData(length);
        digest.AppendData(pathBytes);
        BinaryPrimitives.WriteInt32LittleEndian(length, content.Length);
        digest.AppendData(length);
        digest.AppendData(content);
    }

    private sealed record OverlayFile(
        WorkspaceRelativePath Source,
        WorkspaceRelativePath Target);
}
