using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodingRules;

/// <summary>
/// Code inside an expression tree is data for a provider such as a query translator,
/// not a sequence of steps; it must stay inline, so readability rules neither report
/// nor rewrite it.
/// </summary>
internal static class ExpressionTrees
{
    public static bool Contains(SyntaxNode node, SemanticModel model, CancellationToken token = default)
    {
        var expressionType = model.Compilation.GetTypeByMetadataName("System.Linq.Expressions.Expression`1");
        if (expressionType is null)
        {
            return false;
        }

        bool IsTree(ITypeSymbol? type) =>
            SymbolEqualityComparer.Default.Equals(type?.OriginalDefinition, expressionType);

        bool TakesTree(ISymbol? symbol) =>
            symbol is IMethodSymbol method && method.Parameters.Any(parameter => IsTree(parameter.Type));

        foreach (var ancestor in node.AncestorsAndSelf())
        {
            var tree = ancestor switch
            {
                AnonymousFunctionExpressionSyntax function => IsTree(model.GetTypeInfo(function, token).ConvertedType),
                // A query clause lowers to a lambda passed to the clause's method.
                QueryClauseSyntax clause => TakesTree(model.GetQueryClauseInfo(clause, token).OperationInfo.Symbol),
                SelectOrGroupClauseSyntax or OrderingSyntax => TakesTree(model.GetSymbolInfo(ancestor, token).Symbol),
                _ => false,
            };
            if (tree)
            {
                return true;
            }
        }

        return false;
    }
}
