using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using DevForge.Application.Contracts;
using DevForge.Domain.Execution;

namespace DevForge.Infrastructure.Execution;

internal sealed class JsonPatchExecutionHandler() : FileExecutionHandlerBase("patch-json")
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
        using (var document = JsonDocument.Parse(content, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 64,
        }))
        {
            RejectDuplicateProperties(document.RootElement, cancellationToken);
        }

        var root = JsonNode.Parse(
            content.Span,
            nodeOptions: null,
            new JsonDocumentOptions { MaxDepth = 64 }) as JsonObject
            ?? throw new InvalidDataException();
        foreach (var operation in operations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Apply(root, operation);
        }

        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output, new JsonWriterOptions
        {
            Indented = false,
            SkipValidation = false,
        }))
        {
            WriteCanonical(writer, root, cancellationToken);
        }

        var result = output.ToArray();
        if (result.Length > MaximumFileBytes)
        {
            throw new InvalidDataException();
        }

        using var verification = JsonDocument.Parse(result, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 64,
        });
        RejectDuplicateProperties(verification.RootElement, cancellationToken);
        return result;
    }

    private static void Apply(JsonObject root, StructuredPatchOperation operation)
    {
        var parent = root;
        foreach (var segment in operation.Segments[..^1])
        {
            if (parent.TryGetPropertyValue(segment, out var child))
            {
                parent = child as JsonObject ?? throw new InvalidDataException();
            }
            else if (operation.Operation == "set")
            {
                var created = new JsonObject();
                parent.Add(segment, created);
                parent = created;
            }
            else
            {
                return;
            }
        }

        var leaf = operation.Segments[^1];
        if (operation.Operation == "remove")
        {
            parent.Remove(leaf);
        }
        else
        {
            parent[leaf] = ToJsonNode(operation.Value!);
        }
    }

    private static JsonNode? ToJsonNode(PlanValue value)
    {
        return value.Kind switch
        {
            PlanValueKind.Text => JsonValue.Create(value.StringValue),
            PlanValueKind.Boolean => JsonValue.Create(value.BooleanValue),
            PlanValueKind.WholeNumber => JsonValue.Create(value.IntegerValue),
            PlanValueKind.Sequence => new JsonArray([.. value.ArrayValue.Select(ToJsonNode)]),
            PlanValueKind.Map => new JsonObject(value.ObjectValue
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => KeyValuePair.Create(item.Key, ToJsonNode(item.Value)))),
            _ => throw new InvalidDataException(),
        };
    }

    private static void WriteCanonical(
        Utf8JsonWriter writer,
        JsonNode? node,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        switch (node)
        {
            case null:
                writer.WriteNullValue();
                break;
            case JsonObject value:
                writer.WriteStartObject();
                foreach (var property in value.OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Key);
                    WriteCanonical(writer, property.Value, cancellationToken);
                }

                writer.WriteEndObject();
                break;
            case JsonArray value:
                writer.WriteStartArray();
                foreach (var item in value)
                {
                    WriteCanonical(writer, item, cancellationToken);
                }

                writer.WriteEndArray();
                break;
            default:
                node.WriteTo(writer);
                break;
        }
    }

    private static void RejectDuplicateProperties(
        JsonElement element,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new InvalidDataException();
                }

                RejectDuplicateProperties(property.Value, cancellationToken);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                RejectDuplicateProperties(item, cancellationToken);
            }
        }
    }
}
