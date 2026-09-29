using System.Collections.Generic;
using System.Threading;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodingRules;

internal enum ReturnDecisionKind
{
    Conditional,
    Coalesce,
    Boolean,
    NullableCall,
}

internal static class ReturnDecisionAnalysis
{
    public static bool IsFunctionBody(BlockSyntax block)
    {
        return block.Parent is MethodDeclarationSyntax
            or AccessorDeclarationSyntax
            or LocalFunctionStatementSyntax
            or ParenthesizedLambdaExpressionSyntax
            or SimpleLambdaExpressionSyntax
            or AnonymousMethodExpressionSyntax;
    }

    public static bool TryGetFinalReturn(BlockSyntax block, out ReturnStatementSyntax finalReturn)
    {
        if (block.Statements.Count == 0)
        {
            finalReturn = null!;
            return false;
        }

        if (block.Statements[block.Statements.Count - 1] is ReturnStatementSyntax candidate)
        {
            finalReturn = candidate;
            return true;
        }

        finalReturn = null!;
        return false;
    }

    public static bool HasTopLevelGuard(BlockSyntax block)
    {
        for (var index = 0; index < block.Statements.Count - 1; index++)
        {
            if (block.Statements[index] is not IfStatementSyntax guard)
            {
                continue;
            }

            if (guard.Else is not null)
            {
                continue;
            }

            if (GuardReturns(guard.Statement))
            {
                return true;
            }
        }

        return false;
    }

    public static bool TryGetKind(
        ReturnStatementSyntax finalReturn,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ReturnDecisionKind kind)
    {
        return TryClassify(
            finalReturn,
            semanticModel,
            cancellationToken,
            allowThrowArms: false,
            out kind);
    }

    /// <summary>
    /// Classifies a nested return for the nested decision rules (CR0003-CR0006).
    /// Unlike <see cref="TryGetKind"/> it accepts a ternary with a throw arm,
    /// which the nested fix rewrites to a guard statement.
    /// </summary>
    public static bool TryGetNestedKind(
        ReturnStatementSyntax returnStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ReturnDecisionKind kind)
    {
        return TryClassify(
            returnStatement,
            semanticModel,
            cancellationToken,
            allowThrowArms: true,
            out kind);
    }

    /// <summary>
    /// Collects the return statements of a function body that sit beyond its
    /// top level, without crossing nested function bodies (lambdas, local
    /// functions, and anonymous methods are boundaries in both directions).
    /// Ticket #3 (guard analysis) and #8 (sentinel returns) can reuse this
    /// walk over the same return sets.
    /// </summary>
    public static List<ReturnStatementSyntax> CollectNestedReturns(BlockSyntax body)
    {
        var nestedReturns = new List<ReturnStatementSyntax>();
        CollectNestedReturns(body, body, nestedReturns);
        return nestedReturns;
    }

    private static void CollectNestedReturns(
        BlockSyntax body,
        SyntaxNode node,
        List<ReturnStatementSyntax> results)
    {
        foreach (var child in node.ChildNodes())
        {
            if (child is ReturnStatementSyntax returnStatement)
            {
                if (!ReferenceEquals(returnStatement.Parent, body))
                {
                    results.Add(returnStatement);
                }

                continue;
            }

            if (child is StatementSyntax statement)
            {
                if (statement is LocalFunctionStatementSyntax)
                {
                    continue;
                }

                CollectNestedReturns(body, statement, results);
                continue;
            }

            if (child is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax)
            {
                continue;
            }

            CollectNestedReturns(body, child, results);
        }
    }

