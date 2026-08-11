using System.Collections.Immutable;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Blueprints.Abstractions.Validation;
using DevForge.Domain.Privacy;
using DevForge.Domain.Validation;

namespace DevForge.Application.Planning.CompatibilityRules;

public interface ICompatibilityRuleEvaluator
{
    ValidationResult<bool> Evaluate(
        CompatibilityExpression? expression,
        PlanningRuleContext? context,
        CancellationToken cancellationToken);

    ValidationResult<CompatibilityRuleEvaluation> EvaluateRules(
        IEnumerable<CompatibilityRule?>? rules,
        PlanningRuleContext? context,
        CancellationToken cancellationToken);
}

public sealed class CompatibilityRuleEvaluator : ICompatibilityRuleEvaluator
{
    public ValidationResult<bool> Evaluate(
        CompatibilityExpression? expression,
        PlanningRuleContext? context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (expression is null || context is null)
        {
            return Failure<bool>();
        }

        try
        {
            return ValidationResult.Success(EvaluateExpression(expression, context, cancellationToken));
        }
        catch (RuleEvaluationException)
        {
            return Failure<bool>();
        }
    }

    public ValidationResult<CompatibilityRuleEvaluation> EvaluateRules(
        IEnumerable<CompatibilityRule?>? rules,
        PlanningRuleContext? context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = rules?.ToImmutableArray() ?? [];
        if (rules is null
            || context is null
            || snapshot.Length > BlueprintValue.MaximumCollectionItems
            || snapshot.Any(rule => rule is null))
        {
            return Failure<CompatibilityRuleEvaluation>();
        }

        var parser = new CompatibilityRuleParser();
        var findings = ImmutableArray.CreateBuilder<CompatibilityRuleFinding>();
        foreach (var rule in snapshot.Select(item => item!))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var message = RedactedText.FromTrustedRedaction(rule.Message);
            var remediation = rule.Remediation is null
                ? null
                : RedactedText.FromTrustedRedaction(rule.Remediation);
            if (!BlueprintIdentifierValidator.IsValid(rule.Id)
                || !Enum.IsDefined(rule.Severity)
                || rule.Override != CompatibilityRuleOverride.None
                || !message.IsValid
                || remediation is { IsValid: false })
            {
                return Failure<CompatibilityRuleEvaluation>();
            }

            var parsed = parser.Parse(rule.Expression);
            if (!parsed.IsValid)
            {
                return Failure<CompatibilityRuleEvaluation>();
            }

            var evaluation = Evaluate(parsed.Value, context, cancellationToken);
            if (!evaluation.IsValid)
            {
                return Failure<CompatibilityRuleEvaluation>();
            }

            if (!evaluation.Value)
            {
                findings.Add(new CompatibilityRuleFinding(
                    rule.Id.Trim(),
                    rule.Severity,
                    message.Value,
                    remediation?.Value));
            }
        }

        return ValidationResult.Success(new CompatibilityRuleEvaluation(findings.ToImmutable()));
    }

    private static bool EvaluateExpression(
        CompatibilityExpression expression,
        PlanningRuleContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return expression switch
        {
            LogicalCompatibilityExpression logical when logical.Operator == CompatibilityLogicalOperator.And =>
                EvaluateExpression(logical.Left, context, cancellationToken)
                && EvaluateExpression(logical.Right, context, cancellationToken),
            LogicalCompatibilityExpression logical when logical.Operator == CompatibilityLogicalOperator.Or =>
                EvaluateExpression(logical.Left, context, cancellationToken)
                || EvaluateExpression(logical.Right, context, cancellationToken),
            ComparisonCompatibilityExpression comparison => EvaluateComparison(comparison, context),
            _ => throw new RuleEvaluationException(),
        };
    }

    private static bool EvaluateComparison(
        ComparisonCompatibilityExpression comparison,
        PlanningRuleContext context)
    {
        var left = Resolve(comparison.Left, context);
        var right = Resolve(comparison.Right, context);
        return comparison.Operator switch
        {
            CompatibilityComparisonOperator.Equal => AreEqual(left, right),
            CompatibilityComparisonOperator.NotEqual => !AreEqual(left, right),
            CompatibilityComparisonOperator.In => IsIn(left, right),
            CompatibilityComparisonOperator.NotIn => !IsIn(left, right),
            CompatibilityComparisonOperator.Satisfies => Satisfies(left, right),
            _ => throw new RuleEvaluationException(),
        };
    }

    private static EvaluatedOperand Resolve(
        CompatibilityOperand operand,
        PlanningRuleContext context)
    {
        return operand switch
        {
            IdentifierCompatibilityOperand identifier when context.TryGetValue(
                identifier.Identifier,
                out var value) => EvaluatedOperand.FromScalar(value),
            IdentifierCompatibilityOperand => throw new RuleEvaluationException(),
            LiteralCompatibilityOperand literal => EvaluatedOperand.FromScalar(literal.Value),
            ListCompatibilityOperand list => EvaluatedOperand.FromList(list.Values),
            _ => throw new RuleEvaluationException(),
        };
    }

    private static bool AreEqual(EvaluatedOperand left, EvaluatedOperand right)
    {
        if (left.IsList
            || right.IsList
            || left.Scalar!.Kind != right.Scalar!.Kind)
        {
            throw new RuleEvaluationException();
        }

        return left.Scalar.Equals(right.Scalar);
    }

    private static bool IsIn(EvaluatedOperand left, EvaluatedOperand right)
    {
        if (left.IsList || !right.IsList || right.Values.IsEmpty
            || right.Values.Any(item => item.Kind != left.Scalar!.Kind))
        {
            throw new RuleEvaluationException();
        }

        return right.Values.Contains(left.Scalar!);
    }

    private static bool Satisfies(EvaluatedOperand left, EvaluatedOperand right)
    {
        if (left.IsList
            || right.IsList
            || left.Scalar!.Kind != PlanningRuleValueKind.SemanticVersion
            || right.Scalar!.Kind != PlanningRuleValueKind.Text
            || !SemanticVersionRange.TryParse(right.Scalar.Text, out var range))
        {
            throw new RuleEvaluationException();
        }

        return range.Contains(left.Scalar.SemanticVersion!);
    }

    private static ValidationResult<T> Failure<T>()
    {
        return ValidationResult.Failure<T>(
        [
            new ValidationIssue(
                "DF-PLAN-001",
                "The compatibility rule cannot be evaluated with the supported typed context.",
                "rule"),
        ]);
    }

    private readonly record struct EvaluatedOperand(
        PlanningRuleValue? Scalar,
        ImmutableArray<PlanningRuleValue> Values,
        bool IsList)
    {
        internal static EvaluatedOperand FromScalar(PlanningRuleValue value) =>
            new(value, [], IsList: false);

        internal static EvaluatedOperand FromList(ImmutableArray<PlanningRuleValue> values) =>
            new(null, values, IsList: true);
    }

    private sealed class RuleEvaluationException : Exception;
}
