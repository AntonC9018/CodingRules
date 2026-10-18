// Adapted from Meziantou.Analyzer's ClassMustBeSealedAnalyzer (MA0053),
// commit decb1e62ae7205db62d8b6225220e108d05f52d4. Copyright (c) Gérald Barré.
// MIT license and source/change provenance: third-party/meziantou/.
// Changes: CR1400 manual findings, public/exception/virtual/inherited coverage,
// forwarding-attribute exception, semantic framework identity and generated-code handling.
using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodingRules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SealConcreteClassAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.SealConcreteClass,
        "Review concrete classes for sealing",
        "Review concrete class '{0}' for sealing; resolve inheritance and external constraints manually",
        "Readability", DiagnosticSeverity.Warning, isEnabledByDefault: true,
        description: "Seal non-abstract classes by default. Existing inheritance and test seams require architectural review, not an unsafe modifier insertion. A non-generic attribute base with a type-forwarding generic variant is allowed. No automatic sealing action is offered.",
        helpLinkUri: "https://github.com/AntonC9018/CodingRules/blob/main/docs/sealed-classes.md");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            var analyzerContext = new AnalyzerContext(start.Compilation);
            start.RegisterSyntaxNodeAction(analyzerContext.AnalyzeTypeDeclaration,
                SyntaxKind.ClassDeclaration, SyntaxKind.RecordDeclaration);
        });
    }

    private sealed class AnalyzerContext(Compilation compilation)
    {
        private readonly ConcurrentDictionary<INamedTypeSymbol, byte> reported = new(SymbolEqualityComparer.Default);
        private readonly INamedTypeSymbol? comImportSymbol = compilation.GetTypeByMetadataName("System.Runtime.InteropServices.ComImportAttribute");
        private readonly INamedTypeSymbol? benchmarkSymbol = compilation.GetTypeByMetadataName("BenchmarkDotNet.Attributes.BenchmarkAttribute");

        public void AnalyzeTypeDeclaration(SyntaxNodeAnalysisContext context)
        {
            var declaration = (TypeDeclarationSyntax)context.Node;
            if (declaration.ContainsDiagnostics) return;
            if (context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is not INamedTypeSymbol symbol) return;
            if (symbol.TypeKind != TypeKind.Class) return;
            if (symbol.IsSealed || symbol.IsAbstract || symbol.IsStatic || symbol.IsImplicitlyDeclared) return;
            if (HasExternalAttribute(symbol, comImportSymbol)) return;
            if (HasBenchmarkContract(symbol)) return;
            if (ForwardingAttribute.IsBase(symbol, compilation, context.CancellationToken)) return;

            if (symbol.BaseType?.TypeKind == TypeKind.Error) return;
            // Roslyn invokes this action only for hand-written declarations.
            // A symbol's first declaration can be generated, so selecting it in
            // a symbol action would lose a mixed partial type's finding.
            if (!reported.TryAdd(symbol, 0)) return;
            context.ReportDiagnostic(Diagnostic.Create(Rule, declaration.Identifier.GetLocation(), symbol.Name));
        }

        private bool HasBenchmarkContract(INamedTypeSymbol symbol)
        {
            for (var current = symbol; current is not null; current = current.BaseType)
            {
                if (current.GetMembers().OfType<IMethodSymbol>().Any(method => HasExternalAttribute(method, benchmarkSymbol)))
                    return true;
            }
            return false;
        }

        private static bool HasExternalAttribute(ISymbol symbol, INamedTypeSymbol? attribute)
        {
            if (attribute is null || attribute.Locations.Any(location => location.IsInSource)) return false;
            return symbol.GetAttributes().Any(value => SymbolEqualityComparer.Default.Equals(value.AttributeClass, attribute));
        }
    }
}
