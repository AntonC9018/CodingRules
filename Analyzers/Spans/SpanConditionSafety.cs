using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodingRules;

internal static class SpanConditionSafety
{
    public static bool IntroducesReason(SpanPlan plan, SemanticModel model, AnalyzerOptions options, CancellationToken token)
    {
        foreach (var host in plan.Inspection.Ancestors())
        {
            var expression = ConditionHosts.GetExpression(host);
            if (expression is not null && expression.Span.Contains(plan.Inspection.Span)
                && ConditionFacts.Create(expression, model, token) is { Leaves.Count: > 2 } facts)
            {
                // The opaque scalar helper preserves Boolean structure and leaf count.
                // It removes the framework trim's producing category and, for an
                // entire equality leaf, that leaf's recognized validation category.
                var producing = facts.Leaves.Where(leaf => ConditionFacts.EvaluationNodes(leaf)
                    .Any(node => node != plan.Trim && ConditionFacts.IsProducer(node, model))).ToArray();
                bool Validates(ExpressionSyntax leaf) => !plan.Inspection.Span.Contains(leaf.Span) && ConditionFacts.HasValidation(leaf, model);
                var stillCombined = producing.Any(Validates) || producing.Length != 0 && facts.Leaves.Any(leaf =>
                    !producing.Contains(leaf) && Validates(leaf) && !ConditionFacts.IsNullGuard(leaf));
                var surviving = new HashSet<string>(facts.Reasons);
                if (!stillCombined) surviving.Remove(DiagnosticIds.CombinedConditionOperations);
                var before = Reason(facts.Reasons, expression, model.Compilation, options, token);
                var after = Reason(surviving, expression, model.Compilation, options, token);
                if (after is not null && (after != before || ConditionRewritePlan.Create(expression, model, token) is not { } rewrite
                    || !rewrite.CanExtract && !rewrite.CanExpand)) return true;
            }
            if (host is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or MemberDeclarationSyntax) break;
        }
        return false;
    }

    private static string? Reason(HashSet<string> reasons, ExpressionSyntax expression, Compilation compilation, AnalyzerOptions options, CancellationToken token)
    {
        foreach (var rule in InlineConditionAnalyzer.Priority)
        {
            if (!reasons.Contains(rule.Id)) continue;
            var severity = compilation.Options.SyntaxTreeOptionsProvider?.TryGetDiagnosticValue(expression.SyntaxTree, rule.Id, token, out var treeSeverity) == true
                ? treeSeverity : compilation.Options.SpecificDiagnosticOptions.TryGetValue(rule.Id, out var configured) ? configured : ReportDiagnostic.Default;
            var config = options.AnalyzerConfigOptionsProvider.GetOptions(expression.SyntaxTree);
            if (severity == ReportDiagnostic.Suppress || config.TryGetValue("dotnet_diagnostic." + rule.Id + ".severity", out var value) && value == "none") continue;
            return rule.Id;
        }
        return null;
    }
}
