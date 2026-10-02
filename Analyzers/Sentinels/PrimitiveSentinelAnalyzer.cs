using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodingRules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PrimitiveSentinelAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor ApiRule = new(DiagnosticIds.PrimitiveSentinelReturn, "Represent absence in the return type",
        "Represent absence in the return type", "Readability", DiagnosticSeverity.Warning, true);
    public static readonly DiagnosticDescriptor ForwardRule = new(DiagnosticIds.UncheckedHelperSentinel, "Check a helper sentinel before forwarding",
        "Check a helper sentinel before forwarding", "Readability", DiagnosticSeverity.Warning, true);
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(ApiRule, ForwardRule);
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var summaries = new SentinelSummaries(start.Compilation);
            start.RegisterSyntaxNodeAction(node =>
            {
                var host = SentinelHost.Create(node.Node, node.SemanticModel, node.CancellationToken);
                if (host is null || host.Imposed(node.SemanticModel, node.CancellationToken)) return;
                var result = summaries.Get(host, node.SemanticModel, node.CancellationToken);
                if (host.IsApi && (result.Outcomes & SearchOutcomes.Sentinel) != 0
                    && PipelineAnalyzer.Enabled(ApiRule.Id, node.Node, node.SemanticModel, node.Options.AnalyzerConfigOptionsProvider, node.CancellationToken))
                { node.ReportDiagnostic(Diagnostic.Create(ApiRule, host.Type.GetLocation())); return; }
                if (!PipelineAnalyzer.Enabled(ForwardRule.Id, node.Node, node.SemanticModel, node.Options.AnalyzerConfigOptionsProvider, node.CancellationToken)) return;
                foreach (var site in result.Returns)
                    if (SentinelPlan.Feasible(site, host, node.SemanticModel, node.CancellationToken))
                        node.ReportDiagnostic(Diagnostic.Create(ForwardRule, site.Expression.GetLocation()));
            }, SyntaxKind.MethodDeclaration, SyntaxKind.LocalFunctionStatement, SyntaxKind.GetAccessorDeclaration,
                SyntaxKind.PropertyDeclaration, SyntaxKind.IndexerDeclaration, SyntaxKind.OperatorDeclaration,
                SyntaxKind.ConversionOperatorDeclaration, SyntaxKind.SimpleLambdaExpression, SyntaxKind.ParenthesizedLambdaExpression, SyntaxKind.AnonymousMethodExpression);
        });
    }
}
