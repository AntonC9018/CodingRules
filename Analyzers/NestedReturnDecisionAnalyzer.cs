using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodingRules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NestedReturnDecisionAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor TernaryRule = new(
        DiagnosticIds.NestedTernaryReturn,
        "Make nested ternary return decisions explicit",
        "Make this nested ternary return decision explicit with if statements",
        "Readability",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A ternary return nested in deeper control flow should choose its outcome with explicit if statements.");

    public static readonly DiagnosticDescriptor CoalesceRule = new(
        DiagnosticIds.NestedCoalesceReturn,
        "Make nested null-coalescing return decisions explicit",
        "Make this nested null-coalescing return decision explicit with if statements",
        "Readability",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A null-coalescing return nested in deeper control flow should choose its outcome with explicit if statements.");

    public static readonly DiagnosticDescriptor NullableCallRule = new(
        DiagnosticIds.NestedNullableCallReturn,
        "Make nested nullable-returning call decisions explicit",
        "Make this nested nullable-returning call decision explicit with if statements",
        "Readability",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A nullable-returning call nested in deeper control flow should decide between the found value and null with explicit if statements.");

    public static readonly DiagnosticDescriptor BooleanRule = new(
        DiagnosticIds.NestedBooleanReturn,
        "Make nested boolean return decisions explicit",
        "Make this nested boolean return decision explicit with if statements",
        "Readability",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A boolean return nested in deeper control flow should choose its outcome with explicit if statements.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(TernaryRule, CoalesceRule, NullableCallRule, BooleanRule);

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

        foreach (var nestedReturn in ReturnDecisionAnalysis.CollectNestedReturns(block))
        {
            if (!ReturnDecisionAnalysis.TryGetNestedKind(
                    nestedReturn,
                    context.SemanticModel,
                    context.CancellationToken,
                    out var kind))
            {
                continue;
            }

            var rule = kind switch
            {
                ReturnDecisionKind.Conditional => TernaryRule,
                ReturnDecisionKind.Coalesce => CoalesceRule,
                ReturnDecisionKind.Boolean => BooleanRule,
                ReturnDecisionKind.NullableCall => NullableCallRule,
                _ => null,
            };

            if (rule is null)
            {
                continue;
            }

            var location = nestedReturn.Expression!.GetLocation();
            context.ReportDiagnostic(Diagnostic.Create(rule, location));
        }
    }
}
