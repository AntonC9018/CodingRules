using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodingRules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CompoundPredicateAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.CompoundNestedPredicate,
        "Expand compound or deeply nested predicates",
        "Use a block lambda with explicit outcomes for this compound or deeply nested predicate",
        "Readability", DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var catalog = PredicateCatalog.For(start.Compilation);
            start.RegisterSyntaxNodeAction(node =>
            {
                var lambda = (LambdaExpressionSyntax)node.Node;
                if (CompoundPredicateFacts.ShouldReport(lambda, node.SemanticModel, catalog))
                {
                    node.ReportDiagnostic(Diagnostic.Create(Rule, lambda.GetLocation()));
                }
            }, SyntaxKind.SimpleLambdaExpression, SyntaxKind.ParenthesizedLambdaExpression);
        });
    }
}
