using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodingRules;

/// <summary>The role of a symbol a fix synthesizes without knowing its domain meaning.</summary>
internal enum Placeholder
{
    /// <summary>An extracted Boolean decision, such as a guard-clause local function.</summary>
    Condition,

    /// <summary>An extracted local value.</summary>
    Variable,

    /// <summary>An extracted value-returning local function or helper.</summary>
    Calculation,
}

/// <summary>
/// Names for symbols a fix synthesizes. Generated code never guesses a name from
/// a type, parameter or counter; it uses a <c>TODO_</c> placeholder that CR1700
/// reports until a person or agent renames it after its domain meaning.
/// </summary>
internal static class PlaceholderNames
{
    public const string Prefix = "TODO_";

    public static string Of(Placeholder placeholder) => placeholder switch
    {
        Placeholder.Condition => Prefix + "CONDITION",
        Placeholder.Variable => Prefix + "VARIABLE",
        _ => Prefix + "CALCULATION",
    };

    public static bool IsPlaceholder(string name) => name.StartsWith(Prefix, System.StringComparison.Ordinal);
}

/// <summary>Collision-free names within a declaration space and its containing type's members.</summary>
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

    /// <summary>The placeholder, suffixed with <c>_1</c>, <c>_2</c>... only when it would collide.</summary>
    public string Fresh(Placeholder placeholder)
    {
        var stem = PlaceholderNames.Of(placeholder);
        var name = stem;
        var suffix = 1;
        while (!names.Add(name)) name = stem + "_" + suffix++;
        return name;
    }

    /// <summary>A domain name chosen by a fix that knows the meaning of its result.</summary>
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
    internal const string GeneratedType = "CodingRulesEvaluationType";

    public static TypeSyntax Type(ITypeSymbol type, SemanticModel model, int position) =>
        SyntaxFactory.ParseTypeName(type.ToMinimalDisplayString(model, position,
            SymbolDisplayFormat.MinimallyQualifiedFormat.WithMiscellaneousOptions(
                SymbolDisplayFormat.MinimallyQualifiedFormat.MiscellaneousOptions
                | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier)))
            .WithAdditionalAnnotations(new SyntaxAnnotation(GeneratedType));

    public static TypeSyntax Type(ITypeSymbol type) => type.SpecialType == SpecialType.System_Void
        ? SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword))
        : SyntaxFactory.ParseTypeName(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat
            .WithMiscellaneousOptions(SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
                | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier)))
            .WithAdditionalAnnotations(new SyntaxAnnotation(GeneratedType));
}
