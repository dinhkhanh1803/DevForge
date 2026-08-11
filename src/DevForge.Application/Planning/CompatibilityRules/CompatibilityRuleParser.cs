using System.Collections.Immutable;
using System.Globalization;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Validation;

namespace DevForge.Application.Planning.CompatibilityRules;

public sealed class CompatibilityRuleParser
{
    public const int MaximumInputCharacters = 16384;
    public const int MaximumTokens = 512;
    public const int MaximumDepth = 64;
    public const int MaximumListItems = 128;

    private ImmutableArray<RuleToken> _tokens;
    private int _position;
    private int _parenthesisDepth;
    private readonly object _sync = new();

    public ValidationResult<CompatibilityExpression> Parse(string? expression)
    {
        lock (_sync)
        {
            return ParseCore(expression);
        }
    }

    private ValidationResult<CompatibilityExpression> ParseCore(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression)
            || expression.Length > MaximumInputCharacters)
        {
            return Failure();
        }

        try
        {
            _tokens = Tokenize(expression);
            _position = 0;
            _parenthesisDepth = 0;
            var result = ParseOr();
            Require(RuleTokenKind.End);
            if (GetDepth(result) > MaximumDepth)
            {
                throw new RuleSyntaxException();
            }

            return ValidationResult.Success(result);
        }
        catch (RuleSyntaxException)
        {
            return Failure();
        }
    }

    private CompatibilityExpression ParseOr()
    {
        var expression = ParseAnd();
        while (Match(RuleTokenKind.Or))
        {
            expression = new LogicalCompatibilityExpression(
                CompatibilityLogicalOperator.Or,
                expression,
                ParseAnd());
        }

        return expression;
    }

    private CompatibilityExpression ParseAnd()
    {
        var expression = ParsePrimary();
        while (Match(RuleTokenKind.And))
        {
            expression = new LogicalCompatibilityExpression(
                CompatibilityLogicalOperator.And,
                expression,
                ParsePrimary());
        }

        return expression;
    }

    private CompatibilityExpression ParsePrimary()
    {
        if (Match(RuleTokenKind.LeftParenthesis))
        {
            _parenthesisDepth++;
            if (_parenthesisDepth > MaximumDepth)
            {
                throw new RuleSyntaxException();
            }

            var expression = ParseOr();
            Require(RuleTokenKind.RightParenthesis);
            _parenthesisDepth--;
            return expression;
        }

        return ParseComparison();
    }

    private ComparisonCompatibilityExpression ParseComparison()
    {
        var left = ParseOperand();
        var operation = Current.Kind switch
        {
            RuleTokenKind.Equal => CompatibilityComparisonOperator.Equal,
            RuleTokenKind.NotEqual => CompatibilityComparisonOperator.NotEqual,
            RuleTokenKind.In => CompatibilityComparisonOperator.In,
            RuleTokenKind.NotIn => CompatibilityComparisonOperator.NotIn,
            RuleTokenKind.Satisfies => CompatibilityComparisonOperator.Satisfies,
            _ => throw new RuleSyntaxException(),
        };
        _position++;
        return new ComparisonCompatibilityExpression(operation, left, ParseOperand());
    }

    private CompatibilityOperand ParseOperand()
    {
        if (Match(RuleTokenKind.LeftBracket))
        {
            var values = ImmutableArray.CreateBuilder<PlanningRuleValue>();
            if (Current.Kind == RuleTokenKind.RightBracket)
            {
                throw new RuleSyntaxException();
            }

            while (true)
            {
                values.Add(ParseLiteral());
                if (values.Count > MaximumListItems)
                {
                    throw new RuleSyntaxException();
                }

                if (!Match(RuleTokenKind.Comma))
                {
                    break;
                }

                if (Current.Kind == RuleTokenKind.RightBracket)
                {
                    throw new RuleSyntaxException();
                }
            }

            Require(RuleTokenKind.RightBracket);
            return new ListCompatibilityOperand(values.ToImmutable());
        }

        if (Current.Kind == RuleTokenKind.Identifier)
        {
            var identifier = Current.Value!;
            _position++;
            if (!PlanningRuleIdentifierPolicy.IsAllowed(identifier))
            {
                throw new RuleSyntaxException();
            }

            return new IdentifierCompatibilityOperand(identifier);
        }

        return new LiteralCompatibilityOperand(ParseLiteral());
    }

    private PlanningRuleValue ParseLiteral()
    {
        var token = Current;
        _position++;
        return token.Kind switch
        {
            RuleTokenKind.String => ParseText(token.Value!),
            RuleTokenKind.True => PlanningRuleValue.FromBoolean(true),
            RuleTokenKind.False => PlanningRuleValue.FromBoolean(false),
            RuleTokenKind.Integer => ParseInteger(token.Value!),
            _ => throw new RuleSyntaxException(),
        };
    }

    private static PlanningRuleValue ParseText(string value)
    {
        var result = PlanningRuleValue.FromText(value);
        return result.IsValid ? result.Value : throw new RuleSyntaxException();
    }

    private static PlanningRuleValue ParseInteger(string value)
    {
        var digits = value[0] == '-' ? value[1..] : value;
        if (digits.Length > 1 && digits[0] == '0'
            || !long.TryParse(
                value,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var number))
        {
            throw new RuleSyntaxException();
        }

        return PlanningRuleValue.FromInteger(number);
    }

    private bool Match(RuleTokenKind kind)
    {
        if (Current.Kind != kind)
        {
            return false;
        }

        _position++;
        return true;
    }

    private void Require(RuleTokenKind kind)
    {
        if (!Match(kind))
        {
            throw new RuleSyntaxException();
        }
    }

    private RuleToken Current => _position < _tokens.Length
        ? _tokens[_position]
        : throw new RuleSyntaxException();

    private static int GetDepth(CompatibilityExpression expression)
    {
        return expression switch
        {
            LogicalCompatibilityExpression logical =>
                1 + Math.Max(GetDepth(logical.Left), GetDepth(logical.Right)),
            ComparisonCompatibilityExpression => 1,
            _ => throw new RuleSyntaxException(),
        };
    }

    private static ImmutableArray<RuleToken> Tokenize(string expression)
    {
        var tokens = ImmutableArray.CreateBuilder<RuleToken>();
        var position = 0;
        while (position < expression.Length)
        {
            if (char.IsWhiteSpace(expression[position]))
            {
                position++;
                continue;
            }

            var character = expression[position];
            switch (character)
            {
                case '(':
                    Add(RuleTokenKind.LeftParenthesis);
                    position++;
                    break;
                case ')':
                    Add(RuleTokenKind.RightParenthesis);
                    position++;
                    break;
                case '[':
                    Add(RuleTokenKind.LeftBracket);
                    position++;
                    break;
                case ']':
                    Add(RuleTokenKind.RightBracket);
                    position++;
                    break;
                case ',':
                    Add(RuleTokenKind.Comma);
                    position++;
                    break;
                case '&' when HasNext('&'):
                    Add(RuleTokenKind.And);
                    position += 2;
                    break;
                case '|' when HasNext('|'):
                    Add(RuleTokenKind.Or);
                    position += 2;
                    break;
                case '=' when HasNext('='):
                    Add(RuleTokenKind.Equal);
                    position += 2;
                    break;
                case '!' when HasNext('='):
                    Add(RuleTokenKind.NotEqual);
                    position += 2;
                    break;
                case '\'' or '"':
                    tokens.Add(ReadString(expression, ref position));
                    CheckTokenBound();
                    break;
                default:
                    if (character is >= '0' and <= '9'
                        || character == '-'
                            && position + 1 < expression.Length
                            && expression[position + 1] is >= '0' and <= '9')
                    {
                        tokens.Add(ReadInteger(expression, ref position));
                        CheckTokenBound();
                    }
                    else if (IsIdentifierStart(character))
                    {
                        tokens.Add(ReadIdentifier(expression, ref position));
                        CheckTokenBound();
                    }
                    else
                    {
                        throw new RuleSyntaxException();
                    }

                    break;
            }
        }

        tokens.Add(new RuleToken(RuleTokenKind.End, null));
        return tokens.ToImmutable();

        bool HasNext(char expected)
        {
            return position + 1 < expression.Length && expression[position + 1] == expected;
        }

        void Add(RuleTokenKind kind)
        {
            tokens.Add(new RuleToken(kind, null));
            CheckTokenBound();
        }

        void CheckTokenBound()
        {
            if (tokens.Count > MaximumTokens)
            {
                throw new RuleSyntaxException();
            }
        }
    }

    private static RuleToken ReadString(string expression, ref int position)
    {
        var quote = expression[position++];
        var value = new System.Text.StringBuilder();
        while (position < expression.Length)
        {
            var character = expression[position++];
            if (character == quote)
            {
                var result = value.ToString();
                if (result.Contains("${", StringComparison.Ordinal)
                    || result.Contains("{{", StringComparison.Ordinal))
                {
                    throw new RuleSyntaxException();
                }

                return new RuleToken(RuleTokenKind.String, result);
            }

            if (char.IsControl(character))
            {
                throw new RuleSyntaxException();
            }

            if (character == '\\')
            {
                if (position >= expression.Length
                    || expression[position] is not ('\\' or '\'' or '"'))
                {
                    throw new RuleSyntaxException();
                }

                character = expression[position++];
            }

            value.Append(character);
            if (value.Length > PlanningRuleValueLimits.MaximumTextCharacters)
            {
                throw new RuleSyntaxException();
            }
        }

        throw new RuleSyntaxException();
    }

    private static RuleToken ReadInteger(string expression, ref int position)
    {
        var start = position;
        if (expression[position] == '-')
        {
            position++;
        }

        while (position < expression.Length && expression[position] is >= '0' and <= '9')
        {
            position++;
        }

        return new RuleToken(RuleTokenKind.Integer, expression[start..position]);
    }

    private static RuleToken ReadIdentifier(string expression, ref int position)
    {
        var start = position;
        while (position < expression.Length && IsIdentifierPart(expression[position]))
        {
            position++;
        }

        var value = expression[start..position];
        return value switch
        {
            "true" => new RuleToken(RuleTokenKind.True, value),
            "false" => new RuleToken(RuleTokenKind.False, value),
            "in" => new RuleToken(RuleTokenKind.In, value),
            "not-in" => new RuleToken(RuleTokenKind.NotIn, value),
            "satisfies" => new RuleToken(RuleTokenKind.Satisfies, value),
            _ => new RuleToken(RuleTokenKind.Identifier, value),
        };
    }

    private static bool IsIdentifierStart(char value) =>
        value is >= 'a' and <= 'z' or >= 'A' and <= 'Z';

    private static bool IsIdentifierPart(char value) =>
        IsIdentifierStart(value) || value is >= '0' and <= '9' or '.' or '-';

    private static ValidationResult<CompatibilityExpression> Failure()
    {
        return ValidationResult.Failure<CompatibilityExpression>(
        [
            new ValidationIssue(
                "DF-PLAN-001",
                "The compatibility expression is malformed, unsupported, or exceeds a bound.",
                "expression"),
        ]);
    }

    private readonly record struct RuleToken(RuleTokenKind Kind, string? Value);

    private enum RuleTokenKind
    {
        Identifier = 1,
        String = 2,
        Integer = 3,
        True = 4,
        False = 5,
        Equal = 6,
        NotEqual = 7,
        In = 8,
        NotIn = 9,
        Satisfies = 10,
        And = 11,
        Or = 12,
        LeftParenthesis = 13,
        RightParenthesis = 14,
        LeftBracket = 15,
        RightBracket = 16,
        Comma = 17,
        End = 18,
    }

    private sealed class RuleSyntaxException : Exception;
}

internal static class PlanningRuleValueLimits
{
    internal const int MaximumTextCharacters = BlueprintValue.MaximumTextLength;
}
