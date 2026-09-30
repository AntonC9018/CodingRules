using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace CodingRules;

internal sealed class ConditionFacts
{
    public readonly List<ExpressionSyntax> Leaves = new();
    public readonly HashSet<string> Reasons = new(StringComparer.Ordinal);
    private bool hasAnd;
    private bool hasOr;
    public bool HasReason => Reasons.Count != 0;

    public static ConditionFacts? Create(ExpressionSyntax expression, SemanticModel model, CancellationToken token)
    {
        var type = model.GetTypeInfo(expression, token);
        if (type.Type?.SpecialType != SpecialType.System_Boolean
            || !SymbolEqualityComparer.Default.Equals(type.Type, type.ConvertedType))
        {
            return null;
        }

        var facts = new ConditionFacts();
        if (!facts.Collect(expression, model, token))
        {
            return null;
        }

        if (facts.hasAnd && facts.hasOr)
        {
            facts.Reasons.Add(DiagnosticIds.MixedConditionOperators);
        }

        if (facts.Leaves.Count > 2)
        {
            facts.Reasons.Add(DiagnosticIds.ExcessConditionChecks);
        }

        if (facts.IsRepeated(model))
        {
            facts.Reasons.Add(DiagnosticIds.RepeatedConditionAlternatives);
        }

        var producing = facts.Leaves.Where(leaf => HasProducingOperation(leaf, model)).ToList();
        if (producing.Any(leaf => HasValidation(leaf, model))
            || producing.Count != 0 && facts.Leaves.Any(leaf => !producing.Contains(leaf)
                && HasValidation(leaf, model) && !IsNullGuard(leaf)))
        {
            facts.Reasons.Add(DiagnosticIds.CombinedConditionOperations);
        }

        if (facts.Leaves.Count == 2 && !facts.IsInvariant(model))
        {
            var first = Subject(facts.Leaves[0], model);
            var second = Subject(facts.Leaves[1], model);
            if (first is not null && second is not null && !first.Matches(second))
            {
                facts.Reasons.Add(DiagnosticIds.IndependentConditionChecks);
            }
        }

        return facts;
    }

    private bool Collect(ExpressionSyntax expression, SemanticModel model, CancellationToken token)
    {
        expression = Unwrap(expression);
        if (expression is BinaryExpressionSyntax binary && binary.Kind() is SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression)
        {
            if (model.GetOperation(binary, token) is not IBinaryOperation { OperatorMethod: null } operation
                || operation.Type?.SpecialType != SpecialType.System_Boolean
                || operation.LeftOperand.Type?.SpecialType != SpecialType.System_Boolean
                || operation.RightOperand.Type?.SpecialType != SpecialType.System_Boolean)
            {
                return false;
            }

            hasAnd |= binary.IsKind(SyntaxKind.LogicalAndExpression);
            hasOr |= binary.IsKind(SyntaxKind.LogicalOrExpression);
            return Collect(binary.Left, model, token) && Collect(binary.Right, model, token);
        }

        if (expression is PrefixUnaryExpressionSyntax unary && unary.IsKind(SyntaxKind.LogicalNotExpression))
        {
            if (model.GetOperation(unary, token) is not IUnaryOperation { OperatorMethod: null, Type.SpecialType: SpecialType.System_Boolean })
            {
                return false;
            }

            return Collect(unary.Operand, model, token);
        }

        if (expression is BinaryExpressionSyntax eager && eager.Kind() is SyntaxKind.BitwiseAndExpression or SyntaxKind.BitwiseOrExpression or SyntaxKind.ExclusiveOrExpression)
        {
            return false;
        }

        if (model.GetOperation(expression, token) is null || model.GetTypeInfo(expression, token).Type?.SpecialType != SpecialType.System_Boolean)
        {
            return false;
        }

        Leaves.Add(expression);
        return true;
    }

