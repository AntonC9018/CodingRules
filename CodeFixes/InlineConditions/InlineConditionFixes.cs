using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Simplification;

namespace CodingRules;

internal static class InlineConditionFixes
{
    public static async Task<bool> IsValidAsync(Document document, ConditionRewritePlan plan, bool extract, CancellationToken token)
    {
        var originalTree = plan.Expression.SyntaxTree;
        var changedDocument = await ApplyAsync(document, plan, extract, token).ConfigureAwait(false);
        var originalText = await document.GetTextAsync(token).ConfigureAwait(false);
        var changedText = await changedDocument.GetTextAsync(token).ConfigureAwait(false);
        if (originalText.ContentEquals(changedText))
        {
            return false;
        }

        var changedModel = SavedSource.Compile(plan.Model, changedText, token);
        if (SavedSource.AddsCompilerMessages(plan.Model, changedModel, token))
        {
            return false;
        }

        var originalBindings = CallBindings(await document.GetSyntaxRootAsync(token).ConfigureAwait(false), plan.Model, token);
        // Flow-preserving OR guards duplicate a terminal statement syntactically.
        // Only the first reached guard executes it, just as in the original OR.
        if (!extract && plan.NeedsFlowPreservingIf && plan.Statement is IfStatementSyntax flowIf
            && Parentheses.Unwrap(plan.Expression) is BinaryExpressionSyntax disjunction
            && disjunction.IsKind(SyntaxKind.LogicalOrExpression))
        {
            var leaves = new List<ExpressionSyntax>();
            ConditionFacts.CollectChain(disjunction, SyntaxKind.LogicalOrExpression, leaves);
            foreach (var pair in CallBindings(flowIf.Statement, plan.Model, token))
            {
                originalBindings[pair.Key] += pair.Value * (leaves.Count - 1);
            }
        }

        var changedBindings = CallBindings(changedModel.SyntaxTree.GetRoot(token), changedModel, token);
        foreach (var pair in originalBindings)
        {
            if (!changedBindings.TryGetValue(pair.Key, out var count) || pair.Value != count)
            {
                return false;
            }
        }

        var formattedRoot = await changedDocument.GetSyntaxRootAsync(token).ConfigureAwait(false);
        var parsedRoot = changedModel.SyntaxTree.GetRoot(token);
        foreach (var generated in formattedRoot!.GetAnnotatedNodes(ConditionEvaluator.Linearized))
        {
            var expression = parsedRoot.FindNode(generated.Span, getInnermostNodeForTie: true) as ExpressionSyntax;
            if (expression is null || ConditionFacts.Create(expression, changedModel, token)?.HasReason == true)
            {
                return false;
            }
        }

        if (CountViolations(changedModel.SyntaxTree.GetRoot(token), changedModel, token)
            >= CountViolations(originalTree.GetRoot(token), plan.Model, token))
        {
            return false;
        }

        return true;
    }

    private static int CountViolations(SyntaxNode root, SemanticModel model, CancellationToken token) =>
        root.DescendantNodes().Select(ConditionHosts.GetExpression).Where(expression => expression is not null)
            .Count(expression => ConditionFacts.Create(expression!, model, token)?.HasReason == true);

    private static Dictionary<string, int> CallBindings(SyntaxNode? root, SemanticModel model, CancellationToken token) =>
        root!.DescendantNodes().Where(node => node is InvocationExpressionSyntax or ObjectCreationExpressionSyntax)
            .Select(node => model.GetSymbolInfo(node, token).Symbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
            .Where(binding => binding is not null).GroupBy(binding => binding!)
            .ToDictionary(group => group.Key, group => group.Count());

    public static async Task<Document> ApplyAsync(Document document, ConditionRewritePlan plan, bool extract, CancellationToken token)
    {
        var root = ConditionSiteRewrites.Rewrite(plan, extract);
        root = root.ReplaceNodes(root.GetAnnotatedNodes(ConditionSiteRewrites.Generated),
            (_, node) => node.WithAdditionalAnnotations(Formatter.Annotation));
        root = root.ReplaceNodes(root.GetAnnotatedNodes(EvaluationSyntax.GeneratedType),
            (_, node) => node.WithAdditionalAnnotations(Simplifier.Annotation));
        var changed = document.WithSyntaxRoot(root);
        changed = await Simplifier.ReduceAsync(changed, Simplifier.Annotation, cancellationToken: token).ConfigureAwait(false);
        return await GeneratedSyntaxFormatter.FormatAsync(changed, document, token).ConfigureAwait(false);
    }
}
