using System.Collections.Immutable;
using System.Globalization;
using DevForge.Application.Contracts;
using DevForge.Domain.Execution;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace DevForge.Infrastructure.Execution;

internal sealed class YamlPatchExecutionHandler() : FileExecutionHandlerBase("patch-yaml")
{
    private static readonly ImmutableHashSet<string> _inputs =
        ImmutableHashSet.Create(StringComparer.Ordinal, "target", "operations");

    protected override ImmutableHashSet<string> RequiredInputNames => _inputs;

    protected override async Task<HandlerEffect> CheckPreconditionsCoreAsync(
        HandlerExecutionContext context,
        CancellationToken cancellationToken)
    {
        var target = PathInput(context, "target");
        return await context.Payload.FileExistsAsync(target, cancellationToken).ConfigureAwait(false)
            ? HandlerEffect.Empty
            : throw new HandlerInputException();
    }

    protected override async Task<HandlerEffect> ExecuteCoreAsync(
        HandlerExecutionContext context,
        CancellationToken cancellationToken)
    {
        var target = PathInput(context, "target");
        var content = await CreatePatchedContentAsync(context, cancellationToken).ConfigureAwait(false);
        await WriteAtomicAsync(
            context.Payload,
            target,
            content,
            overwrite: true,
            cancellationToken).ConfigureAwait(false);
        return Effect([target], content);
    }

    protected override async Task<HandlerEffect> CheckPostconditionsCoreAsync(
        HandlerExecutionContext context,
        CancellationToken cancellationToken)
    {
        var target = PathInput(context, "target");
        var current = await ReadBoundedAsync(
            context.Payload,
            target,
            MaximumFileBytes,
            cancellationToken).ConfigureAwait(false);
        var expected = Patch(current, Operations(context), cancellationToken);
        return current.AsSpan().SequenceEqual(expected)
            ? Effect([target], current)
            : throw new HandlerInputException();
    }

    private static async Task<byte[]> CreatePatchedContentAsync(
        HandlerExecutionContext context,
        CancellationToken cancellationToken)
    {
        var target = PathInput(context, "target");
        var current = await ReadBoundedAsync(
            context.Payload,
            target,
            MaximumFileBytes,
            cancellationToken).ConfigureAwait(false);
        return Patch(current, Operations(context), cancellationToken);
    }

    private static ImmutableArray<StructuredPatchOperation> Operations(
        HandlerExecutionContext context) => StructuredPatchOperation.Parse(
            ValueInput(context, "operations"));

    private static byte[] Patch(
        ReadOnlyMemory<byte> content,
        ImmutableArray<StructuredPatchOperation> operations,
        CancellationToken cancellationToken)
    {
        var yaml = StrictUtf8.GetString(content.Span);
        ValidateEvents(yaml, cancellationToken);
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        if (stream.Documents.Count != 1
            || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            throw new InvalidDataException();
        }

        ValidateNode(root, 0, cancellationToken);
        foreach (var operation in operations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Apply(root, operation);
        }

        using var writer = new StringWriter(CultureInfo.InvariantCulture)
        {
            NewLine = "\n",
        };
        new YamlStream(new YamlDocument(root)).Save(writer, assignAnchors: false);
        var result = StrictUtf8.GetBytes(writer.ToString());
        if (result.Length > MaximumFileBytes)
        {
            throw new InvalidDataException();
        }

        VerifySerialized(result, cancellationToken);
        return result;
    }

    private static void VerifySerialized(
        ReadOnlySpan<byte> content,
        CancellationToken cancellationToken)
    {
        var yaml = StrictUtf8.GetString(content);
        ValidateEvents(yaml, cancellationToken);
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        if (stream.Documents.Count != 1
            || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            throw new InvalidDataException();
        }

        ValidateNode(root, 0, cancellationToken);
    }

    private static void ValidateEvents(string yaml, CancellationToken cancellationToken)
    {
        var parser = new Parser(new StringReader(yaml));
        var events = 0;
        while (parser.MoveNext())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++events > 100_000
                || parser.Current is AnchorAlias
                || parser.Current is NodeEvent node
                    && (!node.Anchor.IsEmpty || !node.Tag.IsEmpty))
            {
                throw new InvalidDataException();
            }
        }
    }

    private static void ValidateNode(
        YamlNode node,
        int depth,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (depth > 64 || !node.Anchor.IsEmpty || !node.Tag.IsEmpty)
        {
            throw new InvalidDataException();
        }

        switch (node)
        {
            case YamlMappingNode map:
                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in map.Children)
                {
                    if (item.Key is not YamlScalarNode { Value: not null } key
                        || key.Value == "<<"
                        || !keys.Add(key.Value))
                    {
                        throw new InvalidDataException();
                    }

                    ValidateNode(item.Value, depth + 1, cancellationToken);
                }

                break;
            case YamlSequenceNode sequence:
                foreach (var item in sequence.Children)
                {
                    ValidateNode(item, depth + 1, cancellationToken);
                }

                break;
            case YamlScalarNode:
                break;
            default:
                throw new InvalidDataException();
        }
    }

    private static void Apply(YamlMappingNode root, StructuredPatchOperation operation)
    {
        var parent = root;
        foreach (var segment in operation.Segments[..^1])
        {
            var key = FindKey(parent, segment);
            if (key is not null)
            {
                parent = parent.Children[key] as YamlMappingNode ?? throw new InvalidDataException();
            }
            else if (operation.Operation == "set")
            {
                var created = new YamlMappingNode();
                parent.Add(new YamlScalarNode(segment), created);
                parent = created;
            }
            else
            {
                return;
            }
        }

        var leaf = operation.Segments[^1];
        var existingKey = FindKey(parent, leaf);
        if (operation.Operation == "remove")
        {
            if (existingKey is not null)
            {
                parent.Children.Remove(existingKey);
            }
        }
        else if (existingKey is null)
        {
            parent.Add(new YamlScalarNode(leaf), ToYamlNode(operation.Value!));
        }
        else
        {
            parent.Children[existingKey] = ToYamlNode(operation.Value!);
        }
    }

    private static YamlNode? FindKey(YamlMappingNode map, string name)
    {
        return map.Children.Keys.SingleOrDefault(key =>
            key is YamlScalarNode scalar && StringComparer.Ordinal.Equals(scalar.Value, name));
    }

    private static YamlNode ToYamlNode(PlanValue value)
    {
        return value.Kind switch
        {
            PlanValueKind.Text => new YamlScalarNode(value.StringValue),
            PlanValueKind.Boolean => new YamlScalarNode(value.BooleanValue ? "true" : "false"),
            PlanValueKind.WholeNumber => new YamlScalarNode(
                value.IntegerValue.ToString(CultureInfo.InvariantCulture)),
            PlanValueKind.Sequence => new YamlSequenceNode(value.ArrayValue.Select(ToYamlNode)),
            PlanValueKind.Map => new YamlMappingNode(value.ObjectValue
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => KeyValuePair.Create<YamlNode, YamlNode>(
                    new YamlScalarNode(item.Key),
                    ToYamlNode(item.Value)))),
            _ => throw new InvalidDataException(),
        };
    }
}
