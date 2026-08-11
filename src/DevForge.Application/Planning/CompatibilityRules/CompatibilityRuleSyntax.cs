using System.Collections.Immutable;

namespace DevForge.Application.Planning.CompatibilityRules;

public abstract record CompatibilityExpression;

internal sealed record LogicalCompatibilityExpression(
    CompatibilityLogicalOperator Operator,
    CompatibilityExpression Left,
    CompatibilityExpression Right) : CompatibilityExpression;

internal sealed record ComparisonCompatibilityExpression(
    CompatibilityComparisonOperator Operator,
    CompatibilityOperand Left,
    CompatibilityOperand Right) : CompatibilityExpression;

internal abstract record CompatibilityOperand;

internal sealed record IdentifierCompatibilityOperand(string Identifier) : CompatibilityOperand;

internal sealed record LiteralCompatibilityOperand(PlanningRuleValue Value) : CompatibilityOperand;

internal sealed record ListCompatibilityOperand(
    ImmutableArray<PlanningRuleValue> Values) : CompatibilityOperand;

internal enum CompatibilityLogicalOperator
{
    And = 1,
    Or = 2,
}

internal enum CompatibilityComparisonOperator
{
    Equal = 1,
    NotEqual = 2,
    In = 3,
    NotIn = 4,
    Satisfies = 5,
}
