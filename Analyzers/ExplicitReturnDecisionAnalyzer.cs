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
        "Make return outcomes explicit",
        "Make this return decision explicit with if statements",
        "Readability",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Return ternary outcomes from explicit branches. After an earlier guard, other compound return decisions also require explicit outcomes.");

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

        if (!ReturnDecisionAnalysis.HasTopLevelGuard(block)
            && Parentheses.Unwrap(finalReturn.Expression!) is not ConditionalExpressionSyntax)
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
