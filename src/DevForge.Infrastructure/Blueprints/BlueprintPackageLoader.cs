using System.Collections.Immutable;
using System.Globalization;
using DevForge.Application.Contracts;
using DevForge.Application.Planning.CompatibilityRules;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Privacy;

namespace DevForge.Infrastructure.Blueprints;

internal interface IBlueprintPackageLoader
{
    Task<BlueprintPackageLoadResult> LoadAsync(
        BlueprintPackageSource source,
        WorkspaceRelativePath packageDirectory,
        CancellationToken cancellationToken);
}

internal sealed class BlueprintPackageLoader : IBlueprintPackageLoader
{
    private static readonly string[] _mandatoryControlFiles =
    [
        "manifest.yaml",
        "inputs.schema.json",
        "rules.yaml",
    ];

    public async Task<BlueprintPackageLoadResult> LoadAsync(
        BlueprintPackageSource source,
        WorkspaceRelativePath packageDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(packageDirectory);
        cancellationToken.ThrowIfCancellationRequested();

        var assignedTrust = source.Provenance == BlueprintSourceProvenance.BuiltIn
            ? BlueprintTrust.BuiltIn
            : BlueprintTrust.Untrusted;
        try
        {
            var checksum = await BlueprintChecksumVerifier.VerifyAsync(
                source.Workspace,
                packageDirectory,
                cancellationToken).ConfigureAwait(false);
            if (!checksum.IsValid)
            {
                return Failure(source, packageDirectory, checksum.Issues[0]);
            }

            foreach (var requiredFile in _mandatoryControlFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!checksum.VerifiedControlFiles.ContainsKey(requiredFile))
                {
                    return Failure(source, packageDirectory, StructureIssue());
                }
            }

            var manifestDocument = await ReadYamlAsync(
                checksum.VerifiedControlFiles["manifest.yaml"],
                BlueprintYamlDocumentKind.Manifest,
                cancellationToken).ConfigureAwait(false);
            if (!manifestDocument.IsValid)
            {
                return Failure(source, packageDirectory, manifestDocument.Issues[0]);
            }

            var schemaDocument = await ReadSchemaAsync(
                checksum.VerifiedControlFiles["inputs.schema.json"],
                cancellationToken).ConfigureAwait(false);
            if (!schemaDocument.IsValid)
            {
                return Failure(source, packageDirectory, schemaDocument.Issues[0]);
            }

            var rulesDocument = await ReadYamlAsync(
                checksum.VerifiedControlFiles["rules.yaml"],
                BlueprintYamlDocumentKind.Rules,
                cancellationToken).ConfigureAwait(false);
            if (!rulesDocument.IsValid)
            {
                return Failure(source, packageDirectory, rulesDocument.Issues[0]);
            }

            var inputSchema = ConvertInputSchema(schemaDocument.Value!);
            var draft = ConvertManifest(
                manifestDocument.Value!.Root,
                rulesDocument.Value!.Root,
                inputSchema);
            if (!StringComparer.Ordinal.Equals(packageDirectory.Value, draft.Id))
            {
                return Failure(source, packageDirectory, StructureIssue());
            }

            var manifestResult = BlueprintManifest.Create(
                draft,
                new BlueprintTrustAssignment(assignedTrust));
            if (!manifestResult.IsValid)
            {
                return Failure(source, packageDirectory, StructureIssue());
            }

            var ruleParser = new CompatibilityRuleParser();
            foreach (var rule in manifestResult.Value.CompatibilityRules)
            {
                var parsed = ruleParser.Parse(rule.Expression);
                var message = RedactedText.FromTrustedRedaction(rule.Message);
                var remediation = rule.Remediation is null
                    ? null
                    : RedactedText.FromTrustedRedaction(rule.Remediation);
                if (!parsed.IsValid
                    || !message.IsValid
                    || remediation is { IsValid: false })
                {
                    return Failure(source, packageDirectory, StructureIssue());
                }
            }

            var policyTrust = assignedTrust == BlueprintTrust.BuiltIn
                ? BlueprintTrust.BuiltIn
                : BlueprintTrust.TrustedLocal;
            foreach (var action in manifestResult.Value.Actions)
            {
                var issues = BlueprintActionPolicy.Validate(action, policyTrust);
                if (!issues.IsEmpty)
                {
                    return Failure(source, packageDirectory, issues[0]);
                }
            }

            foreach (var validator in manifestResult.Value.Validators)
            {
                var parameters = validator.Parameters.SetItem(
                    "required",
                    BlueprintValue.FromBoolean(validator.Required));
                var policyAction = new BlueprintActionDefinition(
                    validator.Id,
                    validator.HandlerId,
                    parameters,
                    validator.Timeout);
                var issues = BlueprintActionPolicy.Validate(policyAction, policyTrust);
                if (!issues.IsEmpty)
                {
                    return Failure(source, packageDirectory, issues[0]);
                }
            }

            foreach (var artifact in manifestResult.Value.Artifacts)
            {
                var pathValue = BlueprintValue.FromString(artifact.Path);
                if (!pathValue.IsValid)
                {
                    return Failure(source, packageDirectory, PolicyIssue());
                }

                var policyAction = new BlueprintActionDefinition(
                    "artifact-path",
                    "create-directory",
                    ImmutableDictionary<string, BlueprintValue>.Empty
                        .WithComparers(StringComparer.Ordinal)
                        .Add("path", pathValue.Value),
                    TimeSpan.FromSeconds(1));
                var issues = BlueprintActionPolicy.Validate(policyAction, policyTrust);
                if (!issues.IsEmpty)
                {
                    return Failure(source, packageDirectory, issues[0]);
                }
            }

            var fingerprintResult = BlueprintFingerprint.Create(
                source.Id,
                packageDirectory,
                assignedTrust,
                checksum.AggregateChecksum);
            var referenceResult = BlueprintReference.Create(
                manifestResult.Value.Id,
                manifestResult.Value.Version);
            if (!fingerprintResult.IsValid || !referenceResult.IsValid)
            {
                return Failure(source, packageDirectory, StructureIssue());
            }

            var inspection = BlueprintInspection.Create(
                source.Id,
                packageDirectory,
                referenceResult.Value,
                assignedTrust,
                []).Value;
            return BlueprintPackageLoadResult.Success(
                new LoadedBlueprintPackage(
                    manifestResult.Value,
                    inputSchema,
                    fingerprintResult.Value),
                inspection);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InfrastructureOperationException
            or IOException
            or InvalidDataException
            or ArgumentException
            or OverflowException
            or FormatException)
        {
            return Failure(source, packageDirectory, StructureIssue());
        }
    }

    private static async Task<BlueprintLoadResult<BlueprintYamlDocument>> ReadYamlAsync(
        ImmutableArray<byte> content,
        BlueprintYamlDocumentKind kind,
        CancellationToken cancellationToken)
    {
        await using var stream = new MemoryStream(content.ToArray(), writable: false);
        return await new BlueprintYamlReader(kind)
            .ReadAsync(stream, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<BlueprintLoadResult<BlueprintInputSchemaDocument>> ReadSchemaAsync(
        ImmutableArray<byte> content,
        CancellationToken cancellationToken)
    {
        await using var stream = new MemoryStream(content.ToArray(), writable: false);
        return await new BlueprintJsonSchemaReader()
            .ReadAsync(stream, cancellationToken)
            .ConfigureAwait(false);
    }

    private static ImmutableArray<BlueprintInputPropertyDefinition> ConvertInputSchema(
        BlueprintInputSchemaDocument schema)
    {
        var definitions = ImmutableArray.CreateBuilder<BlueprintInputPropertyDefinition>(
            schema.Properties.Count);
        foreach (var property in schema.Properties.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var kind = property.Value.Kind switch
            {
                BlueprintSchemaValueKind.Text when !property.Value.AllowedValues.IsEmpty =>
                    BlueprintInputKind.Choice,
                BlueprintSchemaValueKind.Text => BlueprintInputKind.Text,
                BlueprintSchemaValueKind.Boolean => BlueprintInputKind.Boolean,
                BlueprintSchemaValueKind.WholeNumber => BlueprintInputKind.WholeNumber,
                _ => throw new InvalidDataException(),
            };
            var draft = new BlueprintInputPropertyDraft(
                property.Key,
                kind,
                schema.Required.Contains(property.Key),
                ConvertSchemaScalar(property.Value.DefaultValue),
                [.. property.Value.AllowedValues.Select(FormatSchemaScalar)],
                property.Value.MinimumLength,
                property.Value.MaximumLength,
                property.Value.Minimum,
                property.Value.Maximum);
            var result = BlueprintInputPropertyDefinition.Create(draft);
            if (!result.IsValid)
            {
                throw new InvalidDataException();
            }

            definitions.Add(result.Value);
        }

        return definitions.ToImmutable();
    }

    private static BlueprintManifestDraft ConvertManifest(
        BlueprintControlValue manifestRoot,
        BlueprintControlValue rulesRoot,
        ImmutableArray<BlueprintInputPropertyDefinition> inputSchema)
    {
        var root = RequireMap(manifestRoot);
        var tools = ReadSequence(root, "tools", required: true)
            .Select(ConvertTool)
            .ToArray();
        var features = ReadSequence(root, "features", required: false)
            .Select(ConvertFeature)
            .ToArray();
        var actions = ReadSequence(root, "actions", required: true)
            .Select(ConvertAction)
            .ToArray();
        var validators = ReadSequence(root, "validators", required: true)
            .Select(ConvertValidator)
            .ToArray();
        var artifacts = ReadSequence(root, "artifacts", required: true)
            .Select(ConvertArtifact)
            .ToArray();
        var dependencies = ReadSequence(root, "dependencies", required: false)
            .Select(ConvertDependency)
            .ToArray();
        var rules = RequireSequence(rulesRoot)
            .Select(ConvertRule)
            .ToArray();
        var legacyInputs = inputSchema.Select(input => new InputDefinition(
            input.Id,
            input.Kind,
            input.Required,
            FormatBlueprintValue(input.DefaultValue))).ToArray();

        return new BlueprintManifestDraft(
            RequireScalar(root, "id"),
            RequireScalar(root, "version"),
            RequireScalar(root, "engineVersion"),
            tools,
            legacyInputs,
            rules,
            [],
            validators,
            OptionalScalar(root, "name"),
            features,
            actions,
            dependencies,
            artifacts);
    }

    private static ToolRequirement ConvertTool(BlueprintControlValue value)
    {
        var map = RequireMap(value);
        return new ToolRequirement(
            RequireScalar(map, "id"),
            RequireScalar(map, "version"),
            ReadBoolean(map, "required"));
    }

    private static BlueprintFeatureDefinition ConvertFeature(BlueprintControlValue value)
    {
        var map = RequireMap(value);
        return new BlueprintFeatureDefinition(
            RequireScalar(map, "id"),
            ReadBoolean(map, "defaultEnabled"));
    }

    private static BlueprintActionDefinition ConvertAction(BlueprintControlValue value)
    {
        var map = RequireMap(value);
        return new BlueprintActionDefinition(
            RequireScalar(map, "id"),
            RequireScalar(map, "handler"),
            ConvertParameters(map, "parameters"),
            ReadTimeout(map));
    }

    private static ValidatorDefinition ConvertValidator(BlueprintControlValue value)
    {
        var map = RequireMap(value);
        return new ValidatorDefinition(
            RequireScalar(map, "id"),
            RequireScalar(map, "handler"),
            ReadTimeout(map),
            ConvertParameters(map, "parameters"),
            ReadBoolean(map, "required"));
    }

    private static BlueprintArtifact ConvertArtifact(BlueprintControlValue value)
    {
        return new BlueprintArtifact(RequireScalar(RequireMap(value), "path"));
    }

    private static BlueprintDependency ConvertDependency(BlueprintControlValue value)
    {
        var map = RequireMap(value);
        return new BlueprintDependency(
            RequireScalar(map, "id"),
            RequireScalar(map, "version"));
    }

    private static CompatibilityRule ConvertRule(BlueprintControlValue value)
    {
        var map = RequireMap(value);
        var severity = RequireScalar(map, "severity") switch
        {
            "blocking" => CompatibilityRuleSeverity.Blocking,
            "warning" => CompatibilityRuleSeverity.Warning,
            _ => throw new InvalidDataException(),
        };
        if (RequireScalar(map, "override") != "none")
        {
            throw new InvalidDataException();
        }

        return new CompatibilityRule(
            RequireScalar(map, "id"),
            RequireScalar(map, "condition"),
            severity,
            RequireScalar(map, "message"),
            OptionalScalar(map, "remediation"),
            CompatibilityRuleOverride.None);
    }

    private static ImmutableDictionary<string, BlueprintValue> ConvertParameters(
        ImmutableDictionary<string, BlueprintControlValue> map,
        string field)
    {
        if (!map.TryGetValue(field, out var value))
        {
            throw new InvalidDataException();
        }

        return RequireMap(value)
            .Select(item => KeyValuePair.Create(item.Key, ConvertValue(item.Value)))
            .ToImmutableDictionary(StringComparer.Ordinal);
    }

    private static BlueprintValue ConvertValue(BlueprintControlValue value)
    {
        return value.Kind switch
        {
            BlueprintControlValueKind.Scalar => ConvertScalarValue(value.Scalar!),
            BlueprintControlValueKind.Sequence => ConvertArrayValue(value.Sequence),
            BlueprintControlValueKind.Mapping => ConvertObjectValue(value.Mapping),
            _ => throw new InvalidDataException(),
        };
    }

    private static BlueprintValue ConvertScalarValue(string value)
    {
        if (bool.TryParse(value, out var boolean))
        {
            return BlueprintValue.FromBoolean(boolean);
        }

        if (long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
        {
            return BlueprintValue.FromInteger(integer);
        }

        var result = BlueprintValue.FromString(value);
        return result.IsValid ? result.Value : throw new InvalidDataException();
    }

    private static BlueprintValue ConvertArrayValue(ImmutableArray<BlueprintControlValue> values)
    {
        var result = BlueprintValue.FromArray(values.Select(ConvertValue));
        return result.IsValid ? result.Value : throw new InvalidDataException();
    }

    private static BlueprintValue ConvertObjectValue(
        ImmutableDictionary<string, BlueprintControlValue> values)
    {
        var result = BlueprintValue.FromObject(values.Select(item =>
            KeyValuePair.Create<string, BlueprintValue?>(item.Key, ConvertValue(item.Value))));
        return result.IsValid ? result.Value : throw new InvalidDataException();
    }

    private static BlueprintValue? ConvertSchemaScalar(BlueprintSchemaScalar? value)
    {
        return value?.Kind switch
        {
            null => null,
            BlueprintSchemaValueKind.Text => ValidString(value.Text!),
            BlueprintSchemaValueKind.Boolean => BlueprintValue.FromBoolean(value.Boolean),
            BlueprintSchemaValueKind.WholeNumber => BlueprintValue.FromInteger(value.WholeNumber),
            _ => throw new InvalidDataException(),
        };
    }

    private static BlueprintValue ValidString(string value)
    {
        var result = BlueprintValue.FromString(value);
        return result.IsValid ? result.Value : throw new InvalidDataException();
    }

    private static string FormatSchemaScalar(BlueprintSchemaScalar value)
    {
        return value.Kind switch
        {
            BlueprintSchemaValueKind.Text => value.Text!,
            BlueprintSchemaValueKind.Boolean => value.Boolean ? "true" : "false",
            BlueprintSchemaValueKind.WholeNumber => value.WholeNumber.ToString(CultureInfo.InvariantCulture),
            _ => throw new InvalidDataException(),
        };
    }

    private static string? FormatBlueprintValue(BlueprintValue? value)
    {
        return value?.Kind switch
        {
            null => null,
            BlueprintValueKind.Text => value.StringValue,
            BlueprintValueKind.Boolean => value.BooleanValue ? "true" : "false",
            BlueprintValueKind.WholeNumber => value.IntegerValue.ToString(CultureInfo.InvariantCulture),
            _ => throw new InvalidDataException(),
        };
    }

    private static ImmutableDictionary<string, BlueprintControlValue> RequireMap(
        BlueprintControlValue value)
    {
        return value.Kind == BlueprintControlValueKind.Mapping
            ? value.Mapping
            : throw new InvalidDataException();
    }

    private static ImmutableArray<BlueprintControlValue> RequireSequence(BlueprintControlValue value)
    {
        return value.Kind == BlueprintControlValueKind.Sequence
            ? value.Sequence
            : throw new InvalidDataException();
    }

    private static ImmutableArray<BlueprintControlValue> ReadSequence(
        ImmutableDictionary<string, BlueprintControlValue> map,
        string field,
        bool required)
    {
        if (!map.TryGetValue(field, out var value))
        {
            return required ? throw new InvalidDataException() : [];
        }

        return RequireSequence(value);
    }

    private static string RequireScalar(
        ImmutableDictionary<string, BlueprintControlValue> map,
        string field)
    {
        return OptionalScalar(map, field) ?? throw new InvalidDataException();
    }

    private static string? OptionalScalar(
        ImmutableDictionary<string, BlueprintControlValue> map,
        string field)
    {
        if (!map.TryGetValue(field, out var value))
        {
            return null;
        }

        return value.Kind == BlueprintControlValueKind.Scalar
            ? value.Scalar
            : throw new InvalidDataException();
    }

    private static bool ReadBoolean(
        ImmutableDictionary<string, BlueprintControlValue> map,
        string field)
    {
        return bool.TryParse(RequireScalar(map, field), out var value)
            ? value
            : throw new InvalidDataException();
    }

    private static TimeSpan ReadTimeout(ImmutableDictionary<string, BlueprintControlValue> map)
    {
        if (!int.TryParse(
                RequireScalar(map, "timeoutSeconds"),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var seconds)
            || seconds <= 0)
        {
            throw new InvalidDataException();
        }

        return TimeSpan.FromSeconds(seconds);
    }

    private static BlueprintPackageLoadResult Failure(
        BlueprintPackageSource source,
        WorkspaceRelativePath packageDirectory,
        BlueprintInspectionIssue issue)
    {
        var inspection = BlueprintInspection.Create(
            source.Id,
            packageDirectory,
            null,
            BlueprintTrust.Quarantined,
            [issue]).Value;
        return BlueprintPackageLoadResult.Failure(inspection);
    }

    private static BlueprintInspectionIssue StructureIssue()
    {
        return BlueprintInspectionIssue.Create(
            "DF-BP-001",
            "The blueprint package structure is malformed or unsupported.").Value;
    }

    private static BlueprintInspectionIssue PolicyIssue()
    {
        return BlueprintInspectionIssue.Create(
            "DF-BP-003",
            "The blueprint package contains a forbidden action, path, or variable.").Value;
    }
}

internal sealed record LoadedBlueprintPackage(
    BlueprintManifest Manifest,
    ImmutableArray<BlueprintInputPropertyDefinition> InputSchema,
    BlueprintFingerprint Fingerprint);

internal sealed record BlueprintPackageLoadResult(
    LoadedBlueprintPackage? Package,
    BlueprintInspection Inspection)
{
    internal bool IsValid => Package is not null && Inspection.Issues.IsEmpty;

    internal static BlueprintPackageLoadResult Success(
        LoadedBlueprintPackage package,
        BlueprintInspection inspection)
    {
        return new BlueprintPackageLoadResult(package, inspection);
    }

    internal static BlueprintPackageLoadResult Failure(BlueprintInspection inspection)
    {
        return new BlueprintPackageLoadResult(null, inspection);
    }
}
