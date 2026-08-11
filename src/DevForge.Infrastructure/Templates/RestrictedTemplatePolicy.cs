using Scriban.Parsing;
using Scriban.Syntax;

namespace DevForge.Infrastructure.Templates;

internal sealed class RestrictedTemplatePolicy
{
    public const int MaximumNodeCount = 10_000;
    public const int MaximumDepth = 64;

    private int _nodeCount;

    public static void Validate(ScriptPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (page.FrontMatter is not null)
        {
            throw new ForbiddenTemplateConstructException();
        }

        new RestrictedTemplatePolicy().ValidateBlock(page.Body, depth: 1);
    }

    private void ValidateBlock(ScriptBlockStatement? block, int depth)
    {
        if (block is null)
        {
            return;
        }

        Enter(depth);
        foreach (var statement in block.Statements)
        {
            ValidateStatement(statement, depth + 1);
        }
    }

    private void ValidateStatement(ScriptStatement statement, int depth)
    {
        Enter(depth);
        switch (statement)
        {
            case ScriptRawStatement:
                return;
            case ScriptEscapeStatement escape when
                escape.EscapeCount == 0 &&
                escape.WhitespaceMode == ScriptWhitespaceMode.None:
                return;
            case ScriptExpressionStatement expression:
                ValidateOutput(expression.Expression, depth + 1);
                return;
            default:
                throw new ForbiddenTemplateConstructException();
        }
    }

    private void ValidateOutput(ScriptExpression? expression, int depth)
    {
        Enter(depth);
        switch (expression)
        {
            case ScriptVariableGlobal:
                return;
            case ScriptMemberExpression member:
                ValidateVariablePath(member, depth + 1);
                return;
            case ScriptLiteral { Value: string or bool }:
                return;
            default:
                throw new ForbiddenTemplateConstructException();
        }
    }

    private void ValidateVariablePath(ScriptExpression? expression, int depth)
    {
        Enter(depth);
        switch (expression)
        {
            case ScriptVariableGlobal:
                return;
            case ScriptMemberExpression member when
                member.DotToken.TokenType == TokenType.Dot &&
                member.Member is ScriptVariableGlobal:
                ValidateVariablePath(member.Target, depth + 1);
                return;
            default:
                throw new ForbiddenTemplateConstructException();
        }
    }

    private void Enter(int depth)
    {
        _nodeCount++;
        if (_nodeCount > MaximumNodeCount || depth > MaximumDepth)
        {
            throw new ForbiddenTemplateConstructException();
        }
    }
}
