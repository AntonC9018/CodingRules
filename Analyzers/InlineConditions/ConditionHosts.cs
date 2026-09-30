using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodingRules;

internal static class ConditionHosts
{
    public static ExpressionSyntax? GetExpression(SyntaxNode node)
    {
        var expression = node switch
        {
            IfStatementSyntax statement => statement.Condition,
            EqualsValueClauseSyntax initializer => initializer.Value,
            ReturnStatementSyntax statement => statement.Expression,
            ArrowExpressionClauseSyntax arrow => arrow.Expression,
            LambdaExpressionSyntax { Body: ExpressionSyntax body } => body,
            AssignmentExpressionSyntax assignment => assignment.Right,
            ArgumentSyntax argument => argument.Expression,
            ConditionalExpressionSyntax conditional => conditional.Condition,
            WhileStatementSyntax statement => statement.Condition,
            ForStatementSyntax statement => statement.Condition,
            DoStatementSyntax statement => statement.Condition,
            CatchFilterClauseSyntax filter => filter.FilterExpression,
            WhenClauseSyntax guard => guard.Condition,
            _ => null,
        };

        // An argument/initializer/return containing an assignment or lambda is
        // owned by that inner host. Distinct arguments inside leaves stay independent.
        if (expression is AssignmentExpressionSyntax or LambdaExpressionSyntax)
        {
            return null;
        }

        return expression;
    }

    public static bool IsInsideExpressionTree(ExpressionSyntax expression, SemanticModel model)
    {
        foreach (var lambda in expression.AncestorsAndSelf().OfType<LambdaExpressionSyntax>())
        {
            var type = model.GetTypeInfo(lambda).ConvertedType as INamedTypeSymbol;
            if (type?.OriginalDefinition.ToDisplayString() == "System.Linq.Expressions.Expression<TDelegate>")
            {
                return true;
            }
        }

        return false;
    }

    public static StatementSyntax? FindStatement(ExpressionSyntax expression)
    {
        foreach (var ancestor in expression.Ancestors())
        {
            if (ancestor is StatementSyntax statement && statement is not LocalFunctionStatementSyntax)
            {
                return statement;
            }

            if (ancestor is LambdaExpressionSyntax or ArrowExpressionClauseSyntax or MemberDeclarationSyntax)
            {
                return null;
            }
        }

        return null;
    }
}