    public static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parentheses)
        {
            expression = parentheses.Expression;
        }

        return expression;
    }

    internal static IEnumerable<SyntaxNode> EvaluationNodes(ExpressionSyntax expression) =>
        expression.DescendantNodesAndSelf(node => node is not AnonymousFunctionExpressionSyntax);

    internal static bool IsProducer(SyntaxNode node, SemanticModel model)
    {
        if (node is AssignmentExpressionSyntax || node is PostfixUnaryExpressionSyntax postfix
                && postfix.Kind() is SyntaxKind.PostIncrementExpression or SyntaxKind.PostDecrementExpression
            || node is PrefixUnaryExpressionSyntax prefix
                && prefix.Kind() is SyntaxKind.PreIncrementExpression or SyntaxKind.PreDecrementExpression)
        {
            return true;
        }

        if (node is CastExpressionSyntax cast && model.GetOperation(cast) is IConversionOperation { OperatorMethod: null } conversion
            && conversion.Type?.IsValueType == true && conversion.Operand.Type?.IsValueType == true)
        {
            return true;
        }

        if (node is not InvocationExpressionSyntax invocation || model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
        {
            return false;
        }

        var type = method.ContainingType.OriginalDefinition.ToDisplayString();
        if (invocation.ArgumentList.Arguments.Any(argument => !argument.RefKindKeyword.IsKind(SyntaxKind.None)))
        {
            return true;
        }

        if (!IsFramework(method))
        {
            return false;
        }

        if (method.Name is "Parse" or "TryParse")
        {
            return method.ContainingType.SpecialType is >= SpecialType.System_SByte and <= SpecialType.System_Decimal
                || type == "System.Guid";
        }

        if (method.Name == "TryGetValue")
        {
            return type is "System.Collections.Generic.Dictionary<TKey, TValue>"
                or "System.Collections.Generic.IDictionary<TKey, TValue>"
                or "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>";
        }

        if (method.Name == "TryGetProperty" && type == "System.Text.Json.JsonElement")
        {
            return true;
        }

        if (type == "System.Convert" && method.Name.StartsWith("To", StringComparison.Ordinal))
        {
            return method.ReturnType.SpecialType is >= SpecialType.System_Boolean and <= SpecialType.System_Decimal;
        }

        return method.ContainingType.SpecialType == SpecialType.System_String
            && method.Name is "Trim" or "TrimStart" or "TrimEnd" or "Replace";
    }

    private static bool IsFramework(IMethodSymbol method) =>
        method.ContainingAssembly.Identity.Name is "mscorlib" or "System.Private.CoreLib" or "System.Runtime"
            or "System.Collections" or "System.Text.Json" or "netstandard";

    private static bool HasProducingOperation(ExpressionSyntax leaf, SemanticModel model) =>
        EvaluationNodes(leaf).Any(node => IsProducer(node, model));

    private static bool HasValidation(ExpressionSyntax leaf, SemanticModel model) =>
        leaf is BinaryExpressionSyntax binary && binary.Kind() is SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression
            or SyntaxKind.LessThanExpression or SyntaxKind.LessThanOrEqualExpression
            or SyntaxKind.GreaterThanExpression or SyntaxKind.GreaterThanOrEqualExpression
        || leaf is IsPatternExpressionSyntax
        || leaf is MemberAccessExpressionSyntax && model.GetTypeInfo(leaf).Type?.SpecialType == SpecialType.System_Boolean
        || leaf is InvocationExpressionSyntax invocation && IsPredicate(invocation, model);

    internal static bool IsPredicate(InvocationExpressionSyntax invocation, SemanticModel model)
    {
        if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method || !IsFramework(method))
        {
            return false;
        }

        return method.ContainingType.SpecialType == SpecialType.System_String
                && method.Name is "Equals" or "StartsWith" or "EndsWith" or "Contains" or "IsNullOrEmpty" or "IsNullOrWhiteSpace"
            || method.ContainingType.SpecialType is SpecialType.System_Single or SpecialType.System_Double
                && method.Name is "IsFinite" or "IsNaN";
    }

    private bool IsRepeated(SemanticModel model)
    {
        if (Leaves.Count < 3 || hasAnd == hasOr)
        {
            return false;
        }

        // AND alternatives must each be negated; do not lose the original grouping.
        var calls = new List<InvocationExpressionSyntax>();
        foreach (var leaf in Leaves)
        {
            if (leaf is not InvocationExpressionSyntax call || !IsPredicate(call, model))
            {
                return false;
            }

            var parent = leaf.Parent;
            while (parent is ParenthesizedExpressionSyntax)
            {
                parent = parent.Parent;
            }

            if (hasAnd != (parent is PrefixUnaryExpressionSyntax unary && unary.IsKind(SyntaxKind.LogicalNotExpression)))
            {
                return false;
            }

            calls.Add(call);
        }

        var first = calls[0];
        var method = model.GetSymbolInfo(first).Symbol;
        if (calls.Any(call => !SymbolEqualityComparer.Default.Equals(method, model.GetSymbolInfo(call).Symbol)
            || call.Expression.ToString() != first.Expression.ToString()
            || call.ArgumentList.Arguments.Count != first.ArgumentList.Arguments.Count))
        {
            return false;
        }

        var differing = 0;
        for (var index = 0; index < first.ArgumentList.Arguments.Count; index++)
        {
            var values = calls.Select(call => model.GetConstantValue(call.ArgumentList.Arguments[index].Expression)).ToList();
            if (calls.All(call => call.ArgumentList.Arguments[index].ToString() == first.ArgumentList.Arguments[index].ToString()))
            {
                continue;
            }

            if (values.Any(value => !value.HasValue))
            {
                return false;
            }

            differing++;
        }

        return differing == 1;
    }

    private bool IsInvariant(SemanticModel model)
    {
        var first = Leaves[0];
        var second = Leaves[1];
        var subject = Subject(first, model);
        if (IsNullGuard(first) && subject is not null
            && EvaluationNodes(second).OfType<ExpressionSyntax>().Any(node => subject.Matches(Subject(node, model))))
        {
            return true;
        }

        if (subject is null || !subject.Matches(Subject(second, model)))
        {
            return false;
        }

        if (first is InvocationExpressionSyntax invocation && IsPredicate(invocation, model)
            && model.GetSymbolInfo(invocation).Symbol is IMethodSymbol { Name: "IsFinite" or "IsNaN" })
        {
            return true;
        }

        return first is BinaryExpressionSyntax firstComparison && second is BinaryExpressionSyntax secondComparison
            && OrderedDirection(firstComparison, model) * OrderedDirection(secondComparison, model) == -1;
    }

    private static int OrderedDirection(BinaryExpressionSyntax comparison, SemanticModel model)
    {
        var direction = comparison.Kind() switch
        {
            SyntaxKind.LessThanExpression or SyntaxKind.LessThanOrEqualExpression => -1,
            SyntaxKind.GreaterThanExpression or SyntaxKind.GreaterThanOrEqualExpression => 1,
            _ => 0,
        };
        return IsBound(comparison.Left, model) ? -direction : direction;
    }

    private static bool IsNullGuard(ExpressionSyntax expression) =>
        expression is IsPatternExpressionSyntax pattern && pattern.Pattern.ToString() is "null" or "not null"
        || expression is BinaryExpressionSyntax binary && (binary.Left.IsKind(SyntaxKind.NullLiteralExpression)
            || binary.Right.IsKind(SyntaxKind.NullLiteralExpression));

    private static bool IsBound(ExpressionSyntax expression, SemanticModel model) =>
        model.GetConstantValue(expression).HasValue
        || expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Length" or "Count" }
        || expression is BinaryExpressionSyntax binary && model.GetOperation(binary) is IBinaryOperation { OperatorMethod: null }
            && model.GetConstantValue(binary.Left).HasValue && model.GetSymbolInfo(binary.Right).Symbol is ILocalSymbol or IParameterSymbol;

    private static ConditionSubject? Subject(ExpressionSyntax expression, SemanticModel model)
    {
        expression = Unwrap(expression);
        if (expression is BinaryExpressionSyntax binary)
        {
            if (binary.Kind() is not (SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression or SyntaxKind.LessThanExpression
                or SyntaxKind.LessThanOrEqualExpression or SyntaxKind.GreaterThanExpression or SyntaxKind.GreaterThanOrEqualExpression)) return null;
            if (model.GetOperation(binary) is not IBinaryOperation { OperatorMethod: null }) return null;
            if (IsBound(binary.Right, model))
            {
                return Subject(binary.Left, model);
            }

            return IsBound(binary.Left, model) ? Subject(binary.Right, model) : null;
        }

        if (expression is IsPatternExpressionSyntax pattern)
        {
            if (pattern.Pattern.DescendantNodesAndSelf().Any(node => node is VariableDesignationSyntax)) return null;
            return Subject(pattern.Expression, model);
        }

        if (expression is InvocationExpressionSyntax invocation && IsPredicate(invocation, model))
        {
            if (invocation.Expression is MemberAccessExpressionSyntax access
                && model.GetSymbolInfo(invocation).Symbol is IMethodSymbol { IsStatic: false })
            {
                return Subject(access.Expression, model);
            }

            return invocation.ArgumentList.Arguments.Count > 0 ? Subject(invocation.ArgumentList.Arguments[0].Expression, model) : null;
        }

        return ConditionSubject.Create(expression, model);
    }
}
