using System.Collections.Immutable;
using System.Text;
using DevForge.Application.Contracts;
using DevForge.Application.Planning;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Blueprints.Abstractions.Validation;
using DevForge.Domain.Privacy;

namespace DevForge.Infrastructure.Blueprints;

internal static class BlueprintActionPolicy
{
    private static readonly ImmutableDictionary<string, ActionDescriptor> _descriptors =
        new Dictionary<string, ActionDescriptor>(StringComparer.Ordinal)
        {
            ["create-directory"] = Descriptor(false, ("path", ParameterKind.Path)),
            ["render-template"] = Descriptor(
                false,
                ("source", ParameterKind.Path),
                ("target", ParameterKind.Path)),
            ["copy-overlay"] = Descriptor(
                false,
                ("source", ParameterKind.Path),
                ("target", ParameterKind.Path)),
            ["patch-json"] = Descriptor(
                false,
                ("target", ParameterKind.Path),
                ("operations", ParameterKind.Sequence)),
            ["patch-yaml"] = Descriptor(
                false,
                ("target", ParameterKind.Path),
                ("operations", ParameterKind.Sequence)),
            ["patch-xml"] = Descriptor(
                false,
                ("target", ParameterKind.Path),
                ("operations", ParameterKind.Sequence)),
            ["run-process"] = Descriptor(
                false,
                ("executable", ParameterKind.Identifier),
                ("arguments", ParameterKind.TextSequence),
                ("workingDirectory", ParameterKind.Path),
                ("allowedExitCodes", ParameterKind.IntegerSequence)),
            ["package-install"] = Descriptor(
                false,
                ("packageManager", ParameterKind.Identifier),
                ("arguments", ParameterKind.TextSequence),
                ("workingDirectory", ParameterKind.Path)),
            ["validate-command"] = Descriptor(
                false,
                ("executable", ParameterKind.Identifier),
                ("arguments", ParameterKind.TextSequence),
                ("workingDirectory", ParameterKind.Path),
                ("allowedExitCodes", ParameterKind.IntegerSequence),
                ("required", ParameterKind.Boolean)),
            ["validate-file-exists"] = Descriptor(
                false,
                ("path", ParameterKind.Path),
                ("required", ParameterKind.Boolean)),
            ["validate-file-content"] = Descriptor(
                false,
                ("path", ParameterKind.Path),
                ("contains", ParameterKind.Text),
                ("required", ParameterKind.Boolean)),
            ["git-operation"] = Descriptor(
                true,
                ("operation", ParameterKind.Identifier),
                ("payload", ParameterKind.Map)),
            ["github-operation"] = Descriptor(
                true,
                ("operation", ParameterKind.Identifier),
                ("payload", ParameterKind.Map)),
            ["finalize-workspace"] = Descriptor(true),
        }.ToImmutableDictionary(StringComparer.Ordinal);

