using System.Collections.Immutable;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using DevForge.Application.Contracts;
using DevForge.Domain.Execution;

namespace DevForge.Infrastructure.Execution;

internal sealed class XmlPatchExecutionHandler() : FileExecutionHandlerBase("patch-xml")
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
        var document = ParseAndValidate(content);
        var root = document.Root ?? throw new InvalidDataException();

        foreach (var operation in operations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Apply(root, operation);
        }

        using var output = new MemoryStream();
        var writerSettings = new XmlWriterSettings
        {
            Encoding = StrictUtf8,
            Indent = false,
            NewLineHandling = NewLineHandling.None,
            OmitXmlDeclaration = true,
        };
        using (var writer = XmlWriter.Create(output, writerSettings))
        {
            document.WriteTo(writer);
        }

        var result = output.ToArray();
        if (result.Length > MaximumFileBytes)
        {
            throw new InvalidDataException();
        }

        _ = ParseAndValidate(result);
        return result;
    }

    private static XDocument ParseAndValidate(ReadOnlyMemory<byte> content)
    {
        var settings = new XmlReaderSettings
        {
            Async = false,
            DtdProcessing = DtdProcessing.Prohibit,
            MaxCharactersFromEntities = 0,
            MaxCharactersInDocument = MaximumFileBytes,
            XmlResolver = null,
        };
        using var input = new MemoryStream(content.ToArray(), writable: false);
        using var reader = XmlReader.Create(input, settings);
        var document = XDocument.Load(reader, LoadOptions.None);
        if (document.DocumentType is not null
            || document.Root is null
            || document.Descendants().Any(element => !element.Name.NamespaceName.Equals(
                string.Empty,
                StringComparison.Ordinal)))
        {
            throw new InvalidDataException();
        }

        return document;
    }

    private static void Apply(XElement root, StructuredPatchOperation operation)
    {
        foreach (var segment in operation.Segments)
        {
            ValidateName(segment.TrimStart('@'));
        }

        if (!StringComparer.Ordinal.Equals(operation.Segments[0], root.Name.LocalName))
        {
            throw new InvalidDataException();
        }

        if (operation.Segments.Length < 2)
        {
            throw new InvalidDataException();
        }

        var current = root;
        foreach (var segment in operation.Segments[1..^1])
        {
            if (segment.StartsWith('@'))
            {
                throw new InvalidDataException();
            }

            current = FindOrCreateChild(current, segment, operation.Operation == "set");
            if (current is null)
            {
                return;
            }
        }

        var leaf = operation.Segments[^1];
        if (leaf.StartsWith('@'))
        {
            ApplyAttribute(current, leaf[1..], operation);
        }
        else
        {
            ApplyElement(current, leaf, operation);
        }
    }

    private static XElement? FindOrCreateChild(XElement parent, string name, bool create)
    {
        var matches = parent.Elements(name).Take(2).ToArray();
        if (matches.Length > 1)
        {
            throw new InvalidDataException();
        }

        if (matches.Length == 1)
        {
            return matches[0];
        }

        if (!create)
        {
            return null;
        }

        var child = new XElement(name);
        parent.Add(child);
        return child;
    }

    private static void ApplyAttribute(
        XElement parent,
        string name,
        StructuredPatchOperation operation)
    {
        if (operation.Operation == "remove")
        {
            parent.Attribute(name)?.Remove();
        }
        else
        {
            parent.SetAttributeValue(name, ScalarText(operation.Value!));
        }
    }

    private static void ApplyElement(
        XElement parent,
        string name,
        StructuredPatchOperation operation)
    {
        var existing = FindOrCreateChild(parent, name, operation.Operation == "set");
        if (existing is null)
        {
            return;
        }

        if (operation.Operation == "remove")
        {
            existing.Remove();
        }
        else
        {
            existing.RemoveNodes();
            existing.Value = ScalarText(operation.Value!);
        }
    }

    private static string ScalarText(PlanValue value)
    {
        return value.Kind switch
        {
            PlanValueKind.Text => value.StringValue!,
            PlanValueKind.Boolean => value.BooleanValue ? "true" : "false",
            PlanValueKind.WholeNumber => value.IntegerValue.ToString(CultureInfo.InvariantCulture),
            _ => throw new InvalidDataException(),
        };
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrEmpty(name)
            || name.Contains(':')
            || !StringComparer.Ordinal.Equals(XmlConvert.VerifyNCName(name), name))
        {
            throw new InvalidDataException();
        }
    }
}
