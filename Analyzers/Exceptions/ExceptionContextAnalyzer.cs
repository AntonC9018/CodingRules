using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodingRules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ExceptionContextAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Rule = new(DiagnosticIds.ExceptionContext,
        "Expose exception context", "Expose interpolated exception values through typed read-only properties",
        "Readability", DiagnosticSeverity.Warning, true,
        description: "Keep failure context accessible without parsing Message. Generating a derived exception is an API change; review the offered action before applying it.");
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.InterpolatedStringExpression);
    }
    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var expression = (InterpolatedStringExpressionSyntax)context.Node;
        if (expression.Contents.OfType<InterpolationSyntax>().Any() && IsMessage(expression, context.SemanticModel))
            context.ReportDiagnostic(Diagnostic.Create(Rule, expression.GetLocation()));
    }
    internal static bool IsMessage(InterpolatedStringExpressionSyntax expression, SemanticModel model)
    {
        if (expression.Parent is not ArgumentSyntax { Parent.Parent: BaseObjectCreationExpressionSyntax creation }
            || model.GetTypeInfo(expression).ConvertedType?.SpecialType != SpecialType.System_String) return false;
        var exception = model.Compilation.GetTypeByMetadataName("System.Exception");
        for (var type = model.GetTypeInfo(creation).Type as INamedTypeSymbol; type is not null; type = type.BaseType)
            if (SymbolEqualityComparer.Default.Equals(type, exception)) return true;
        return false;
    }
}
