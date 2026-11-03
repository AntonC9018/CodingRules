using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace CodingRules;

/// <summary>
/// Locals and parameters whose value cannot change while a rewritten site is
/// evaluated. A fix reads such a value in place instead of copying it into an
/// alias before work it moves ahead of the original read.
/// </summary>
internal sealed class StableValues
{
    private readonly SemanticModel model;
    private readonly ExpressionSyntax site;
    private readonly HashSet<ISymbol> writtenBySite = new(SymbolEqualityComparer.Default);
    private readonly HashSet<ISymbol> writtenByClosures = new(SymbolEqualityComparer.Default);
    private readonly bool analyzed;

    public StableValues(SemanticModel model, SyntaxNode declarationSpace, ExpressionSyntax site)
    {
        this.model = model;
        this.site = site;
        var flow = model.AnalyzeDataFlow(site);
        analyzed = flow is { Succeeded: true };
        if (analyzed) writtenBySite.UnionWith(flow!.WrittenInside);
        foreach (var closure in declarationSpace.DescendantNodes()
                     .Where(node => node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
        {
            AddClosureWrites(closure);
        }
    }

    /// <summary>Whether <paramref name="symbol"/> may change while the site is evaluated.</summary>
    public bool MayChange(ISymbol symbol) => !analyzed || writtenBySite.Contains(symbol) || writtenByClosures.Contains(symbol);

    /// <summary>An identifier of a by-value local or parameter that nothing in the site or a closure writes.</summary>
    public bool IsStable(ExpressionSyntax expression) => Parentheses.Unwrap(expression) is IdentifierNameSyntax identifier
        && ByValueVariable(identifier) is { } symbol && !MayChange(symbol);

    private ISymbol? ByValueVariable(IdentifierNameSyntax identifier) => model.GetSymbolInfo(identifier).Symbol switch
    {
        ILocalSymbol { RefKind: RefKind.None } local => local,
        IParameterSymbol { RefKind: RefKind.None } parameter => parameter,
        _ => null,
    };

    /// <summary>
    /// The local or parameter a simple assignment or prefix increment writes, when the
    /// mutation can run as its own statement and its result be read back from that
    /// variable: it is the variable's only write in the site and no closure writes it.
    /// </summary>
    public ISymbol? HoistableTarget(ExpressionSyntax mutation)
    {
        var target = mutation switch
        {
            AssignmentExpressionSyntax assignment when assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) => assignment.Left,
            PrefixUnaryExpressionSyntax unary when unary.Kind() is SyntaxKind.PreIncrementExpression
                or SyntaxKind.PreDecrementExpression => unary.Operand,
            _ => null,
        };
        if (!analyzed || target is not IdentifierNameSyntax identifier
            || ByValueVariable(identifier) is not { } symbol || writtenByClosures.Contains(symbol))
        {
            return null;
        }

        var writes = SymbolReferences.In(site, symbol, model).Count(IsWrite);
        return writes == 1 ? symbol : null;
    }

    private static bool IsWrite(IdentifierNameSyntax identifier)
    {
        SyntaxNode node = identifier;
        while (node.Parent is ParenthesizedExpressionSyntax) node = node.Parent;
        return node.Parent switch
        {
            AssignmentExpressionSyntax assignment => assignment.Left == node,
            PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax => node.Parent.Kind() is SyntaxKind.PreIncrementExpression
                or SyntaxKind.PreDecrementExpression or SyntaxKind.PostIncrementExpression or SyntaxKind.PostDecrementExpression,
            ArgumentSyntax argument => !argument.RefKindKeyword.IsKind(SyntaxKind.None),
            _ => false,
        };
    }

    private void AddClosureWrites(SyntaxNode closure)
    {
        var body = closure switch
        {
            AnonymousFunctionExpressionSyntax { Body: { } lambdaBody } => lambdaBody,
            LocalFunctionStatementSyntax { Body: { } localBody } => localBody,
            LocalFunctionStatementSyntax { ExpressionBody: { } expressionBody } => expressionBody.Expression,
            _ => null,
        };
        var flow = body is null ? null : model.AnalyzeDataFlow(body);
        if (flow is { Succeeded: true })
        {
            writtenByClosures.UnionWith(flow.WrittenInside);
            foreach (var argument in body!.DescendantNodes().OfType<ArgumentSyntax>().Where(IsReferenceArgument))
            {
                foreach (var identifier in argument.Expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
                {
                    if (model.GetSymbolInfo(identifier).Symbol is { } symbol) writtenByClosures.Add(symbol);
                }
            }
            return;
        }

        // A failed body analysis cannot prove that a captured value stays stable.
        foreach (var identifier in closure.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (model.GetSymbolInfo(identifier).Symbol is { } symbol) writtenByClosures.Add(symbol);
        }
    }

    private bool IsReferenceArgument(ArgumentSyntax argument)
    {
        var refKind = (model.GetOperation(argument) as IArgumentOperation)?.Parameter?.RefKind;
        return refKind is not null and not RefKind.None || !argument.RefOrOutKeyword.IsKind(SyntaxKind.None);
    }
}
