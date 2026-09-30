using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodingRules;

internal static class InlineConditionFixes
{
    public static async Task<bool> IsValidAsync(Document document, ConditionRewritePlan plan, bool extract, CancellationToken token)
    {
        var originalTree = plan.Expression.SyntaxTree;
        var root = ConditionSiteRewrites.Rewrite(plan, extract);
        var changedTree = originalTree.WithRootAndOptions(root, originalTree.Options);
        var compilation = plan.Model.Compilation.ReplaceSyntaxTree(originalTree, changedTree);
        var changedModel = compilation.GetSemanticModel(changedTree);
        var before = CompilerMessages(plan.Model, token);
        var after = CompilerMessages(changedModel, token);
        foreach (var pair in after)
        {
            if (!before.TryGetValue(pair.Key, out var count) || pair.Value > count)
            {
                return false;
            }
        }

        var originalBindings = CallBindings(await document.GetSyntaxRootAsync(token).ConfigureAwait(false), plan.Model, token);
        var changedBindings = CallBindings(changedTree.GetRoot(token), changedModel, token);
        foreach (var pair in originalBindings)
        {
            if (!changedBindings.TryGetValue(pair.Key, out var count) || pair.Value != count)
            {
                return false;
            }
        }

        return true;
    }

    private static Dictionary<string, int> CompilerMessages(SemanticModel model, CancellationToken token) =>
        model.GetDiagnostics(cancellationToken: token).Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error)
            .GroupBy(diagnostic => diagnostic.Id + ":" + diagnostic.GetMessage()).ToDictionary(group => group.Key, group => group.Count());

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
        var changed = document.WithSyntaxRoot(root);
        var options = await changed.GetOptionsAsync(token).ConfigureAwait(false);
        var language = document.Project.Language;
        var configured = options.GetOption(FormattingOptions.NewLine, language);
        var source = await document.GetTextAsync(token).ConfigureAwait(false);
        var text = source.ToString();
        var firstNewline = text.IndexOf('\n');
        var original = firstNewline > 0 && text[firstNewline - 1] == '\r' ? "\r\n" : "\n";
        var config = document.Project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GetOptions(plan.Expression.SyntaxTree);
        var newline = original;
        if (config.TryGetValue("end_of_line", out var configuredConvention))
        {
            newline = configuredConvention switch { "lf" => "\n", "crlf" => "\r\n", "cr" => "\r", _ => configured };
        }
        return await Formatter.FormatAsync(changed, Formatter.Annotation,
            options.WithChangedOption(FormattingOptions.NewLine, language, newline), token).ConfigureAwait(false);
    }
}
