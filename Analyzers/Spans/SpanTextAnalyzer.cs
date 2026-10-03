using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodingRules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SpanTextAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Rule = new(DiagnosticIds.TransientTextInspection, "Use a span for transient text inspection",
        "Use a span for transient text inspection", "Readability", DiagnosticSeverity.Warning, true);
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var catalog = SpanCatalog.For(start.Compilation);
            start.RegisterSyntaxNodeAction(node =>
            {
                if (SpanPlan.Create((InvocationExpressionSyntax)node.Node, node.SemanticModel, catalog, node.CancellationToken, node.Options) is not null)
                    node.ReportDiagnostic(Diagnostic.Create(Rule, node.Node.GetLocation()));
            }, SyntaxKind.InvocationExpression);
        });
    }
}
