using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace CodingRules;

internal static class CompoundPredicateFacts
{
    private const int MaximumCompactDepth = 2;

    public static bool ShouldReport(LambdaExpressionSyntax lambda, SemanticModel model, PredicateCatalog catalog) =>
        IsCompound(lambda, model, catalog)
        || ExceedsCompactDepth(lambda, model, catalog) && !HasCompactParent(lambda, model, catalog);

    public static bool ExceedsCompactDepth(LambdaExpressionSyntax lambda, SemanticModel model,
        PredicateCatalog catalog) => ExceedsCompactDepth(lambda, model, catalog, MaximumCompactDepth);

    private static bool ExceedsCompactDepth(LambdaExpressionSyntax lambda, SemanticModel model,
        PredicateCatalog catalog, int remaining)
    {
        if (lambda.Body is not ExpressionSyntax body || lambda.ContainsDiagnostics
            || !IsPredicate(lambda, model, catalog))
        {
            return false;
        }

        // An unrelated lambda, a block, or a named stage ends this compact chain.
        // Stop at the first excess level rather than measuring an unbounded tree.
        return remaining == 0 || EvaluationNodes(body).OfType<LambdaExpressionSyntax>()
            .Any(child => ExceedsCompactDepth(child, model, catalog, remaining - 1));
    }

    private static bool HasCompactParent(LambdaExpressionSyntax lambda, SemanticModel model,
        PredicateCatalog catalog) => lambda.Ancestors()
        .FirstOrDefault(node => node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)
        is LambdaExpressionSyntax { Body: ExpressionSyntax } parent && IsPredicate(parent, model, catalog);

    public static bool IsCompound(LambdaExpressionSyntax lambda, SemanticModel model, PredicateCatalog catalog)
    {
        if (lambda.Body is not ExpressionSyntax body || lambda.ContainsDiagnostics
            || !IsPredicate(lambda, model, catalog))
        {
            return false;
        }

        // A Boolean decision inside a child lambda belongs to that child. Merely
        // wrapping a simple Any in another Any does not create a compound stage.
        return EvaluationNodes(body).OfType<BinaryExpressionSyntax>()
            .Where(binary => IsBuiltinLogical(binary, model))
            .Any(binary => EvaluationNodes(binary).OfType<InvocationExpressionSyntax>()
                .Any(call => HasPredicateLambda(call, model, catalog)));
    }

    private static bool HasPredicateLambda(InvocationExpressionSyntax call, SemanticModel model,
        PredicateCatalog catalog) => call.ArgumentList.Arguments
        .Select(argument => Unwrap(argument.Expression)).OfType<LambdaExpressionSyntax>()
        .Any(lambda => IsPredicate(lambda, model, catalog));

    public static bool IsPredicate(LambdaExpressionSyntax lambda, SemanticModel model, PredicateCatalog catalog)
    {
        SyntaxNode value = lambda;
        while (value.Parent is ParenthesizedExpressionSyntax parentheses)
        {
            value = parentheses;
        }

        return value.Parent is ArgumentSyntax argument
            && argument.Parent?.Parent is InvocationExpressionSyntax call
            && model.GetOperation(call) is IInvocationOperation invocation
            && catalog.Contains(invocation.TargetMethod)
            && model.GetOperation(argument) is IArgumentOperation { Parameter.Type: INamedTypeSymbol parameter }
            && parameter.TypeKind == TypeKind.Delegate
            && parameter.DelegateInvokeMethod?.ReturnType.SpecialType == SpecialType.System_Boolean;
    }

    public static bool IsBuiltinLogical(BinaryExpressionSyntax binary, SemanticModel model) =>
        binary.Kind() is SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression
        && model.GetOperation(binary) is IBinaryOperation
        {
            OperatorMethod: null,
            Type.SpecialType: SpecialType.System_Boolean,
            LeftOperand.Type.SpecialType: SpecialType.System_Boolean,
            RightOperand.Type.SpecialType: SpecialType.System_Boolean,
        };

    public static IEnumerable<SyntaxNode> EvaluationNodes(SyntaxNode body) =>
        body.DescendantNodesAndSelf(node => node is not AnonymousFunctionExpressionSyntax
            and not LocalFunctionStatementSyntax);

    public static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parentheses)
        {
            expression = parentheses.Expression;
        }

        return expression;
    }
}