    internal static ImmutableArray<BlueprintInspectionIssue> Validate(
        BlueprintActionDefinition action,
        BlueprintTrust trust)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!Enum.IsDefined(trust)
            || trust is BlueprintTrust.Untrusted or BlueprintTrust.Quarantined)
        {
            return [Issue("DF-BP-002", "The blueprint trust state does not permit executable actions.")];
        }

        if (!_descriptors.TryGetValue(action.HandlerId, out var descriptor)
            || (descriptor.BuiltInOnly && trust != BlueprintTrust.BuiltIn)
            || action.Parameters is null
            || action.Timeout <= TimeSpan.Zero)
        {
            return [Issue("DF-BP-003", "The blueprint action is not permitted by the closed handler policy.")];
        }

        if (action.Parameters.Count != descriptor.Parameters.Count
            || action.Parameters.Keys.Any(key => !descriptor.Parameters.ContainsKey(key)))
        {
            return [Issue("DF-BP-003", "The blueprint action parameter set is not permitted.")];
        }

        foreach (var parameter in descriptor.Parameters)
        {
            if (!action.Parameters.TryGetValue(parameter.Key, out var value)
                || value is null
                || !HasKind(value, parameter.Value)
                || !VariablesAreValid(value)
                || parameter.Value == ParameterKind.Path && !IsSafePath(value.StringValue!)
                || parameter.Value == ParameterKind.Path
                    && IsEngineOwnedEvidenceTarget(parameter.Key, value.StringValue!)
                || parameter.Value == ParameterKind.Identifier
                    && !BlueprintIdentifierValidator.IsValid(value.StringValue))
            {
                return [Issue("DF-BP-003", "A blueprint action parameter is invalid or unsafe.")];
            }
        }

        return [];
    }

    private static bool HasKind(BlueprintValue value, ParameterKind kind)
    {
        return kind switch
        {
            ParameterKind.Boolean => value.Kind == BlueprintValueKind.Boolean,
            ParameterKind.Text => value.Kind == BlueprintValueKind.Text,
            ParameterKind.Identifier or ParameterKind.Path => value.Kind == BlueprintValueKind.Text,
            ParameterKind.Sequence => value.Kind == BlueprintValueKind.Sequence,
            ParameterKind.Map => value.Kind == BlueprintValueKind.Map,
            ParameterKind.TextSequence => value.Kind == BlueprintValueKind.Sequence
                && value.ArrayValue.All(item => item.Kind == BlueprintValueKind.Text),
            ParameterKind.IntegerSequence => value.Kind == BlueprintValueKind.Sequence
                && value.ArrayValue.All(item => item.Kind == BlueprintValueKind.WholeNumber),
            _ => false,
        };
    }

    private static bool VariablesAreValid(BlueprintValue value)
    {
        return value.Kind switch
        {
            BlueprintValueKind.Text => TryTokenize(value.StringValue!, out _),
            BlueprintValueKind.Sequence => value.ArrayValue.All(VariablesAreValid),
            BlueprintValueKind.Map => value.ObjectValue.All(pair =>
                !RedactedText.IsSecretShapedKey(pair.Key) && VariablesAreValid(pair.Value)),
            BlueprintValueKind.Boolean or BlueprintValueKind.WholeNumber => true,
            _ => false,
        };
    }

    private static bool IsSafePath(string template)
    {
        if (!TryTokenize(template, out var structuralValue))
        {
            return false;
        }

        if (structuralValue == ".")
        {
            return true;
        }

        var path = WorkspaceRelativePath.Create(structuralValue);
        return path.IsValid
            && !path.Value.Value.Split('\\').Any(segment =>
                segment.Equals(".env", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsEngineOwnedEvidenceTarget(string parameterName, string template)
    {
        if (parameterName is "source" or "workingDirectory"
            || !TryTokenize(template, out var structuralValue))
        {
            return false;
        }

        var path = WorkspaceRelativePath.Create(structuralValue);
        return path.IsValid && ProjectEvidencePathPolicy.IsReserved(path.Value);
    }

    private static bool TryTokenize(string value, out string structuralValue)
    {
        var output = new StringBuilder(value.Length);
        var position = 0;
        while (position < value.Length)
        {
            var open = value.IndexOf("{{", position, StringComparison.Ordinal);
            var strayClose = value.IndexOf("}}", position, StringComparison.Ordinal);
            if (strayClose >= 0 && (open < 0 || strayClose < open))
            {
                structuralValue = string.Empty;
                return false;
            }

            if (open < 0)
            {
                output.Append(value, position, value.Length - position);
                structuralValue = output.ToString();
                return true;
            }

            output.Append(value, position, open - position);
            var close = value.IndexOf("}}", open + 2, StringComparison.Ordinal);
            if (close < 0)
            {
                structuralValue = string.Empty;
                return false;
            }

            var identifier = value[(open + 2)..close].Trim();
            if (!IsKnownVariable(identifier))
            {
                structuralValue = string.Empty;
                return false;
            }

            output.Append("value");
            position = close + 2;
        }

        structuralValue = output.ToString();
        return true;
    }

    private static bool IsKnownVariable(string identifier)
    {
        if (identifier.Length == 0
            || identifier.Contains('{')
            || identifier.Contains('}')
            || RedactedText.IsSecretShapedKey(identifier))
        {
            return false;
        }

        return PlanningVariableIdentifierPolicy.IsAllowed(identifier);
    }

    private static BlueprintInspectionIssue Issue(string code, string summary)
    {
        return BlueprintInspectionIssue.Create(code, summary).Value;
    }

    private static ActionDescriptor Descriptor(
        bool builtInOnly,
        params (string Name, ParameterKind Kind)[] parameters)
    {
        return new ActionDescriptor(
            builtInOnly,
            parameters.ToImmutableDictionary(
                parameter => parameter.Name,
                parameter => parameter.Kind,
                StringComparer.Ordinal));
    }

    private sealed record ActionDescriptor(
        bool BuiltInOnly,
        ImmutableDictionary<string, ParameterKind> Parameters);

    private enum ParameterKind
    {
        Boolean = 1,
        Text = 2,
        Identifier = 3,
        Path = 4,
        Sequence = 5,
        Map = 6,
        TextSequence = 7,
        IntegerSequence = 8,
    }
}
