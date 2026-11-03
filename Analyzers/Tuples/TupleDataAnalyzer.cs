using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodingRules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TupleDataAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.NamedTupleData,
        "Represent tuple data with a named record struct",
        "Represent tuple data with a named record struct",
        "Readability",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Name method results, method inputs and stored collection elements with a record struct. API and caller migration requires manual review.",
        helpLinkUri: "https://github.com/AntonC9018/CodingRules/blob/main/docs/tuple-data.md");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            var collections = CollectionTypes(start.Compilation);
            start.RegisterSyntaxNodeAction(node => Analyze(node, collections),
                SyntaxKind.MethodDeclaration, SyntaxKind.Parameter, SyntaxKind.PropertyDeclaration,
                SyntaxKind.VariableDeclarator);
        });
    }

    private static ImmutableArray<INamedTypeSymbol> CollectionTypes(Compilation compilation)
    {
        var types = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
        foreach (var name in new[]
        {
            "System.Collections.Generic.List`1",
            "System.Collections.Generic.IEnumerable`1",
            "System.ReadOnlySpan`1",
        })
        {
            var type = compilation.GetTypeByMetadataName(name);
            if (type is not null) types.Add(type);
        }
        return types.ToImmutable();
    }

    private static void Analyze(SyntaxNodeAnalysisContext context, ImmutableArray<INamedTypeSymbol> collections)
    {
        if (context.Node.ContainsDiagnostics) return;
        var model = context.SemanticModel;
        var cancellation = context.CancellationToken;
        ITypeSymbol? type;
        Location location;
        var allowDirectTuple = false;
        switch (context.Node)
        {
            case MethodDeclarationSyntax method:
                type = model.GetDeclaredSymbol(method, cancellation)?.ReturnType;
                location = method.ReturnType.GetLocation();
                allowDirectTuple = true;
                break;
            case ParameterSyntax parameter when parameter.Type is not null
                && parameter.Parent?.Parent is MethodDeclarationSyntax or LocalFunctionStatementSyntax:
                // Local-function results are exempt; their inputs are a separate data boundary.
                type = model.GetDeclaredSymbol(parameter, cancellation)?.Type;
                location = parameter.Type.GetLocation();
                allowDirectTuple = true;
                break;
            case PropertyDeclarationSyntax property:
                type = model.GetDeclaredSymbol(property, cancellation)?.Type;
                location = property.Type.GetLocation();
                break;
            case VariableDeclaratorSyntax variable:
                var symbol = model.GetDeclaredSymbol(variable, cancellation);
                type = symbol switch
                {
                    ILocalSymbol local => local.Type,
                    IFieldSymbol field => field.Type,
                    _ => null,
                };
                location = variable.Identifier.GetLocation();
                break;
            default:
                return;
        }

        if (allowDirectTuple && IsTuple(type))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, location));
            return;
        }
        if (IsTupleCollection(type, collections, includeEnumerable: allowDirectTuple))
            context.ReportDiagnostic(Diagnostic.Create(Rule, location));
    }

    private static bool IsTuple(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol { IsTupleType: true } tuple) return false;
        return tuple.TupleElements.All(element => element.Type.TypeKind != TypeKind.Error);
    }

    private static bool IsTupleCollection(ITypeSymbol? type, ImmutableArray<INamedTypeSymbol> collections, bool includeEnumerable)
    {
        if (type is IArrayTypeSymbol array) return IsTuple(array.ElementType);
        if (type is not INamedTypeSymbol { TypeArguments.Length: 1 } named) return false;
        if (!IsTuple(named.TypeArguments[0])) return false;
        foreach (var collection in collections)
        {
            // Iterator results may be kept locally without treating their lazy sequence as a materialized table.
            if (!includeEnumerable && collection.Name == "IEnumerable") continue;
            if (SymbolEqualityComparer.Default.Equals(collection, named.OriginalDefinition)) return true;
        }
        return false;
    }
}
