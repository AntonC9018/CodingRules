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
        kind = default;
        if (finalReturn.Expression is null
            || finalReturn.Expression is RefExpressionSyntax
            || finalReturn.ContainsDirectives)
        {
            return false;
        }

        if (finalReturn.Expression.DescendantTrivia().Any(IsComment))
        {
            return false;
        }

        var expression = Unwrap(finalReturn.Expression);
        if (expression is ConditionalExpressionSyntax conditional)
        {
            if (ContainsThrowArm(conditional))
            {
                return false;
            }

            if (IsFallback(conditional.WhenTrue) && !IsFallback(conditional.WhenFalse))
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
            var leftType = semanticModel.GetTypeInfo(coalesce.Left, cancellationToken).Type;
            if ((leftType?.IsReferenceType != true && !IsNullableValueType(leftType))
                || coalesce.Right is ThrowExpressionSyntax)
            {
                return false;
            }

            kind = ReturnDecisionKind.Coalesce;
            return true;
        }

        var type = semanticModel.GetTypeInfo(expression, cancellationToken).Type;
        if (type?.SpecialType == SpecialType.System_Boolean && IsBooleanDecision(expression))
        {
            kind = ReturnDecisionKind.Boolean;
            return true;
        }

        if (IsCall(expression) && IsNullable(type))
        {
            kind = ReturnDecisionKind.NullableCall;
            return true;
        }

        return false;
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
        return unwrapped.IsKind(SyntaxKind.NullLiteralExpression)
            || unwrapped.IsKind(SyntaxKind.DefaultLiteralExpression)
            || unwrapped is DefaultExpressionSyntax;
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

        if (statement is BlockSyntax block && block.Statements.Count == 1)
        {
            return block.Statements[0] is ReturnStatementSyntax;
        }

        return false;
    }

    private static bool ContainsThrowArm(ConditionalExpressionSyntax conditional)
    {
        return Unwrap(conditional.WhenTrue) is ThrowExpressionSyntax
            || Unwrap(conditional.WhenFalse) is ThrowExpressionSyntax;
    }

    private static bool IsBooleanDecision(ExpressionSyntax expression)
    {
        return expression is InvocationExpressionSyntax
            or BinaryExpressionSyntax
            or PrefixUnaryExpressionSyntax
            or IsPatternExpressionSyntax
            or AwaitExpressionSyntax;
    }

    private static bool IsCall(ExpressionSyntax expression)
    {
        if (expression is InvocationExpressionSyntax)
        {
            return true;
        }

        if (expression is AwaitExpressionSyntax awaitExpression)
        {
            return Unwrap(awaitExpression.Expression) is InvocationExpressionSyntax;
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

        return IsNullableValueType(type);
    }

    private static bool IsComment(SyntaxTrivia trivia)
    {
        return trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
            || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
            || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
            || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia);
    }
}
