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

    internal static bool NeedsHelper(BlockSyntax body, SemanticModel model)
    {
        var nodes = StageNodes(body).ToArray();
        if (nodes.Where(node => node is IfStatementSyntax or SwitchStatementSyntax).Any(node => node.Ancestors()
                .TakeWhile(ancestor => ancestor != body).Any(ancestor => ancestor is IfStatementSyntax or SwitchStatementSyntax
                    && !(node.Parent is ElseClauseSyntax && node.Parent.Parent == ancestor)))) return true;
        var declarations = nodes.OfType<LocalDeclarationStatementSyntax>().Where(local => local.Modifiers.Count == 0
            && local.UsingKeyword.RawKind == 0 && local.AwaitKeyword.RawKind == 0 && local.Declaration.Variables.Count == 1
            && local.Declaration.Variables[0].Initializer is not null
            && OperationFacts.Unwrap(local.Declaration.Variables[0].Initializer!.Value) is not IdentifierNameSyntax
            && model.GetDeclaredSymbol(local.Declaration.Variables[0]) is ILocalSymbol { Type.TypeKind: not TypeKind.Delegate })
            .Select(local => local.Declaration.Variables[0]).ToArray();
        var feeding = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var completion in nodes.OfType<ReturnStatementSyntax>())
            foreach (var name in StageNodes(completion).OfType<IdentifierNameSyntax>())
                if (model.GetSymbolInfo(name).Symbol is ILocalSymbol symbol) feeding.Add(symbol);
        for (var index = declarations.Length - 1; index >= 0; index--)
        {
            var declaration = declarations[index];
            if (model.GetDeclaredSymbol(declaration) is not ILocalSymbol symbol || !feeding.Contains(symbol)) continue;
            foreach (var name in StageNodes(declaration.Initializer!.Value).OfType<IdentifierNameSyntax>())
                if (model.GetSymbolInfo(name).Symbol is ILocalSymbol dependency) feeding.Add(dependency);
        }
        return declarations.Count(declaration => model.GetDeclaredSymbol(declaration) is ILocalSymbol symbol && feeding.Contains(symbol)) >= 2;
    }
}
