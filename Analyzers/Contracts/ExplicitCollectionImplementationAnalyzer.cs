using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodingRules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ExplicitCollectionImplementationAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.ExplicitCollectionImplementation,
        "Expose generic collection operations through the generic API",
        "Implement non-generic collection member '{0}' explicitly",
        "Readability", DiagnosticSeverity.Warning, isEnabledByDefault: true,
        description: "Non-generic collection members on a generic collection belong on the explicit interface API. Members shared with a generic interface remain public. Caller migration requires manual review.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.MethodDeclaration, SyntaxKind.PropertyDeclaration, SyntaxKind.IndexerDeclaration);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var symbol = context.SemanticModel.GetDeclaredSymbol(context.Node, context.CancellationToken);
        if (symbol is not (IMethodSymbol or IPropertySymbol) || symbol.DeclaredAccessibility != Accessibility.Public
            || symbol.ContainingType is not { TypeKind: TypeKind.Class or TypeKind.Struct } type) return;
        var enumerable = context.Compilation.GetTypeByMetadataName("System.Collections.Generic.IEnumerable`1");
        if (enumerable is null || !type.AllInterfaces.Any(contract => SymbolEqualityComparer.Default.Equals(contract.OriginalDefinition, enumerable))) return;
        var contracts = new[] { "System.Collections.IEnumerable", "System.Collections.ICollection", "System.Collections.IList" }
            .Select(context.Compilation.GetTypeByMetadataName).Where(contract => contract is not null).ToArray();
        var implementsLegacy = contracts.Any(contract => contract!.GetMembers().Any(member =>
            SymbolEqualityComparer.Default.Equals(type.FindImplementationForInterfaceMember(member), symbol)));
        if (!implementsLegacy) return;
        // Count/Clear/etc. may serve both interfaces. Making those explicit would
        // remove the intended generic API, and they have no object-only overload.
        if (type.AllInterfaces.Where(contract => contract.IsGenericType).SelectMany(contract => contract.GetMembers())
            .Any(member => SymbolEqualityComparer.Default.Equals(type.FindImplementationForInterfaceMember(member), symbol))) return;
        var location = context.Node switch
        {
            MethodDeclarationSyntax method => method.Identifier.GetLocation(),
            PropertyDeclarationSyntax property => property.Identifier.GetLocation(),
            IndexerDeclarationSyntax indexer => indexer.ThisKeyword.GetLocation(),
            _ => context.Node.GetLocation(),
        };
        context.ReportDiagnostic(Diagnostic.Create(Rule, location, symbol.Name));
    }
}
