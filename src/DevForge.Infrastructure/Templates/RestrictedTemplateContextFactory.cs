using DevForge.Application.Contracts;
using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;
using Scriban.Syntax;

namespace DevForge.Infrastructure.Templates;

internal static class RestrictedTemplateContextFactory
{
    public static TemplateContext Create(
        TemplateRenderRequest request,
        IScriptOutput output,
        CancellationToken cancellationToken)
    {
        var builtins = new ScriptObject
        {
            IsReadOnly = true,
        };
        var context = new TemplateContext(builtins, StringComparer.Ordinal)
        {
            CancellationToken = cancellationToken,
            StrictVariables = true,
            EnableRelaxedTargetAccess = false,
            EnableRelaxedMemberAccess = false,
            EnableRelaxedFunctionAccess = false,
            EnableRelaxedIndexerAccess = false,
            RecursiveLimit = RestrictedTemplatePolicy.MaximumDepth,
            LimitToString = 0,
            TemplateLoader = null,
        };
        context.TryGetVariable = MissingVariable;
        context.TryGetMember = MissingMember;
        context.PushGlobal(CreateFrozenGlobals(request));
        context.PushOutput(output);
        return context;
    }

    private static ScriptObject CreateFrozenGlobals(TemplateRenderRequest request)
    {
        var root = new ContextNode();
        foreach (var pair in request.Context.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var node = root;
            var segments = pair.Key.Split('.');
            for (var index = 0; index < segments.Length; index++)
            {
                if (!node.Children.TryGetValue(segments[index], out var child))
                {
                    child = new ContextNode();
                    node.Children.Add(segments[index], child);
                }

                node = child;
            }

            node.Value = pair.Value;
        }

        return CreateScriptObject(root);
    }

    private static ScriptObject CreateScriptObject(ContextNode node)
    {
        var result = new ScriptObject(node.Children.Count);
        foreach (var child in node.Children.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            object value = child.Value.Children.Count == 0
                ? child.Value.Value!
                : CreateScriptObject(child.Value);
            result.SetValue(child.Key, value, readOnly: true);
        }

        result.IsReadOnly = true;
        return result;
    }

    private static bool MissingVariable(
        TemplateContext context,
        SourceSpan span,
        ScriptVariable variable,
        out object? value)
    {
        value = null;
        throw new MissingTemplateVariableException();
    }

    private static bool MissingMember(
        TemplateContext context,
        SourceSpan span,
        object target,
        string member,
        out object? value)
    {
        value = null;
        throw new MissingTemplateVariableException();
    }

    private sealed class ContextNode
    {
        public SortedDictionary<string, ContextNode> Children { get; } = new(StringComparer.Ordinal);

        public string? Value { get; set; }
    }
}
