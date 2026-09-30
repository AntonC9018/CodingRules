using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodingRules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InlineConditionAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor MixedRule = CreateRule(DiagnosticIds.MixedConditionOperators,
        "Make mixed Boolean conditions explicit", "Make this mixed Boolean condition explicit");
    public static readonly DiagnosticDescriptor ExcessRule = CreateRule(DiagnosticIds.ExcessConditionChecks,
        "Split long Boolean conditions", "Split this condition into simple checks");
    public static readonly DiagnosticDescriptor IndependentRule = CreateRule(DiagnosticIds.IndependentConditionChecks,
        "Separate independent checks", "Separate checks on different values");
    public static readonly DiagnosticDescriptor OperationsRule = CreateRule(DiagnosticIds.CombinedConditionOperations,
        "Separate condition operations", "Separate the operations in this condition");
    public static readonly DiagnosticDescriptor AlternativesRule = CreateRule(DiagnosticIds.RepeatedConditionAlternatives,
        "Linearize repeated alternatives", "Move repeated alternatives into linear logic");

    internal static readonly ImmutableArray<DiagnosticDescriptor> Priority =
        ImmutableArray.Create(MixedRule, OperationsRule, AlternativesRule, ExcessRule, IndependentRule);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => Priority;

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.IfStatement, SyntaxKind.EqualsValueClause,
            SyntaxKind.ReturnStatement, SyntaxKind.ArrowExpressionClause, SyntaxKind.SimpleLambdaExpression,
            SyntaxKind.ParenthesizedLambdaExpression, SyntaxKind.SimpleAssignmentExpression, SyntaxKind.Argument,
            SyntaxKind.ConditionalExpression, SyntaxKind.WhileStatement, SyntaxKind.ForStatement,
            SyntaxKind.DoStatement, SyntaxKind.CatchFilterClause, SyntaxKind.WhenClause);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var expression = ConditionHosts.GetExpression(context.Node);
        if (expression is null)
        {
            return;
        }

        var facts = ConditionFacts.Create(expression, context.SemanticModel, context.CancellationToken);
        if (facts is null || !facts.HasReason)
        {
            return;
        }

        DiagnosticDescriptor? rule = null;
        foreach (var candidate in Priority)
        {
            if (!facts.Reasons.Contains(candidate.Id))
            {
                continue;
            }

            var severity = context.Compilation.Options.SyntaxTreeOptionsProvider?.TryGetDiagnosticValue(
                expression.SyntaxTree, candidate.Id, context.CancellationToken, out var treeSeverity) == true
                ? treeSeverity
                : context.Compilation.Options.SpecificDiagnosticOptions.TryGetValue(candidate.Id, out var configured)
                    ? configured : ReportDiagnostic.Default;
            var options = context.Options.AnalyzerConfigOptionsProvider.GetOptions(expression.SyntaxTree);
            if (severity == ReportDiagnostic.Suppress
                || options.TryGetValue("dotnet_diagnostic." + candidate.Id + ".severity", out var value)
                    && value == "none")
            {
                continue;
            }

            rule = candidate;
            break;
        }

        if (rule is null)
        {
            return;
        }

        var plan = ConditionRewritePlan.Create(expression, context.SemanticModel, context.CancellationToken);
        if (plan is null || !plan.CanExtract && !plan.CanExpand)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(rule, expression.GetLocation()));
    }

    private static DiagnosticDescriptor CreateRule(string id, string title, string message) =>
        new(id, title, message, "Readability", DiagnosticSeverity.Warning, isEnabledByDefault: true);
}
