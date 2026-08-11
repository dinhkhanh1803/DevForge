using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using DevForge.Application.Contracts;
using DevForge.Blueprints.Abstractions.Validation;

namespace DevForge.Infrastructure.Blueprints;

internal enum BlueprintSchemaValueKind
{
    Text = 1,
    Boolean = 2,
    WholeNumber = 3,
}

internal sealed record BlueprintSchemaScalar(
    BlueprintSchemaValueKind Kind,
    string? Text,
    bool Boolean,
    long WholeNumber);

internal sealed record BlueprintInputSchemaPropertyDocument(
    string Id,
    BlueprintSchemaValueKind Kind,
    ImmutableArray<BlueprintSchemaScalar> AllowedValues,
    BlueprintSchemaScalar? DefaultValue,
    int? MinimumLength,
    int? MaximumLength,
    long? Minimum,
    long? Maximum);

internal sealed record BlueprintInputSchemaDocument(
    ImmutableDictionary<string, BlueprintInputSchemaPropertyDocument> Properties,
    ImmutableHashSet<string> Required);

internal sealed class BlueprintJsonSchemaReader
    : IBlueprintControlReader<BlueprintInputSchemaDocument>
{
    private static readonly ImmutableHashSet<string> _rootFields = CreateSet(
        "type",
        "properties",
        "required",
        "additionalProperties");

    private static readonly ImmutableHashSet<string> _propertyFields = CreateSet(
        "type",
        "enum",
        "default",
        "minLength",
        "maxLength",
        "minimum",
        "maximum");

    public async ValueTask<BlueprintLoadResult<BlueprintInputSchemaDocument>> ReadAsync(
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
            ValidateJsonDepth(Encoding.UTF8.GetBytes(bounded.Text!));
            using var document = JsonDocument.Parse(
                bounded.Text!,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = BlueprintControlLimits.MaximumDepth,
                });
            ValidateTree(document.RootElement, 1, cancellationToken);
            var schema = ParseSchema(document.RootElement);
            return new BlueprintLoadResult<BlueprintInputSchemaDocument>(schema, []);
        }
        catch (BlueprintSchemaBoundsException)
        {
            return Failure(BlueprintControlReadSupport.BoundsIssue());
        }
        catch (JsonException)
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

    private static BlueprintInputSchemaDocument ParseSchema(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException();
        }

        var rootProperties = root.EnumerateObject().ToArray();
        if (rootProperties.Any(property => !_rootFields.Contains(property.Name))
            || !TryGet(root, "type", JsonValueKind.String, out var type)
            || type.GetString() != "object"
            || !TryGet(root, "properties", JsonValueKind.Object, out var propertiesElement)
            || !TryGet(root, "required", JsonValueKind.Array, out var requiredElement)
            || !TryGet(root, "additionalProperties", JsonValueKind.False, out _))
        {
            throw new InvalidDataException();
        }

        var properties = ImmutableDictionary.CreateBuilder<string, BlueprintInputSchemaPropertyDocument>(
            StringComparer.Ordinal);
        foreach (var property in propertiesElement.EnumerateObject())
        {
            if (!BlueprintIdentifierValidator.IsValid(property.Name))
            {
                throw new InvalidDataException();
            }

            properties.Add(property.Name, ParseProperty(property.Name, property.Value));
        }

        var required = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var item in requiredElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException();
            }

            var id = item.GetString();
            if (id is null || !properties.ContainsKey(id) || !required.Add(id))
            {
                throw new InvalidDataException();
            }
        }

        return new BlueprintInputSchemaDocument(properties.ToImmutable(), required.ToImmutable());
    }

    private static BlueprintInputSchemaPropertyDocument ParseProperty(string id, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object
            || element.EnumerateObject().Any(property => !_propertyFields.Contains(property.Name))
            || !TryGet(element, "type", JsonValueKind.String, out var typeElement))
        {
            throw new InvalidDataException();
        }

        var kind = typeElement.GetString() switch
        {
            "string" => BlueprintSchemaValueKind.Text,
            "boolean" => BlueprintSchemaValueKind.Boolean,
            "integer" => BlueprintSchemaValueKind.WholeNumber,
            _ => throw new InvalidDataException(),
        };
        var allowedValues = ImmutableArray<BlueprintSchemaScalar>.Empty;
        if (element.TryGetProperty("enum", out var enumElement))
        {
            if (enumElement.ValueKind != JsonValueKind.Array || enumElement.GetArrayLength() == 0)
            {
                throw new InvalidDataException();
            }

            allowedValues = [.. enumElement.EnumerateArray().Select(value => ParseScalar(value, kind))];
        }

        var defaultValue = element.TryGetProperty("default", out var defaultElement)
            ? ParseScalar(defaultElement, kind)
            : null;
        var minimumLength = ReadOptionalNonNegativeInt32(element, "minLength");
        var maximumLength = ReadOptionalNonNegativeInt32(element, "maxLength");
        var minimum = ReadOptionalInt64(element, "minimum");
        var maximum = ReadOptionalInt64(element, "maximum");
        if (kind == BlueprintSchemaValueKind.Text)
        {
            if (minimum is not null || maximum is not null || minimumLength > maximumLength)
            {
                throw new InvalidDataException();
            }
        }
        else if (kind == BlueprintSchemaValueKind.WholeNumber)
        {
            if (minimumLength is not null || maximumLength is not null || minimum > maximum)
            {
                throw new InvalidDataException();
            }
        }
        else if (minimumLength is not null
            || maximumLength is not null
            || minimum is not null
            || maximum is not null)
        {
            throw new InvalidDataException();
        }

        if (defaultValue is not null
            && !allowedValues.IsEmpty
            && !allowedValues.Contains(defaultValue))
        {
            throw new InvalidDataException();
        }

        return new BlueprintInputSchemaPropertyDocument(
            id,
            kind,
            allowedValues,
            defaultValue,
            minimumLength,
            maximumLength,
            minimum,
            maximum);
    }

    private static BlueprintSchemaScalar ParseScalar(
        JsonElement element,
        BlueprintSchemaValueKind expectedKind)
    {
        return expectedKind switch
        {
            BlueprintSchemaValueKind.Text when element.ValueKind == JsonValueKind.String =>
                new BlueprintSchemaScalar(expectedKind, element.GetString(), false, 0),
            BlueprintSchemaValueKind.Boolean when element.ValueKind is JsonValueKind.True or JsonValueKind.False =>
                new BlueprintSchemaScalar(expectedKind, null, element.GetBoolean(), 0),
            BlueprintSchemaValueKind.WholeNumber when element.ValueKind == JsonValueKind.Number
                && element.TryGetInt64(out var value) =>
                new BlueprintSchemaScalar(expectedKind, null, false, value),
            _ => throw new InvalidDataException(),
        };
    }

    private static int? ReadOptionalNonNegativeInt32(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var number)
            || number < 0)
        {
            throw new InvalidDataException();
        }

        return number;
    }

    private static long? ReadOptionalInt64(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number))
        {
            throw new InvalidDataException();
        }

        return number;
    }

    private static void ValidateTree(
        JsonElement element,
        int depth,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (depth > BlueprintControlLimits.MaximumDepth)
        {
            throw new BlueprintSchemaBoundsException();
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name.Length > BlueprintControlLimits.MaximumScalarCharacters
                        || !names.Add(property.Name))
                    {
                        throw new InvalidDataException();
                    }

                    ValidateTree(property.Value, depth + 1, cancellationToken);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    ValidateTree(item, depth + 1, cancellationToken);
                }

                break;
            case JsonValueKind.String:
                if (element.GetString()?.Length > BlueprintControlLimits.MaximumScalarCharacters)
                {
                    throw new BlueprintSchemaBoundsException();
                }

                break;
        }
    }

    private static void ValidateJsonDepth(ReadOnlySpan<byte> content)
    {
        var reader = new Utf8JsonReader(
            content,
            new JsonReaderOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = BlueprintControlLimits.MaximumControlFileBytes,
            });
        while (reader.Read())
        {
            if (reader.CurrentDepth > BlueprintControlLimits.MaximumDepth)
            {
                throw new BlueprintSchemaBoundsException();
            }
        }
    }

    private static bool TryGet(
        JsonElement element,
        string name,
        JsonValueKind expectedKind,
        out JsonElement value)
    {
        return element.TryGetProperty(name, out value) && value.ValueKind == expectedKind;
    }

    private static BlueprintLoadResult<BlueprintInputSchemaDocument> Failure(
        BlueprintInspectionIssue issue)
    {
        return new BlueprintLoadResult<BlueprintInputSchemaDocument>(null, [issue]);
    }

    private static ImmutableHashSet<string> CreateSet(params string[] values)
    {
        return values.ToImmutableHashSet(StringComparer.Ordinal);
    }

    private sealed class BlueprintSchemaBoundsException : Exception;
}
