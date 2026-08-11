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
            case ScriptEndStatement:
                return;
            case ScriptIfStatement conditional:
                ValidateCondition(conditional.Condition, depth + 1);
                ValidateBlock(conditional.Then, depth + 1);
                ValidateElse(conditional.Else, depth + 1);
                return;
            default:
                throw new ForbiddenTemplateConstructException();
        }
    }

    private void ValidateElse(ScriptConditionStatement? statement, int depth)
    {
        if (statement is null)
        {
            return;
        }

        Enter(depth);
        switch (statement)
        {
            case ScriptElseStatement otherwise:
                ValidateBlock(otherwise.Body, depth + 1);
                return;
            case ScriptIfStatement conditional when conditional.IsElseIf:
                ValidateCondition(conditional.Condition, depth + 1);
                ValidateBlock(conditional.Then, depth + 1);
                ValidateElse(conditional.Else, depth + 1);
                return;
            default:
                throw new ForbiddenTemplateConstructException();
        }
    }

    private void ValidateCondition(ScriptExpression? expression, int depth)
    {
        Enter(depth);
        switch (expression)
        {
            case ScriptLiteral { Value: bool }:
                return;
            case ScriptNestedExpression nested:
                ValidateCondition(nested.Expression, depth + 1);
                return;
            case ScriptUnaryExpression { Operator: ScriptUnaryOperator.Not } unary:
                ValidateCondition(unary.Right, depth + 1);
                return;
            case ScriptBinaryExpression binary when
                binary.Operator is ScriptBinaryOperator.And or ScriptBinaryOperator.Or:
                ValidateCondition(binary.Left, depth + 1);
                ValidateCondition(binary.Right, depth + 1);
                return;
            case ScriptBinaryExpression binary when
                binary.Operator is ScriptBinaryOperator.CompareEqual or
                    ScriptBinaryOperator.CompareNotEqual:
                ValidateComparablePair(binary.Left, binary.Right, depth + 1);
                return;
            default:
                throw new ForbiddenTemplateConstructException();
        }
    }

    private void ValidateComparablePair(
        ScriptExpression? left,
        ScriptExpression? right,
        int depth)
    {
        Enter(depth);
        if (left is ScriptLiteral { Value: bool } && right is ScriptLiteral { Value: bool })
        {
            return;
        }

        if (IsStringComparable(left, depth + 1) && IsStringComparable(right, depth + 1))
        {
            return;
        }

        throw new ForbiddenTemplateConstructException();
    }

    private bool IsStringComparable(ScriptExpression? expression, int depth)
    {
        Enter(depth);
        switch (expression)
        {
            case ScriptLiteral { Value: string }:
                return true;
            case ScriptVariableGlobal:
                return true;
            case ScriptMemberExpression member:
                ValidateVariablePath(member, depth + 1);
                return true;
            default:
                return false;
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
