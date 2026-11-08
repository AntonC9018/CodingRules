using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodingRules;

internal static class PipelineFacts
{
    internal static IEnumerable<SyntaxNode> StageNodes(SyntaxNode body) => body.DescendantNodesAndSelf(node =>
        node == body || node is not AnonymousFunctionExpressionSyntax and not LocalFunctionStatementSyntax);

    internal static bool HasComposition(ExpressionSyntax body, SemanticModel model)
    {
        var components = body switch
        {
            TupleExpressionSyntax tuple => tuple.Arguments.Select(argument => argument.Expression),
            BaseObjectCreationExpressionSyntax { Initializer: not null } creation => (creation.ArgumentList?.Arguments.Select(argument => argument.Expression) ?? System.Array.Empty<ExpressionSyntax>())
                .Concat(creation.Initializer.Expressions.OfType<AssignmentExpressionSyntax>().Select(assignment => assignment.Right)),
            _ => new[] { body },
        };
        return components.Any(component => OperationFacts.EvaluationNodes(component).OfType<ExpressionSyntax>()
            .Any(expression => OperationFacts.Create(expression, model) is { Nested: true } or { Combined: true }));
    }

}
