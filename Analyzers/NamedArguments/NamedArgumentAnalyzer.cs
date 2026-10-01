using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodingRules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NamedArgumentAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Rule = new(DiagnosticIds.NamedArgumentRoles, "Name same-type argument roles",
        "Name arguments with ambiguous same-type roles", "Readability", DiagnosticSeverity.Warning, true);
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(node =>
        {
            if (!PipelineAnalyzer.Enabled(Rule.Id, node.Node, node.SemanticModel, node.Options.AnalyzerConfigOptionsProvider, node.CancellationToken)) return;
            var plan = NamedArgumentPlan.Create(node.Node, node.SemanticModel, node.Options.AnalyzerConfigOptionsProvider, node.CancellationToken);
            if (plan is not null) node.ReportDiagnostic(Diagnostic.Create(Rule, plan.Site.List.GetLocation()));
        }, SyntaxKind.InvocationExpression, SyntaxKind.ObjectCreationExpression, SyntaxKind.ImplicitObjectCreationExpression,
            SyntaxKind.BaseConstructorInitializer, SyntaxKind.ThisConstructorInitializer, SyntaxKind.PrimaryConstructorBaseType, SyntaxKind.Attribute);
    }
}
