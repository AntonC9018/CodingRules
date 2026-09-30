using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Formatting;

namespace CodingRules;

internal static class InlineConditionFixes
{
    public static async Task<Document> ApplyAsync(Document document, ConditionRewritePlan plan, bool extract, CancellationToken token)
    {
        var root = ConditionSiteRewrites.Rewrite(plan, extract);
        root = root.ReplaceNodes(root.GetAnnotatedNodes(ConditionSiteRewrites.Generated),
            (_, node) => node.WithAdditionalAnnotations(Formatter.Annotation));
        var changed = document.WithSyntaxRoot(root);
        var options = await changed.GetOptionsAsync(token).ConfigureAwait(false);
        var language = document.Project.Language;
        var configured = options.GetOption(FormattingOptions.NewLine, language);
        var workspaceDefault = document.Project.Solution.Workspace.Options.GetOption(FormattingOptions.NewLine, language);
        var source = await document.GetTextAsync(token).ConfigureAwait(false);
        var text = source.ToString();
        var firstNewline = text.IndexOf('\n');
        var original = firstNewline > 0 && text[firstNewline - 1] == '\r' ? "\r\n" : "\n";
        var newline = string.Equals(configured, workspaceDefault, StringComparison.Ordinal) ? original : configured;
        return await Formatter.FormatAsync(changed, Formatter.Annotation,
            options.WithChangedOption(FormattingOptions.NewLine, language, newline), token).ConfigureAwait(false);
    }
}