    private static bool TryClassify(
        ReturnStatementSyntax returnStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        bool allowThrowArms,
        out ReturnDecisionKind kind)
    {
        kind = default;
        if (returnStatement.Expression is null)
        {
            return false;
        }

        if (returnStatement.Expression is RefExpressionSyntax)
        {
            return false;
        }

        if (returnStatement.ContainsDirectives)
        {
            return false;
        }

        if (returnStatement.Expression.DescendantTrivia().Any(IsComment))
        {
            return false;
        }

        var expression = Unwrap(returnStatement.Expression);
        if (expression is ConditionalExpressionSyntax conditional)
        {
            if (!HasIdentityReturnConversion(expression, semanticModel, cancellationToken))
            {
                return false;
            }

            var trueArmThrows = Unwrap(conditional.WhenTrue) is ThrowExpressionSyntax;
            var falseArmThrows = Unwrap(conditional.WhenFalse) is ThrowExpressionSyntax;
            if (trueArmThrows && falseArmThrows)
            {
                return false;
            }

            if (trueArmThrows || falseArmThrows)
            {
                if (!allowThrowArms)
                {
                    return false;
                }
            }

            // The guard rewrite negates the condition when the fallback or the
            // throw sits in the false arm, so the condition must be a plain bool.
            if (NeedsFallbackInversion(conditional) || falseArmThrows)
            {
                var conditionType = semanticModel.GetTypeInfo(conditional.Condition, cancellationToken).Type;
                if (conditionType?.SpecialType != SpecialType.System_Boolean)
                {
                    return false;
                }
            }

            kind = ReturnDecisionKind.Conditional;
            return true;
        }

        if (expression is BinaryExpressionSyntax coalesce && coalesce.IsKind(SyntaxKind.CoalesceExpression))
        {
            if (!HasIdentityReturnConversion(expression, semanticModel, cancellationToken))
            {
                return false;
            }

            var leftType = semanticModel.GetTypeInfo(coalesce.Left, cancellationToken).Type;
            if (leftType?.IsReferenceType != true)
            {
                if (!IsNullableValueType(leftType))
                {
                    return false;
                }
            }

            if (coalesce.Right is ThrowExpressionSyntax)
            {
                return false;
            }

            kind = ReturnDecisionKind.Coalesce;
            return true;
        }

        var type = semanticModel.GetTypeInfo(expression, cancellationToken).Type;
        if (type?.SpecialType == SpecialType.System_Boolean)
        {
            if (IsBooleanDecision(expression))
            {
                kind = ReturnDecisionKind.Boolean;
                return true;
            }
        }

        if (!IsCall(expression))
        {
            return false;
        }

        if (!IsNullable(type))
        {
            return false;
        }

        if (!HasIdentityReturnConversion(expression, semanticModel, cancellationToken))
        {
            return false;
        }

        kind = ReturnDecisionKind.NullableCall;
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

    public static bool IsFallback(ExpressionSyntax expression)
    {
        var unwrapped = Unwrap(expression);
        switch (unwrapped.Kind())
        {
            case SyntaxKind.NullLiteralExpression:
            case SyntaxKind.DefaultLiteralExpression:
            case SyntaxKind.DefaultExpression:
                return true;
            default:
                return false;
        }
    }

    public static bool IsNullableValueType(ITypeSymbol? type)
    {
        return type is INamedTypeSymbol named
            && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
    }

    private static bool GuardReturns(StatementSyntax statement)
    {
        if (statement is ReturnStatementSyntax)
        {
            return true;
        }

        if (statement is BlockSyntax block && block.Statements.Count > 0)
        {
            for (var index = 0; index < block.Statements.Count - 1; index++)
            {
                if (block.Statements[index] is not ExpressionStatementSyntax
                    and not LocalDeclarationStatementSyntax
                    and not EmptyStatementSyntax)
                {
                    return false;
                }
            }

            if (block.Statements[block.Statements.Count - 1] is ReturnStatementSyntax)
            {
                return true;
            }
        }

        return false;
    }

    public static bool NeedsFallbackInversion(ConditionalExpressionSyntax conditional)
    {
        if (!IsFallback(conditional.WhenTrue))
        {
            return false;
        }

        if (IsFallback(conditional.WhenFalse))
        {
            return false;
        }

        return true;
    }

    private static bool IsBooleanDecision(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case InvocationExpressionSyntax:
            case BinaryExpressionSyntax:
            case PrefixUnaryExpressionSyntax:
            case IsPatternExpressionSyntax:
            case AwaitExpressionSyntax:
                return true;
            default:
                return false;
        }
    }

    private static bool IsCall(ExpressionSyntax expression)
    {
        if (expression is InvocationExpressionSyntax)
        {
            return true;
        }

        if (expression is AwaitExpressionSyntax awaitExpression)
        {
            if (Unwrap(awaitExpression.Expression) is InvocationExpressionSyntax)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsNullable(ITypeSymbol? type)
    {
        if (type is null)
        {
            return false;
        }

        if (type.IsReferenceType && type.NullableAnnotation == NullableAnnotation.Annotated)
        {
            return true;
        }

        if (IsNullableValueType(type))
        {
            return true;
        }

        return false;
    }

    private static bool HasIdentityReturnConversion(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
        if (typeInfo.Type is null)
        {
            return false;
        }

        if (typeInfo.ConvertedType is null)
        {
            return false;
        }

        if (SymbolEqualityComparer.Default.Equals(typeInfo.Type, typeInfo.ConvertedType))
        {
            return true;
        }

        return false;
    }

    private static bool IsComment(SyntaxTrivia trivia)
    {
        switch (trivia.Kind())
        {
            case SyntaxKind.SingleLineCommentTrivia:
            case SyntaxKind.MultiLineCommentTrivia:
            case SyntaxKind.SingleLineDocumentationCommentTrivia:
            case SyntaxKind.MultiLineDocumentationCommentTrivia:
                return true;
            default:
                return false;
        }
    }
}
