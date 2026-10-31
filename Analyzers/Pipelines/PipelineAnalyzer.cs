using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodingRules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PipelineAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor ProjectionRule = Rule(DiagnosticIds.ComposedProjection, "Name projection stages", "Calculate this projection in named stages");
    public static readonly DiagnosticDescriptor HelperRule = Rule(DiagnosticIds.StageImplementation, "Extract stage implementation", "Move this stage implementation to a local function");
    public static readonly DiagnosticDescriptor SelectorsRule = Rule(DiagnosticIds.ExplicitCollectionSelectors, "Make collection selectors explicit", "Name the key and element selectors explicitly");
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(ProjectionRule, HelperRule, SelectorsRule);
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var catalog = PipelineCatalog.For(start.Compilation);
            start.RegisterSyntaxNodeAction(node => Analyze(node, catalog), SyntaxKind.SimpleLambdaExpression, SyntaxKind.ParenthesizedLambdaExpression, SyntaxKind.InvocationExpression);
        });
    }

    private static void Analyze(SyntaxNodeAnalysisContext context, PipelineCatalog catalog)
    {
        if (context.Node is LambdaExpressionSyntax lambda)
        {
            var plan = ProjectionPlan.Create(lambda, context.SemanticModel, catalog, context.CancellationToken);
            if (plan is null) return;
            var rule = lambda.Body is BlockSyntax ? HelperRule : ProjectionRule;
            if (Enabled(rule.Id, lambda, context.SemanticModel, context.Options.AnalyzerConfigOptionsProvider, context.CancellationToken))
                context.ReportDiagnostic(Diagnostic.Create(rule, lambda.GetLocation()));
        }
        else if (context.Node is InvocationExpressionSyntax call && Enabled(SelectorsRule.Id, call, context.SemanticModel,
                     context.Options.AnalyzerConfigOptionsProvider, context.CancellationToken)
                 && SelectorPlan.Create(call, context.SemanticModel, catalog) is not null)
            context.ReportDiagnostic(Diagnostic.Create(SelectorsRule, call.GetLocation()));
    }

    private static bool Enabled(string id, SyntaxNode node, SemanticModel model, AnalyzerConfigOptionsProvider options, CancellationToken token) =>
        RuleSeverity.IsEnabled(id, node.SyntaxTree, model.Compilation, options, token);

    private static DiagnosticDescriptor Rule(string id, string title, string message) => new(id, title, message, "Readability", DiagnosticSeverity.Warning, true);
}
