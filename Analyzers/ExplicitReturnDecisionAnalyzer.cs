using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodingRules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ExplicitReturnDecisionAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.ExplicitReturnDecision,
        "Make return outcomes explicit after a guard clause",
        "Make this return decision explicit after the earlier guard clause",
        "Readability",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "After a conditional early return, return each outcome from an explicit branch.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(AnalyzeBlock, SyntaxKind.Block);
    }

    private static void AnalyzeBlock(SyntaxNodeAnalysisContext context)
    {
        var block = (BlockSyntax)context.Node;
        if (!ReturnDecisionAnalysis.IsFunctionBody(block))
        {
            return;
        }

        if (!ReturnDecisionAnalysis.TryGetFinalReturn(block, out var finalReturn))
        {
            return;
        }

        if (!ReturnDecisionAnalysis.HasTopLevelGuard(block))
        {
            return;
        }

        if (!ReturnDecisionAnalysis.TryGetKind(
                finalReturn,
                context.SemanticModel,
                context.CancellationToken,
                out _))
        {
            return;
        }

        var location = finalReturn.Expression!.GetLocation();
        var diagnostic = Diagnostic.Create(Rule, location);
        context.ReportDiagnostic(diagnostic);
    }
}
