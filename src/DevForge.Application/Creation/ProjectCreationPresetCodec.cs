using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using DevForge.Application.Contracts;
using DevForge.Application.Contracts.Persistence;
using DevForge.Domain.Projects;
using DevForge.Domain.Validation;

namespace DevForge.Application.Creation;

public sealed class ProjectCreationPresetCodec
{
    private const int CurrentSchemaVersion = 2;
    private const int LegacySchemaVersion = 1;
    private const int MaximumItems = 128;

    private static readonly ImmutableHashSet<string> _rootProperties =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "schemaVersion",
            "blueprint",
            "inputs",
            "features",
            "ideId",
            "git");

    private static readonly ImmutableHashSet<string> _blueprintProperties =
        ImmutableHashSet.Create(StringComparer.Ordinal, "id", "version");

    private static readonly ImmutableHashSet<string> _inputProperties =
        ImmutableHashSet.Create(StringComparer.Ordinal, "kind", "value");

    private static readonly ImmutableHashSet<string> _gitProperties =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "initializeRepository",
            "branchPolicy",
            "publishToGitHub",
            "isPrivate",
            "githubAccount",
            "githubRepository");

    private readonly JsonDocumentOptions _documentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 32,
    };

    private readonly JsonWriterOptions _writerOptions = new() { Indented = false };

    public ValidationResult<PersistableJson> Encode(ProjectCreationPresetDraft? draft)
    {
        if (draft is null)
        {
            return Failure<PersistableJson>(
                "creation.preset.required",
                "A guarded project creation preset is required.",
                "draft");
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, _writerOptions))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", CurrentSchemaVersion);
            writer.WritePropertyName("blueprint");
            writer.WriteStartObject();
            writer.WriteString("id", draft.Blueprint.Id);
            writer.WriteString("version", draft.Blueprint.Version);
            writer.WriteEndObject();
            writer.WritePropertyName("inputs");
            writer.WriteStartObject();
            foreach (var input in draft.Inputs)
            {
                writer.WritePropertyName(input.Key);
                WriteInput(writer, input.Value);
            }

            writer.WriteEndObject();
            writer.WritePropertyName("features");
            writer.WriteStartArray();
            foreach (var feature in draft.Features.Order(StringComparer.Ordinal))
            {
                writer.WriteStringValue(feature);
            }

            writer.WriteEndArray();
            writer.WriteString("ideId", draft.IdeId);
            WriteGit(writer, draft.Git);
            writer.WriteEndObject();
        }

        return PersistableJson.Create(Encoding.UTF8.GetString(stream.ToArray()));
    }

    public ValidationResult<ProjectCreationPresetDraft> Decode(PersistableJson? document)
    {
        if (document is null)
        {
            return Failure<ProjectCreationPresetDraft>(
                "creation.preset.document.required",
                "A persisted project creation preset is required.",
                "document");
        }

        try
        {
            using var parsed = JsonDocument.Parse(document.Value, _documentOptions);
            var root = parsed.RootElement;
            var issues = new List<ValidationIssue>();
            AddUnknownProperties(root, _rootProperties, "creation.preset.property.unknown", issues);

            var versionNumber = 0;
            if (!root.TryGetProperty("schemaVersion", out var version)
                || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out versionNumber)
                || versionNumber is not (LegacySchemaVersion or CurrentSchemaVersion))
            {
                issues.Add(new ValidationIssue(
                    "creation.preset.schema-version.invalid",
                    "The preset schema version is missing or unsupported.",
                    "schemaVersion"));
            }

            var blueprint = DecodeBlueprint(root, issues);
            var inputs = DecodeInputs(root, issues);
            var features = DecodeFeatures(root, issues);
            var ideId = ReadRequiredString(root, "ideId", "creation.preset.ide.invalid", issues);
            var git = DecodeGit(root, versionNumber, issues);
            if (issues.Count != 0)
            {
                return ValidationResult.Failure<ProjectCreationPresetDraft>(issues);
            }

            return ProjectCreationPresetDraft.Create(
                blueprint,
                inputs,
                features,
                ideId,
                git.InitializeRepository,
                git.BranchPolicy,
                git.PublishToGitHub,
                git.IsPrivate,
                git.GitHubAccount,
                git.GitHubRepository);
        }
        catch (JsonException)
        {
            return Failure<ProjectCreationPresetDraft>(
                "creation.preset.invalid",
                "The persisted project creation preset is invalid.",
                "document");
        }
    }

    private static GitOptions DecodeGit(
        JsonElement root,
        int schemaVersion,
        List<ValidationIssue> issues)
    {
        if (schemaVersion == LegacySchemaVersion)
        {
            if (root.TryGetProperty("git", out _))
            {
                issues.Add(new ValidationIssue(
                    "creation.preset.git.not-supported",
                    "Legacy presets cannot contain reviewed Git settings.",
                    "git"));
            }

            return GitOptions.Create().Value;
        }

        if (!root.TryGetProperty("git", out var element)
            || element.ValueKind != JsonValueKind.Object)
        {
            issues.Add(new ValidationIssue(
                "creation.preset.git.invalid",
                "Version two presets require reviewed Git settings.",
                "git"));
            return GitOptions.Create().Value;
        }

        AddUnknownProperties(
            element,
            _gitProperties,
            "creation.preset.git.property.unknown",
            issues);
        var initializeRepository = ReadRequiredBoolean(
            element,
            "initializeRepository",
            "creation.preset.git.initialize.invalid",
            issues);
        var publishToGitHub = ReadRequiredBoolean(
            element,
            "publishToGitHub",
            "creation.preset.git.publish.invalid",
            issues);
        var isPrivate = ReadRequiredBoolean(
            element,
            "isPrivate",
            "creation.preset.git.visibility.invalid",
            issues);
        var branchPolicyText = ReadRequiredString(
            element,
            "branchPolicy",
            "creation.preset.git.branch-policy.invalid",
            issues);
        var branchPolicy = branchPolicyText switch
        {
            "main" => GitBranchPolicy.Main,
            "main-and-develop" => GitBranchPolicy.MainAndDevelop,
            _ => (GitBranchPolicy?)null,
        };
        if (branchPolicy is null && branchPolicyText is not null)
        {
            issues.Add(new ValidationIssue(
                "creation.preset.git.branch-policy.invalid",
                "A supported Git branch policy is required.",
                "git.branchPolicy"));
        }

        var githubAccount = ReadNullableString(
            element,
            "githubAccount",
            "creation.preset.git.account.invalid",
            issues);
        var githubRepository = ReadNullableString(
            element,
            "githubRepository",
            "creation.preset.git.repository.invalid",
            issues);
        var result = GitOptions.Create(
            initializeRepository,
            primaryBranch: "main",
            useDevelopBranch: branchPolicy == GitBranchPolicy.MainAndDevelop,
            publishToGitHub,
            isPrivate,
            githubAccount,
            githubRepository);
        if (!result.IsValid)
        {
            issues.AddRange(result.Issues);
            return GitOptions.Create().Value;
        }

        return result.Value;
    }

    private static bool ReadRequiredBoolean(
        JsonElement owner,
        string propertyName,
        string code,
        List<ValidationIssue> issues)
    {
        if (!owner.TryGetProperty(propertyName, out var element)
            || element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            issues.Add(new ValidationIssue(
                code,
                "A required preset Boolean value is missing or invalid.",
                $"git.{propertyName}"));
            return false;
        }

        return element.GetBoolean();
    }

    private static string? ReadNullableString(
        JsonElement owner,
        string propertyName,
        string code,
        List<ValidationIssue> issues)
    {
        if (!owner.TryGetProperty(propertyName, out var element))
        {
            issues.Add(new ValidationIssue(
                code,
                "A required nullable preset string is missing.",
                $"git.{propertyName}"));
            return null;
        }

        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(element.GetString()))
        {
            issues.Add(new ValidationIssue(
                code,
                "A preset string value is invalid.",
                $"git.{propertyName}"));
            return null;
        }

        return element.GetString();
    }

    private static BlueprintReference? DecodeBlueprint(
        JsonElement root,
        List<ValidationIssue> issues)
    {
        if (!root.TryGetProperty("blueprint", out var element)
            || element.ValueKind != JsonValueKind.Object)
        {
            issues.Add(new ValidationIssue(
                "creation.preset.blueprint.invalid",
                "An exact blueprint reference is required.",
                "blueprint"));
            return null;
        }

        AddUnknownProperties(
            element,
            _blueprintProperties,
            "creation.preset.blueprint.property.unknown",
            issues);
        var id = ReadRequiredString(
            element,
            "id",
            "creation.preset.blueprint.invalid",
            issues);
        var version = ReadRequiredString(
            element,
            "version",
            "creation.preset.blueprint.invalid",
            issues);
        var result = BlueprintReference.Create(id, version);
        if (!result.IsValid)
        {
            issues.Add(new ValidationIssue(
                "creation.preset.blueprint.invalid",
                "An exact canonical blueprint reference is required.",
                "blueprint"));
            return null;
        }

        return result.Value;
    }

    private static List<KeyValuePair<string, DynamicInputValue?>>? DecodeInputs(
        JsonElement root,
        List<ValidationIssue> issues)
    {
        if (!root.TryGetProperty("inputs", out var element)
            || element.ValueKind != JsonValueKind.Object)
        {
            issues.Add(new ValidationIssue(
                "creation.preset.inputs.invalid",
                "A persisted input object is required.",
                "inputs"));
            return null;
        }

        var properties = element.EnumerateObject().ToArray();
        if (properties.Length > MaximumItems)
        {
            issues.Add(new ValidationIssue(
                "creation.preset.inputs.too-many",
                "The preset input collection exceeds the supported count.",
                "inputs"));
        }

        var result = new List<KeyValuePair<string, DynamicInputValue?>>(properties.Length);
        foreach (var property in properties)
        {
            var input = DecodeInput(property, issues);
            result.Add(new KeyValuePair<string, DynamicInputValue?>(property.Name, input));
        }

        return result;
    }

    private static DynamicInputValue? DecodeInput(
        JsonProperty property,
        List<ValidationIssue> issues)
    {
        if (property.Value.ValueKind != JsonValueKind.Object)
        {
            AddInvalidInput(property.Name, issues);
            return null;
        }

        AddUnknownProperties(
            property.Value,
            _inputProperties,
            "creation.preset.input.property.unknown",
            issues);
        if (!property.Value.TryGetProperty("kind", out var kindElement)
            || kindElement.ValueKind != JsonValueKind.String
            || !property.Value.TryGetProperty("value", out var valueElement))
        {
            AddInvalidInput(property.Name, issues);
            return null;
        }

        ValidationResult<DynamicInputValue>? result = kindElement.GetString() switch
        {
            "text" when valueElement.ValueKind == JsonValueKind.String =>
                DynamicInputValue.Text(valueElement.GetString()),
            "boolean" when valueElement.ValueKind is JsonValueKind.True or JsonValueKind.False =>
                DynamicInputValue.Boolean(valueElement.GetBoolean()),
            "whole-number" when valueElement.ValueKind == JsonValueKind.Number
                && valueElement.TryGetInt64(out var value) => DynamicInputValue.WholeNumber(value),
            _ => null,
        };
        if (result is null || !result.IsValid)
        {
            AddInvalidInput(property.Name, issues);
            return null;
        }

        return result.Value;
    }

    private static string?[]? DecodeFeatures(
        JsonElement root,
        List<ValidationIssue> issues)
    {
        if (!root.TryGetProperty("features", out var element)
            || element.ValueKind != JsonValueKind.Array)
        {
            issues.Add(new ValidationIssue(
                "creation.preset.features.invalid",
                "A persisted feature array is required.",
                "features"));
            return null;
        }

        var items = element.EnumerateArray().ToArray();
        if (items.Length > MaximumItems)
        {
            issues.Add(new ValidationIssue(
                "creation.preset.features.too-many",
                "The preset feature collection exceeds the supported count.",
                "features"));
        }

        var result = new string?[items.Length];
        for (var index = 0; index < items.Length; index++)
        {
            if (items[index].ValueKind != JsonValueKind.String)
            {
                issues.Add(new ValidationIssue(
                    "creation.preset.feature.invalid",
                    "A preset feature must be a canonical string identifier.",
                    $"features[{index}]"));
                continue;
            }

            result[index] = items[index].GetString();
        }

        return result;
    }

    private static string? ReadRequiredString(
        JsonElement owner,
        string propertyName,
        string code,
        List<ValidationIssue> issues)
    {
        if (!owner.TryGetProperty(propertyName, out var element)
            || element.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(element.GetString()))
        {
            issues.Add(new ValidationIssue(
                code,
                "A required preset string value is missing or invalid.",
                propertyName));
            return null;
        }

        return element.GetString();
    }

    private static void AddUnknownProperties(
        JsonElement element,
        ImmutableHashSet<string> allowed,
        string code,
        List<ValidationIssue> issues)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
            {
                issues.Add(new ValidationIssue(
                    code,
                    "The preset contains an unsupported property.",
                    property.Name));
            }
        }
    }

    private static void AddInvalidInput(string name, List<ValidationIssue> issues)
    {
        issues.Add(new ValidationIssue(
            "creation.preset.input.invalid",
            "A preset input kind and value do not match.",
            $"inputs.{name}"));
    }

    private static void WriteInput(Utf8JsonWriter writer, DynamicInputValue value)
    {
        writer.WriteStartObject();
        switch (value.Kind)
        {
            case DynamicInputValueKind.Text:
                writer.WriteString("kind", "text");
                writer.WriteString("value", value.TextValue);
                break;
            case DynamicInputValueKind.Boolean:
                writer.WriteString("kind", "boolean");
                writer.WriteBoolean("value", value.BooleanValue);
                break;
            case DynamicInputValueKind.WholeNumber:
                writer.WriteString("kind", "whole-number");
                writer.WriteNumber("value", value.WholeNumberValue);
                break;
            default:
                throw new InvalidOperationException("An unsupported dynamic input kind cannot be encoded.");
        }

        writer.WriteEndObject();
    }

    private static void WriteGit(Utf8JsonWriter writer, GitOptions git)
    {
        writer.WritePropertyName("git");
        writer.WriteStartObject();
        writer.WriteBoolean("initializeRepository", git.InitializeRepository);
        writer.WriteString(
            "branchPolicy",
            git.BranchPolicy == GitBranchPolicy.MainAndDevelop ? "main-and-develop" : "main");
        writer.WriteBoolean("publishToGitHub", git.PublishToGitHub);
        writer.WriteBoolean("isPrivate", git.IsPrivate);
        writer.WriteString("githubAccount", git.GitHubAccount);
        writer.WriteString("githubRepository", git.GitHubRepository);
        writer.WriteEndObject();
    }

    private static ValidationResult<T> Failure<T>(string code, string message, string location)
    {
        return ValidationResult.Failure<T>([new ValidationIssue(code, message, location)]);
    }
}
