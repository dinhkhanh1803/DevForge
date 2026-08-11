using System.Collections.Immutable;
using DevForge.Application.Contracts;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace DevForge.Infrastructure.Blueprints;

internal enum BlueprintYamlDocumentKind
{
    Manifest = 1,
    Rules = 2,
}

internal sealed record BlueprintYamlDocument(BlueprintControlValue Root);

internal sealed class BlueprintYamlReader(BlueprintYamlDocumentKind kind)
    : IBlueprintControlReader<BlueprintYamlDocument>
{
    private static readonly ImmutableHashSet<string> _manifestFields = CreateSet(
        "id",
        "name",
        "version",
        "engineVersion",
        "tools",
        "features",
        "actions",
        "validators",
        "artifacts",
        "dependencies");

    private static readonly ImmutableDictionary<string, ImmutableHashSet<string>> _manifestItemFields =
        new Dictionary<string, ImmutableHashSet<string>>(StringComparer.Ordinal)
        {
            ["tools"] = CreateSet("id", "version", "required"),
            ["features"] = CreateSet("id", "defaultEnabled"),
            ["actions"] = CreateSet("id", "handler", "timeoutSeconds", "parameters"),
            ["validators"] = CreateSet("id", "handler", "timeoutSeconds", "parameters", "required"),
            ["artifacts"] = CreateSet("path"),
            ["dependencies"] = CreateSet("id", "version"),
        }.ToImmutableDictionary(StringComparer.Ordinal);

    private static readonly ImmutableHashSet<string> _ruleFields = CreateSet(
        "id",
        "condition",
        "severity",
        "message",
        "remediation",
        "override");

    public async ValueTask<BlueprintLoadResult<BlueprintYamlDocument>> ReadAsync(
        Stream content,
        CancellationToken cancellationToken)
    {
        var bounded = await BlueprintControlReadSupport
            .ReadTextAsync(content, cancellationToken)
            .ConfigureAwait(false);
        if (!bounded.IsValid)
        {
            return Failure(bounded.Issue!);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stream = new YamlStream();
            stream.Load(new StringReader(bounded.Text!));
            if (stream.Documents.Count != 1)
            {
                return Failure(BlueprintControlReadSupport.MalformedIssue());
            }

            var root = ConvertNode(stream.Documents[0].RootNode, 1, cancellationToken);
            if (!HasAllowedShape(root))
            {
                return Failure(BlueprintControlReadSupport.MalformedIssue());
            }

            return new BlueprintLoadResult<BlueprintYamlDocument>(
                new BlueprintYamlDocument(root),
                []);
        }
        catch (BlueprintControlBoundsException)
        {
            return Failure(BlueprintControlReadSupport.BoundsIssue());
        }
        catch (YamlException)
        {
            return Failure(BlueprintControlReadSupport.MalformedIssue());
        }
        catch (InvalidDataException)
        {
            return Failure(BlueprintControlReadSupport.MalformedIssue());
        }
        catch (ArgumentException)
        {
            return Failure(BlueprintControlReadSupport.MalformedIssue());
        }
        catch (InvalidOperationException)
        {
            return Failure(BlueprintControlReadSupport.MalformedIssue());
        }
    }

    private static BlueprintControlValue ConvertNode(
        YamlNode node,
        int depth,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (depth > BlueprintControlLimits.MaximumDepth)
        {
            throw new BlueprintControlBoundsException();
        }

        if (!node.Anchor.IsEmpty
            || !node.Tag.IsEmpty
            || node.NodeType == YamlNodeType.Alias)
        {
            throw new InvalidDataException();
        }

        return node switch
        {
            YamlScalarNode scalar => ConvertScalar(scalar),
            YamlSequenceNode sequence => BlueprintControlValue.FromSequence(
                sequence.Children.Select(child => ConvertNode(child, depth + 1, cancellationToken))),
            YamlMappingNode mapping => ConvertMapping(mapping, depth, cancellationToken),
            _ => throw new InvalidDataException(),
        };
    }

    private static BlueprintControlValue ConvertScalar(YamlScalarNode scalar)
    {
        var value = scalar.Value ?? string.Empty;
        if (value.Length > BlueprintControlLimits.MaximumScalarCharacters)
        {
            throw new BlueprintControlBoundsException();
        }

        return BlueprintControlValue.FromScalar(value);
    }

    private static BlueprintControlValue ConvertMapping(
        YamlMappingNode mapping,
        int depth,
        CancellationToken cancellationToken)
    {
        var entries = new List<KeyValuePair<string, BlueprintControlValue>>(mapping.Children.Count);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in mapping.Children)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (pair.Key is not YamlScalarNode keyNode)
            {
                throw new InvalidDataException();
            }

            var key = keyNode.Value ?? string.Empty;
            if (key.Length == 0
                || key.Length > BlueprintControlLimits.MaximumScalarCharacters
                || key == "<<"
                || !keys.Add(key))
            {
                throw new InvalidDataException();
            }

            entries.Add(KeyValuePair.Create(
                key,
                ConvertNode(pair.Value, depth + 1, cancellationToken)));
        }

        return BlueprintControlValue.FromMapping(entries);
    }

    private bool HasAllowedShape(BlueprintControlValue root)
    {
        return kind switch
        {
            BlueprintYamlDocumentKind.Manifest => HasManifestShape(root),
            BlueprintYamlDocumentKind.Rules => HasRulesShape(root),
            _ => false,
        };
    }

    private static bool HasManifestShape(BlueprintControlValue root)
    {
        if (root.Kind != BlueprintControlValueKind.Mapping
            || root.Mapping.Keys.Any(key => !_manifestFields.Contains(key)))
        {
            return false;
        }

        foreach (var collection in _manifestItemFields)
        {
            if (!root.Mapping.TryGetValue(collection.Key, out var value))
            {
                continue;
            }

            if (value.Kind != BlueprintControlValueKind.Sequence)
            {
                return false;
            }

            foreach (var item in value.Sequence)
            {
                if (item.Kind != BlueprintControlValueKind.Mapping
                    || item.Mapping.Keys.Any(key => !collection.Value.Contains(key)))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool HasRulesShape(BlueprintControlValue root)
    {
        return root.Kind == BlueprintControlValueKind.Sequence
            && root.Sequence.All(rule => rule.Kind == BlueprintControlValueKind.Mapping
                && rule.Mapping.Keys.All(_ruleFields.Contains));
    }

    private static BlueprintLoadResult<BlueprintYamlDocument> Failure(
        BlueprintInspectionIssue issue)
    {
        return new BlueprintLoadResult<BlueprintYamlDocument>(null, [issue]);
    }

    private static ImmutableHashSet<string> CreateSet(params string[] values)
    {
        return values.ToImmutableHashSet(StringComparer.Ordinal);
    }

    private sealed class BlueprintControlBoundsException : Exception;
}
