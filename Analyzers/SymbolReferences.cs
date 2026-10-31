using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodingRules;

internal static class SymbolReferences
{
    /// <summary>
    /// Identifiers in <paramref name="scope"/> that bind to <paramref name="symbol"/>.
    /// A reference must spell the symbol's name, so other names are skipped before
    /// binding; scanning a whole member otherwise binds every identifier in it.
    /// </summary>
    public static IEnumerable<IdentifierNameSyntax> In(SyntaxNode scope, ISymbol symbol, SemanticModel model) =>
        scope.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Where(identifier =>
            identifier.Identifier.ValueText == symbol.Name
            && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(identifier).Symbol, symbol));
}
