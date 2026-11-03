using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodingRules;

internal static class Parentheses
{
    /// <summary>The expression inside any number of enclosing parentheses.</summary>
    public static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parentheses)
        {
            expression = parentheses.Expression;
        }

        return expression;
    }

    /// <summary>
    /// <paramref name="parentheses"/> around a lowered <paramref name="inner"/>
    /// expression, or the bare name when a fix replaced the parenthesized work with one.
    /// </summary>
    public static ExpressionSyntax Rewrap(ParenthesizedExpressionSyntax parentheses, ExpressionSyntax inner) =>
        inner is IdentifierNameSyntax or InvocationExpressionSyntax { Expression: IdentifierNameSyntax }
        && !inner.IsEquivalentTo(parentheses.Expression)
            ? inner
            : parentheses.WithExpression(inner).WithoutTrivia();
}
