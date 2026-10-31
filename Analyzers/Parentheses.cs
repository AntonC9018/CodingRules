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
}
