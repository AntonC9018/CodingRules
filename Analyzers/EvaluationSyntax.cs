using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodingRules;

internal sealed class EvaluationNames
{
    private readonly HashSet<string> names;

    public EvaluationNames(SyntaxNode declarationSpace, SemanticModel model)
    {
        names = new HashSet<string>(declarationSpace.DescendantTokens().Where(token => token.IsKind(SyntaxKind.IdentifierToken))
            .Select(token => token.ValueText));
        var declaration = declarationSpace.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (declaration is not null && model.GetDeclaredSymbol(declaration) is INamedTypeSymbol type)
        {
            foreach (var member in type.GetMembers()) names.Add(member.Name);
        }
    }

    public string Fresh(string stem)
    {
        var name = stem;
        var suffix = 2;
        while (!names.Add(name)) name = stem + suffix++;
        return name;
    }
}

internal static class EvaluationSyntax
{
    public static TypeSyntax Type(ITypeSymbol type) => type.SpecialType == SpecialType.System_Void
        ? SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword))
        : SyntaxFactory.ParseTypeName(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat
            .WithMiscellaneousOptions(SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
                | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier)));
}
