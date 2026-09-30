using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodingRules;

/// <summary>Symbol identity and receiver paths; spelling does not identify a value.</summary>
internal sealed class ConditionSubject
{
    private readonly ISymbol? symbol;
    private readonly ConditionSubject? receiver;
    private readonly List<ExpressionSyntax> indices;
    private readonly SemanticModel model;

    private ConditionSubject(ISymbol? symbol, ConditionSubject? receiver, SemanticModel model, IEnumerable<ExpressionSyntax>? indices = null)
    {
        this.symbol = symbol;
        this.receiver = receiver;
        this.model = model;
        this.indices = indices?.ToList() ?? new List<ExpressionSyntax>();
    }

    public static ConditionSubject? Create(ExpressionSyntax expression, SemanticModel model)
    {
        expression = ConditionFacts.Unwrap(expression);
        if (expression is ThisExpressionSyntax or BaseExpressionSyntax)
        {
            var type = model.GetEnclosingSymbol(expression.SpanStart)?.ContainingType;
            return new ConditionSubject(type, null, model);
        }

        if (expression is ElementAccessExpressionSyntax element)
        {
            var receiver = Create(element.Expression, model);
            if (receiver is null) return null;
            var arguments = element.ArgumentList.Arguments.Select(argument => argument.Expression).ToList();
            if (arguments.Any(index => index.DescendantNodesAndSelf().Any(node => node is InvocationExpressionSyntax
                or AssignmentExpressionSyntax or PostfixUnaryExpressionSyntax))) return null;
            return new ConditionSubject(null, receiver, model, arguments);
        }

        var symbol = model.GetSymbolInfo(expression).Symbol;
        if (symbol is not ILocalSymbol and not IParameterSymbol and not IFieldSymbol and not IPropertySymbol) return null;
        if (symbol.IsStatic) return new ConditionSubject(symbol, null, model);
        if (expression is MemberAccessExpressionSyntax member)
        {
            var receiver = Create(member.Expression, model);
            if (receiver is null) return null;
            return new ConditionSubject(symbol, receiver, model);
        }

        if (symbol is IFieldSymbol or IPropertySymbol)
        {
            var type = model.GetEnclosingSymbol(expression.SpanStart)?.ContainingType;
            var receiver = new ConditionSubject(type, null, model);
            return new ConditionSubject(symbol, receiver, model);
        }

        return new ConditionSubject(symbol, null, model);
    }

    public bool Matches(ConditionSubject? other)
    {
        if (other is null) return false;
        if (!SymbolEqualityComparer.Default.Equals(symbol, other.symbol)) return false;
        if (receiver is null && other.receiver is not null) return false;
        if (receiver is not null && !receiver.Matches(other.receiver)) return false;
        if (indices.Count != other.indices.Count) return false;
        for (var index = 0; index < indices.Count; index++)
        {
            if (!SameIndex(indices[index], other.indices[index], other.model)) return false;
        }

        return true;
    }

    private bool SameIndex(ExpressionSyntax first, ExpressionSyntax second, SemanticModel secondModel)
    {
        var firstConstant = model.GetConstantValue(first);
        var secondConstant = secondModel.GetConstantValue(second);
        if (firstConstant.HasValue && secondConstant.HasValue)
        {
            if (Equals(firstConstant.Value, secondConstant.Value)) return true;
            return false;
        }

        if (!SyntaxFactory.AreEquivalent(first, second)) return false;
        var firstSymbols = first.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Select(node => model.GetSymbolInfo(node).Symbol).ToList();
        var secondSymbols = second.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Select(node => secondModel.GetSymbolInfo(node).Symbol).ToList();
        if (firstSymbols.Count != secondSymbols.Count) return false;
        for (var index = 0; index < firstSymbols.Count; index++)
        {
            if (!SymbolEqualityComparer.Default.Equals(firstSymbols[index], secondSymbols[index])) return false;
        }

        return true;
    }
}
